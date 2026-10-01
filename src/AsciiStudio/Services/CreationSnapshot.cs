using AsciiStudio.Core;

namespace AsciiStudio.Services;

public sealed record CreationSnapshot(StudioProject Project, bool Edited, AsciiDocument? Generated)
{
    public static CreationSnapshot Capture(StudioProject project, bool edited, AsciiDocument? generated)
    {
        project.Document.Validate();
        var document = project.Document with { Colors = project.Document.Colors?.ToArray(), BackgroundColors = project.Document.BackgroundColors?.ToArray() };
        return new(project with { Document = document, Parameters = project.Parameters is null ? null : new(project.Parameters), Edited = edited, GeneratedDocument = generated }, edited, generated);
    }

    public long EstimateBytes() => 512L + Project.Document.Text.Length * 2L + (Project.Document.Colors?.LongLength ?? 0) * 4
        + (Project.Document.BackgroundColors?.LongLength ?? 0) * 4 + (Project.SourceImage?.Length ?? 0) * 2L
        + (Project.SourceText?.Length ?? 0) * 2L + (Generated is { } original && !ReferenceEquals(original, Project.Document) ? original.Text.Length * 2L + (original.Colors?.LongLength ?? 0) * 4 + (original.BackgroundColors?.LongLength ?? 0) * 4 : 0)
        + (Project.Parameters?.Sum(p => (p.Key.Length + p.Value.Length) * 2L) ?? 0);
}
