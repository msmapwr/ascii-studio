using Xunit;

namespace Charloom.Creation.Tests;

[CollectionDefinition("Workspace")]
public sealed class WorkspaceCollection : ICollectionFixture<WorkspaceTestScope> { }

public sealed class WorkspaceTestScope : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "Charloom-native-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? previous = Environment.GetEnvironmentVariable("CHARLOOM_DATA_DIRECTORY");
    public WorkspaceTestScope()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Environment.SetEnvironmentVariable("CHARLOOM_DATA_DIRECTORY", Directory);
    }
    public string FilePath(string name) => Path.Combine(Directory, name);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CHARLOOM_DATA_DIRECTORY", previous);
        if (!Path.GetFullPath(Directory).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test directory.");
        System.IO.Directory.Delete(Directory, true);
    }
}
