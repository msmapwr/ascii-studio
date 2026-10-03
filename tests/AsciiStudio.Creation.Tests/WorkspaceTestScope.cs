using Xunit;

namespace AsciiStudio.Creation.Tests;

[CollectionDefinition("Workspace")]
public sealed class WorkspaceCollection : ICollectionFixture<WorkspaceTestScope> { }

public sealed class WorkspaceTestScope : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AsciiStudio-native-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? previous = Environment.GetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY");
    public WorkspaceTestScope()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", Directory);
    }
    public string FilePath(string name) => Path.Combine(Directory, name);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", previous);
        if (!Path.GetFullPath(Directory).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test directory.");
        System.IO.Directory.Delete(Directory, true);
    }
}
