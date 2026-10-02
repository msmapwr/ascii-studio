using AsciiStudio.Controls;
using AsciiStudio.Core;

namespace AsciiStudio.Services;

public interface IProjectSessionPage
{
    ResultPane ResultPane { get; }
    string SessionMode { get; }
    Task LoadProject(StudioProject project);
    void SetSession(string id, string? path);
    Task<bool> SaveProjectAsync();
    Task SaveRecoveryAsync();
    event Action<bool>? DirtyChanged;
    event Action<AsciiDocument>? DocumentChanged;
}
