using System.Diagnostics;

namespace FullArtSpire.Tests;

internal static class BackgroundLoaderTests
{
    private sealed class Owned : IDisposable
    {
        internal readonly string Name;
        internal bool Disposed;
        internal Owned(string name) => Name = name;
        public void Dispose() => Disposed = true;
    }

    internal static void Run()
    {
        static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }

        var loader = new BackgroundLoader<string, Owned>(1, StringComparer.OrdinalIgnoreCase, startThreads: false);
        var delivered = new List<string>();
        int runs = 0;
        Action<Owned?, Exception?> record = (result, _) => delivered.Add(result!.Name);
        Require(loader.Request("a", () => { runs++; return new Owned("a"); }, record), "first request queues");
        Require(!loader.Request("A", () => { runs++; return new Owned("dup"); }, record), "pending key is not queued twice");
        Require(loader.IsPending("a") && loader.PendingCount == 1, "request is pending until delivered");
        loader.Request("b", () => { runs++; return new Owned("b"); }, record);
        while (loader.RunOne()) { }
        Require(runs == 2, "deduplicated work runs once per key");
        Require(loader.Drain() == 2 && delivered.SequenceEqual(new[] { "b", "a" }), "newest request runs first");
        Require(loader.PendingCount == 0 && !loader.IsPending("a"), "delivery clears pending");

        Owned? stale = null;
        loader.Request("c", () => stale = new Owned("stale"), record);
        loader.RunOne();
        loader.Invalidate();
        Require(!loader.IsPending("c"), "invalidation clears pending keys");
        loader.Request("c", () => new Owned("fresh"), record);
        Require(loader.Drain() == 0 && stale!.Disposed, "results from before invalidation are discarded and disposed");
        loader.RunOne();
        Require(loader.Drain() == 1 && delivered[^1] == "fresh", "a request after invalidation delivers its own result");

        loader.Request("d", () => new Owned("old"), record);
        loader.Invalidate();
        Require(!loader.RunOne(), "invalidation drops queued work");

        Exception? failure = null;
        loader.Request("e", () => throw new InvalidDataException("bad png"), (_, error) => failure = error);
        loader.RunOne();
        loader.Drain();
        Require(failure is InvalidDataException && !loader.IsPending("e"), "work errors are delivered, not thrown");

        for (int i = 0; i < 3; i++) loader.Request("budget" + i, () => new Owned("budget"), record);
        while (loader.RunOne()) { }
        int allowed = 1;
        Require(loader.Drain(() => allowed-- > 0) == 1 && loader.PendingCount == 2, "drain stops when the budget is spent");
        loader.Drain();

        bool rejected = false;
        try { _ = new BackgroundLoader<int, int>(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "a loader needs at least one worker");

        // Real threads: concurrency stays within the worker count.
        var threaded = new BackgroundLoader<int, int>(2);
        int active = 0, peak = 0, done = 0, worked = 0;
        for (int i = 0; i < 12; i++)
        {
            threaded.Request(i, () =>
            {
                int now = Interlocked.Increment(ref active);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                Thread.Sleep(15);
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref worked);
                return i;
            }, (_, _) => done++);
        }
        var clock = Stopwatch.StartNew();
        while (Volatile.Read(ref worked) < 4 && clock.Elapsed.TotalSeconds < 5) Thread.Sleep(5);
        Thread.Sleep(50);
        Require(Volatile.Read(ref worked) == 4 && done == 0,
            "decoded results are bounded at twice the worker count until the main thread drains");
        clock.Restart();
        while (done < 12 && clock.Elapsed.TotalSeconds < 10)
        {
            threaded.Drain();
            Thread.Sleep(5);
        }
        Require(done == 12 && threaded.PendingCount == 0, "threaded work completes and delivers");
        Require(peak is >= 1 and <= 2, $"at most two jobs run at once (peak {peak})");

        // Invalidate while a worker is inside its decoder, then reuse the key.
        var racing = new BackgroundLoader<string, Owned>(1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Owned? oldResult = null;
        int oldDelivered = 0, freshDelivered = 0;
        racing.Request("same", () =>
        {
            started.Set();
            release.Wait();
            return oldResult = new Owned("old-running");
        }, (_, _) => oldDelivered++);
        Require(started.Wait(TimeSpan.FromSeconds(5)), "worker began the old decode");
        racing.Invalidate();
        racing.Request("same", () => new Owned("new-generation"), (_, _) => freshDelivered++);
        release.Set();
        clock.Restart();
        while (freshDelivered == 0 && clock.Elapsed.TotalSeconds < 5)
        {
            racing.Drain();
            Thread.Sleep(5);
        }
        Require(oldDelivered == 0 && oldResult?.Disposed == true && freshDelivered == 1,
            "a running old-generation decode cannot replace a new request for the same key");
        Console.WriteLine("Background loader dedupe, newest-first order, invalidation, budget and concurrency tests passed");
    }
}
