using Charloom.Core;

namespace Charloom.Services;

public sealed record ImageSourceSnapshot(byte[] Bytes, string Revision, string Encoded,
    ImageFrame Frame, byte[] OriginalPreview, string Title);

/// <summary>Per-project input and preview state. Contains no UI objects.</summary>
public sealed class ImageSourceState
{
    public byte[]? Source { get; set; }
    public string Revision { get; set; } = "";
    public string? Encoded { get; set; }
    public byte[]? OriginalPreview { get; set; }
    public (byte[] pixels, int width, int height) Decoded { get; set; }
    public string Title { get; set; } = "Image";
    public int LoadVersion { get; set; }
    public int ThumbnailVersion { get; set; }
    public SemaphoreSlim ThumbnailGate { get; } = new(1, 1);
    public void Apply(ImageSourceSnapshot snapshot)
    {
        Source = snapshot.Bytes; Revision = snapshot.Revision; Encoded = snapshot.Encoded;
        Decoded = (snapshot.Frame.Pixels, snapshot.Frame.Width, snapshot.Frame.Height);
        OriginalPreview = snapshot.OriginalPreview; Title = snapshot.Title;
    }
}
