namespace Charloom.Services;

/// <summary>Checks the length of the opened handle, not a separate path lookup.</summary>
public static class BoundedFile
{
    public static ReadOnlyMemory<byte> JsonBytes(byte[] bytes)
    {
        // File.ReadAllText previously recognized these BOMs in legacy JSON.
        var span = bytes.AsSpan();
        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return bytes.AsMemory(3);
        System.Text.Encoding? encoding = null; var offset = 0;
        if (span.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { encoding = new System.Text.UTF32Encoding(false, false, true); offset = 4; }
        else if (span.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { encoding = new System.Text.UTF32Encoding(true, false, true); offset = 4; }
        else if (span.StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new System.Text.UnicodeEncoding(false, false, true); offset = 2; }
        else if (span.StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new System.Text.UnicodeEncoding(true, false, true); offset = 2; }
        return encoding is null ? bytes : System.Text.Encoding.UTF8.GetBytes(encoding.GetString(span[offset..]));
    }
    public static byte[] Read(string path, int maximumBytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = Allocate(file, maximumBytes);
        file.ReadExactly(bytes);
        return bytes;
    }

    public static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        var bytes = Allocate(file, maximumBytes);
        await file.ReadExactlyAsync(bytes, token);
        return bytes;
    }

    private static byte[] Allocate(FileStream file, int maximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (file.Length > maximumBytes) throw new InvalidDataException($"文件超过 {maximumBytes:N0} 字节上限。");
        return new byte[checked((int)file.Length)];
    }
}
