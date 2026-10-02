using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class HistoryTests
{
    [Fact(DisplayName = "bounded creation history merge undo redo and divergence")]
    public void BoundedCreationHistoryMergeUndoRedoAndDivergence()
    {
        var history = new BoundedHistory<string>(text => text.Length * 2, 3, 1024);
        history.Push("initial"); history.Push("a"); history.Push("ab", merge: true);
        Assert.True(history.Count == 2 && history.Undo() == "initial", "Typing transaction did not merge");
        Assert.True(history.Redo() == "ab", "Redo failed"); history.Undo(); history.Push("different");
        Assert.True(!history.CanRedo && history.PeekUndo() == "initial", "Divergent change retained redo");
        history.Push("three"); history.Push("four"); history.Push("five");
        Assert.True(history.Count == 4 && history.Undo() == "four", "Step limit changed current state");
    }

    [Fact(DisplayName = "bounded creation history budgets retain usable current state")]
    public void BoundedCreationHistoryBudgetsRetainUsableCurrentState()
    {
        var history = new BoundedHistory<string>(text => text.Length * 2, 100, 12);
        history.Push("one"); history.Push("two"); history.Push("six");
        Assert.True(history.Count == 2 && history.RetainedBytes == 12, "Budget was not enforced");
        history.Push(new string('x', 20));
        Assert.True(history.Count == 1 && !history.CanUndo && history.Current.Length == 20, "Oversized current state lost");
        try { history.Undo(); throw new Exception("Empty undo accepted"); } catch (InvalidOperationException) { }
    }
}
