using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Charloom.Core;

namespace Charloom.Services;

public interface IClipboardService
{
    Task<string> ReadText(CancellationToken token);
    Task<byte[]> ReadPng(CancellationToken token);
    Task WriteText(string text, CancellationToken token);
}

public sealed class ClipboardBusyException() : IOException("Windows clipboard is busy; retry when the other application releases it.");
public sealed class ClipboardContentException(string message) : ArgumentException(message);

/// <summary>Eager Win32 clipboard transfers; no WinUI dispatcher or clipboard monitoring.</summary>
public sealed class WindowsClipboardService : IClipboardService
{
    public const int MaximumTextBytes = 8_000_000;
    public const int MaximumImageBytes = ImageResourceLimits.FileBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);

    public static void ValidateText(string text)
    {
        if (text.Contains('\0')) throw new ClipboardContentException("Clipboard text cannot contain NUL characters.");
        if (Utf8.GetByteCount(text) > MaximumTextBytes) throw new ClipboardContentException("Clipboard text exceeds the 8MB UTF-8 budget.");
    }

    public static void ValidatePng(byte[] bytes)
    {
        if (bytes.Length > MaximumImageBytes) throw new ClipboardContentException("Clipboard image exceeds the 100MB PNG budget.");
        if (bytes.Length < 33 || !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) != 13)
            throw new ClipboardContentException("Clipboard PNG payload is not a PNG image.");
        // Reject extreme dimensions from IHDR before asking GDI+ to decode pixels.
        ValidateDimensions(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, true, true);
            ValidateDimensions(image.Width, image.Height);
            if (image.RawFormat.Guid != ImageFormat.Png.Guid) throw new ClipboardContentException("Clipboard PNG payload is not a PNG image.");
        }
        catch (Exception error) when (error is ArgumentException and not ClipboardContentException or ExternalException)
        { throw new ClipboardContentException("Clipboard PNG data is damaged or unsupported."); }
    }

    public Task<string> ReadText(CancellationToken token) => Task.Run(() => WithClipboard(() =>
    {
        if (!IsClipboardFormatAvailable(13)) throw new ClipboardContentException("Clipboard contains no Unicode text.");
        var bytes = CopyMemory(GetClipboardData(13), MaximumTextBytes * 2 + 16);
        var end = -1;
        for (var i = 0; i + 1 < bytes.Length; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        if (end < 0) throw new ClipboardContentException("Clipboard text is not terminated correctly.");
        var text = Utf16.GetString(bytes, 0, end); ValidateText(text); return text;
    }, token), token);

    public Task WriteText(string text, CancellationToken token)
    {
        ValidateText(text);
        var bytes = Utf16.GetBytes(text + "\0");
        return Task.Run(() =>
        {
            var handle = GlobalAlloc(2, (nuint)bytes.Length);
            if (handle == 0) throw new IOException("Cannot allocate clipboard memory.");
            try
            {
                var pointer = GlobalLock(handle);
                if (pointer == 0) throw new IOException("Cannot lock clipboard memory.");
                try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
                finally { GlobalUnlock(handle); }
                WithClipboard(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!EmptyClipboard()) throw new IOException("Cannot prepare clipboard for writing.");
                    if (SetClipboardData(13, handle) == 0) throw new IOException("Cannot transfer clipboard text.");
                    handle = 0; // Windows now owns this allocation; never free it here.
                    return true;
                }, token);
            }
            finally { if (handle != 0) GlobalFree(handle); }
        }, token);
    }

    public Task<byte[]> ReadPng(CancellationToken token) => Task.Run(() =>
    {
        Bitmap? bitmap = null;
        try
        {
            var bytes = WithClipboard(() =>
            {
                var png = RegisterClipboardFormatW("PNG");
                if (png == 0) throw new IOException("Cannot register the PNG clipboard format.");
                if (IsClipboardFormatAvailable(png)) return CopyMemory(GetClipboardData(png), MaximumImageBytes);
                // ponytail: CF_BITMAP has no alpha guarantee; add CF_DIBV5 decoding
                // if transparent non-PNG clipboard sources require it.
                if (!IsClipboardFormatAvailable(2)) throw new ClipboardContentException("Clipboard contains no supported image (PNG or Windows bitmap).");
                var handle = GetClipboardData(2);
                if (handle == 0 || GetObjectW(handle, Marshal.SizeOf<BitmapInfo>(), out var info) == 0)
                    throw new ClipboardContentException("Cannot read clipboard bitmap dimensions.");
                ValidateDimensions(info.Width, info.Height);
                bitmap = Image.FromHbitmap(handle); // Copies pixels; the clipboard retains its HBITMAP.
                return (byte[]?)null;
            }, token);
            token.ThrowIfCancellationRequested();
            if (bytes is not null)
            {
                ValidatePng(bytes);
                return bytes;
            }
            using var encoded = new MemoryStream(); bitmap!.Save(encoded, ImageFormat.Png);
            if (encoded.Length > MaximumImageBytes) throw new ClipboardContentException("Clipboard image exceeds the 100MB PNG budget.");
            token.ThrowIfCancellationRequested(); return encoded.ToArray();
        }
        finally { bitmap?.Dispose(); }
    }, token);

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > ImageResourceLimits.SourcePixels)
            throw new ClipboardContentException("Clipboard image dimensions exceed the 200 million pixel budget.");
    }
    private static byte[] CopyMemory(nint handle, int maximum)
    {
        if (handle == 0) throw new ClipboardContentException("Clipboard data is unavailable.");
        var length = GlobalSize(handle);
        if (length == 0 || length > (nuint)maximum) throw new ClipboardContentException("Clipboard data exceeds its byte budget or is empty.");
        var pointer = GlobalLock(handle);
        if (pointer == 0) throw new IOException("Cannot lock clipboard data.");
        try { var bytes = new byte[(int)length]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { GlobalUnlock(handle); }
    }
    private static T WithClipboard<T>(Func<T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // A real owner is required for EmptyClipboard/SetClipboardData. This
        // message-only window uses eager data, so no delayed-render message pump.
        var owner = CreateWindowExW(0, "STATIC", "Charloom Clipboard", 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);
        if (owner == 0) throw new IOException("Cannot create the clipboard owner window.");
        var opened = false;
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (OpenClipboard(owner)) { opened = true; break; }
                if (attempt < 4 && token.WaitHandle.WaitOne(50)) token.ThrowIfCancellationRequested();
            }
            if (!opened) throw new ClipboardBusyException();
            token.ThrowIfCancellationRequested(); return action();
        }
        finally { if (opened) CloseClipboard(); DestroyWindow(owner); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint handle, int size, out BitmapInfo value);
}
