using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml.Controls;

namespace AsciiStudio.Controls;

public sealed class SnapshotProjectPage : Grid, IProjectSessionPage
{
    private readonly ResultPane result = new();
    public SnapshotProjectPage() => Children.Add(result);
    public ResultPane ResultPane => result;
    public string SessionMode => "snapshot";
    public event Action<bool>? DirtyChanged { add => result.DirtyChanged += value; remove => result.DirtyChanged -= value; }
    public event Action<AsciiDocument>? DocumentChanged { add => result.DocumentChanged += value; remove => result.DocumentChanged -= value; }
    public void SetSession(string id, string? path) => result.SetSession(id, path);
    public Task<bool> SaveProjectAsync() => result.SaveProjectAsync();
    public Task SaveRecoveryAsync() => result.SaveRecoveryAsync();
    public Task LoadProject(StudioProject project) => result.LoadDocument(project);
}
