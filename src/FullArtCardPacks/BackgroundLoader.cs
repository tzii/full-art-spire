using System.Collections.Concurrent;

namespace FullArtCardPacks;

/// <summary>Runs keyed work on a few background threads and hands results back
/// through <see cref="Drain"/> on the main thread. A key is queued once while
/// pending, and the newest request runs first so cards scrolled into view load
/// before cards already scrolled past. <see cref="Invalidate"/> drops queued
/// work and discards results of work that started before it.</summary>
internal sealed class BackgroundLoader<TKey, TResult> where TKey : notnull
{
    private readonly record struct Job(TKey Key, Func<TResult> Work, Action<TResult?, Exception?> Complete, int Generation);
    private readonly record struct Done(Job Job, TResult? Result, Exception? Error, bool DeliverySlot);

    private readonly object _gate = new();
    private readonly LinkedList<Job> _queued = new();
    private readonly Dictionary<TKey, int> _pending;
    private readonly ConcurrentQueue<Done> _done = new();
    private readonly SemaphoreSlim _signal = new(0);
    // Bound decoded results as well as active workers. Otherwise fast scrolling
    // can leave an entire deck's decoded images waiting for GPU uploads.
    private readonly SemaphoreSlim _deliverySlots;
    private readonly int _workers;
    private readonly bool _startThreads;
    private bool _started;
    private int _generation;

    /// <param name="startThreads">False leaves execution to <see cref="RunOne"/>, for deterministic tests.</param>
    internal BackgroundLoader(int workers, IEqualityComparer<TKey>? comparer = null, bool startThreads = true)
    {
        if (workers < 1) throw new ArgumentOutOfRangeException(nameof(workers));
        _workers = workers;
        _deliverySlots = new SemaphoreSlim(workers * 2, workers * 2);
        _startThreads = startThreads;
        _pending = new Dictionary<TKey, int>(comparer);
    }

    internal int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    internal bool IsPending(TKey key)
    {
        lock (_gate) return _pending.ContainsKey(key);
    }

    /// <returns>False when the key is already pending; its first request wins.</returns>
    internal bool Request(TKey key, Func<TResult> work, Action<TResult?, Exception?> complete)
    {
        lock (_gate)
        {
            if (_pending.ContainsKey(key)) return false;
            _pending[key] = _generation;
            _queued.AddFirst(new Job(key, work, complete, _generation));
            if (_startThreads && !_started)
            {
                _started = true;
                for (int i = 0; i < _workers; i++)
                    new Thread(WorkerLoop) { IsBackground = true, Name = "FullArtCardPacks loader " + i,
                        Priority = ThreadPriority.BelowNormal }.Start();
            }
        }
        _signal.Release();
        return true;
    }

    internal void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _queued.Clear();
            _pending.Clear();
        }
    }

    /// <summary>Runs the newest queued job on the calling thread.</summary>
    internal bool RunOne() => RunOne(deliverySlot: false);

    private bool RunOne(bool deliverySlot)
    {
        Job job;
        lock (_gate)
        {
            if (_queued.First is not { } node) return false;
            job = node.Value;
            _queued.RemoveFirst();
        }
        TResult? result = default;
        Exception? error = null;
        try { result = job.Work(); }
        catch (Exception ex) { error = ex; }
        _done.Enqueue(new Done(job, result, error, deliverySlot));
        return true;
    }

    /// <summary>Delivers finished jobs on the calling (main) thread while
    /// <paramref name="more"/> allows; it is checked before each delivery.
    /// Stale results are disposed when they own resources.</summary>
    internal int Drain(Func<bool>? more = null)
    {
        int delivered = 0;
        while ((more?.Invoke() ?? true) && _done.TryDequeue(out Done done))
        {
            bool current;
            lock (_gate)
            {
                current = done.Job.Generation == _generation
                    && _pending.TryGetValue(done.Job.Key, out int generation) && generation == done.Job.Generation;
                if (current) _pending.Remove(done.Job.Key);
            }
            try
            {
                if (!current)
                {
                    (done.Result as IDisposable)?.Dispose();
                    continue;
                }
                done.Job.Complete(done.Result, done.Error);
                delivered++;
            }
            finally
            {
                if (done.DeliverySlot) _deliverySlots.Release();
            }
        }
        return delivered;
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _signal.Wait();
            _deliverySlots.Wait();
            if (!RunOne(deliverySlot: true)) _deliverySlots.Release();
        }
    }
}
