namespace AsciiStudio.Core;

/// <summary>Shared LRU with independent owner and global retained-byte budgets.</summary>
public sealed class BudgetCache(long ownerBudget = 64L * 1024 * 1024, long totalBudget = 256L * 1024 * 1024)
{
    private sealed record Entry(string Owner, object Key, object Value, long Bytes);
    private readonly object gate = new();
    private readonly Dictionary<(string, object), LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> lru = new();
    private readonly Dictionary<string, long> owners = [];
    private long retained;
    public long RetainedBytes { get { lock (gate) return retained; } }
    public long OwnerBytes(string owner) { lock (gate) return owners.GetValueOrDefault(owner); }
    public T? Get<T>(string owner, object key) where T : class
    {
        lock (gate)
        {
            if (!entries.TryGetValue((owner, key), out var node)) return null;
            lru.Remove(node); lru.AddLast(node); return node.Value.Value as T;
        }
    }
    public void Put(string owner, object key, object value, long bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(value);
        if (bytes < 0 || ownerBudget <= 0 || totalBudget <= 0) throw new ArgumentException("缓存预算无效。");
        bytes = checked(bytes + 128); // entry/index overhead is included conservatively
        lock (gate)
        {
            if (entries.TryGetValue((owner, key), out var old)) Remove(old);
            if (bytes > ownerBudget || bytes > totalBudget) return;
            while (owners.GetValueOrDefault(owner) + bytes > ownerBudget)
            {
                var node = lru.First;
                while (node is not null && node.Value.Owner != owner) node = node.Next;
                if (node is null) break;
                Remove(node);
            }
            while (lru.First is { } first && (retained + bytes > totalBudget || entries.Count >= 2048)) Remove(first);
            var next = lru.AddLast(new Entry(owner, key, value, bytes)); entries[(owner, key)] = next;
            retained += bytes; owners[owner] = owners.GetValueOrDefault(owner) + bytes;
        }
    }
    public void RemoveOwner(string owner)
    {
        lock (gate)
        {
            for (var node = lru.First; node is not null;)
            {
                var next = node.Next; if (node.Value.Owner == owner) Remove(node); node = next;
            }
        }
    }
    private void Remove(LinkedListNode<Entry> node)
    {
        var entry = node.Value; entries.Remove((entry.Owner, entry.Key)); lru.Remove(node); retained -= entry.Bytes;
        var bytes = owners[entry.Owner] - entry.Bytes;
        if (bytes == 0) owners.Remove(entry.Owner); else owners[entry.Owner] = bytes;
    }
}
