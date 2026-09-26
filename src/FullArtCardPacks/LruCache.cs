namespace FullArtCardPacks;

/// <summary>Main-thread cache. Eviction releases only this cache's reference;
/// it never disposes a value which a live native material may still use.</summary>
internal sealed class LruCache<TKey,TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey,LinkedListNode<(TKey Key,TValue Value)>> _items;
    private readonly LinkedList<(TKey Key,TValue Value)> _recency = new();
    internal LruCache(int capacity,IEqualityComparer<TKey>? comparer=null)
    {
        if(capacity<1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity=capacity;
        _items=new Dictionary<TKey,LinkedListNode<(TKey Key,TValue Value)>>(comparer);
    }
    public int Count => _items.Count;
    public IEnumerable<TValue> Values => _recency.Select(item=>item.Value);
    internal bool TryGetValue(TKey key,out TValue value)
    {
        if(!_items.TryGetValue(key,out var node)) { value=default!;return false; }
        _recency.Remove(node);_recency.AddFirst(node);value=node.Value.Value;return true;
    }
    internal void Set(TKey key,TValue value)
    {
        if(_items.TryGetValue(key,out var node))
        {
            node.Value=(key,value);_recency.Remove(node);_recency.AddFirst(node);return;
        }
        _items.Add(key,_recency.AddFirst((key,value)));
        if(_items.Count<=_capacity) return;
        var oldest=_recency.Last!;
        _items.Remove(oldest.Value.Key);_recency.RemoveLast();
    }
    internal void Clear() { _items.Clear();_recency.Clear(); }
}
