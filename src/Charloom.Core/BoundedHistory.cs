namespace Charloom.Core;

/// <summary>Retains the current state even when it alone exceeds the undo budget.</summary>
public sealed class BoundedHistory<T>(Func<T, long> estimate, int maximumSteps = 100, long maximumBytes = 64L * 1024 * 1024)
{
    private readonly List<(T Value, long Bytes)> states = [];
    private int index = -1;
    public bool HasCurrent => index >= 0;
    public T Current => HasCurrent ? states[index].Value : throw new InvalidOperationException("History is empty.");
    public bool CanUndo => index > 0;
    public bool CanRedo => index >= 0 && index < states.Count - 1;
    public int Count => states.Count;
    public long RetainedBytes => states.Sum(state => state.Bytes);

    public void Push(T value, bool merge = false)
    {
        var bytes = estimate(value);
        if (bytes < 0 || maximumSteps < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(value));
        if (CanRedo) states.RemoveRange(index + 1, states.Count - index - 1);
        if (merge && HasCurrent) states[index] = (value, bytes);
        else { states.Add((value, bytes)); index++; }
        var retained = RetainedBytes;
        while (states.Count > 1 && (states.Count > maximumSteps + 1 || retained > maximumBytes))
        {
            retained -= states[0].Bytes; states.RemoveAt(0); index--;
        }
    }

    public T PeekUndo() => CanUndo ? states[index - 1].Value : throw new InvalidOperationException("Nothing to undo.");
    public T PeekRedo() => CanRedo ? states[index + 1].Value : throw new InvalidOperationException("Nothing to redo.");
    public T Undo() { var target = PeekUndo(); index--; return target; }
    public T Redo() { var target = PeekRedo(); index++; return target; }
}
