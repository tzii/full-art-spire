namespace FullArtCardPacks.Tests;

internal static class LruCacheTests
{
    private sealed class Resource : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed=true;
    }
    internal static void Run()
    {
        static void Require(bool condition,string detail) { if(!condition)throw new InvalidOperationException(detail); }
        var cache=new LruCache<string,Resource?>(2,StringComparer.OrdinalIgnoreCase);
        var first=new Resource();var second=new Resource();var third=new Resource();
        cache.Set("first",first);cache.Set("second",second);
        Require(cache.TryGetValue("FIRST",out var loaded)&&ReferenceEquals(first,loaded),"LRU preserves path comparer and identity");
        Require(cache.ContainsKey("second"),"membership check finds an entry");
        cache.Set("third",third);
        Require(cache.Count==2&&!cache.TryGetValue("second",out _),"LRU evicts least recently read entry; membership does not promote");
        Require(!second.Disposed,"Eviction must not dispose a potentially live resource");
        cache.Set("missing",null);
        Require(cache.TryGetValue("missing",out var missing)&&missing is null,"Negative entries are cached and bounded");
        cache.Set("missing",third);
        Require(cache.Count==2&&cache.TryGetValue("missing",out var recovered)&&ReferenceEquals(third,recovered),"Replacement updates existing key");
        cache.Clear();
        Require(cache.Count==0&&!cache.TryGetValue("missing",out _)&&!first.Disposed&&!third.Disposed,"Invalidation drops ownership without disposal");
        bool rejected=false;try { _=new LruCache<int,int>(0); }catch(ArgumentOutOfRangeException) {rejected=true;}
        Require(rejected,"LRU requires a positive limit");
        var turnover=new LruCache<int,int>(48);
        for(int i=0;i<577;i++)turnover.Set(i,i);
        Require(turnover.Count==48&&!turnover.TryGetValue(528,out _)&&turnover.TryGetValue(529,out _),"577-card turnover remains bounded");
        Console.WriteLine("Bounded LRU ownership, recency, negative-cache and 577-entry turnover tests passed");
    }
}
