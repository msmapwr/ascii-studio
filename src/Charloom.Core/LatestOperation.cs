namespace Charloom.Core;

/// <summary>UI-thread-owned operation lifetime. Superseded work cannot commit a result.</summary>
public sealed class LatestOperation
{
    private Lease? current;
    public bool IsRunning => current?.IsCurrent == true;
    public Lease Begin()
    {
        Cancel();
        return current = new Lease(this);
    }
    public void Cancel() => current?.Cancel();
    public bool HasNewer(Lease lease) => current is not null && !ReferenceEquals(current, lease);
    public sealed class Lease : IDisposable
    {
        private readonly LatestOperation owner;
        private readonly CancellationTokenSource source = new();
        private bool disposed;
        internal Lease(LatestOperation owner) => this.owner = owner;
        public CancellationToken Token => source.Token;
        public bool IsCurrent => !disposed && ReferenceEquals(owner.current, this) && !source.IsCancellationRequested;
        internal void Cancel() { if (!disposed) source.Cancel(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(owner.current, this)) owner.current = null;
            source.Dispose();
        }
    }
}
