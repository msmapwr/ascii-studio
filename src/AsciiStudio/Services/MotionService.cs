using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;

namespace AsciiStudio.Services;

public static class MotionService
{
    private sealed class AnimationState { public int Version; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, AnimationState> states = new();
    private static readonly Windows.UI.ViewManagement.UISettings system = new();
    private static readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    public static bool Enabled => WorkspaceService.Settings.Animations && system.AnimationsEnabled && !accessibility.HighContrast;

    public static async Task Fade(UIElement element, float from, float to, int milliseconds = 180)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var state = states.GetOrCreateValue(element); var version = ++state.Version;
        visual.StopAnimation("Opacity");
        if (!Enabled) { visual.Opacity = to; return; }
        visual.Opacity = from;
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, from); animation.InsertKeyFrame(1, to);
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        using var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        batch.Completed += (_, _) => finished.TrySetResult();
        visual.StartAnimation("Opacity", animation); batch.End();
        await finished.Task;
        if (version == state.Version) visual.Opacity = to;
    }
}
