using System.Collections.Concurrent;

namespace PicSelect.Core;

/// <summary>
/// 后台预取 + LRU 缓存。核心目标是让翻页永远命中缓存，而不是现场解码。
/// </summary>
public sealed class ImageCache : IDisposable
{
    public const int LookAhead = 10;
    public const int LookBehind = 3;

    /// <summary>高清图长边上限。老机器核显共享内存，96MB 的单张纹理压力太大。</summary>
    private const int MaxLongEdge = 4096;

    private readonly long _budget;

    private readonly string[] _files;
    private readonly int _previewW;
    private readonly int _previewH;

    private readonly ConcurrentDictionary<int, DecodedImage> _cache = new();
    private readonly ConcurrentDictionary<int, byte> _inflight = new();
    private readonly ConcurrentDictionary<int, byte> _failed = new();
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly Thread[] _workers;

    private volatile bool _stop;
    private int _cursor;
    private long _bytes;

    public ImageCache(IReadOnlyList<string> files, int previewWidth, int previewHeight, int? workerCount = null)
    {
        _files = files.ToArray();
        _previewW = previewWidth;
        _previewH = previewHeight;
        _budget = ComputeBudget();

        // 双核低压 U（如 i5-6200U：2 物理核 4 逻辑核）上开 3 个解码线程会跟 UI 抢 CPU
        int n = workerCount ?? (Environment.ProcessorCount <= 4 ? 2 : 3);

        _workers = new Thread[n];
        for (int i = 0; i < n; i++)
        {
            _workers[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"decode-{i}",
                Priority = ThreadPriority.BelowNormal,
            };
            _workers[i].Start();
        }
    }

    public int CachedCount => _cache.Count;
    public long CachedBytes => Interlocked.Read(ref _bytes);
    public bool IsFailed(int idx) => _failed.ContainsKey(idx);

    /// <summary>光标移动后重排预取优先级：前进优先，回退次之。</summary>
    public void SetCursor(int idx)
    {
        Volatile.Write(ref _cursor, idx);
        while (_queue.TryDequeue(out _)) { }

        for (int i = 1; i <= LookAhead; i++)
        {
            int t = idx + i;
            if (t < _files.Length && !_cache.ContainsKey(t) && !_failed.ContainsKey(t))
                _queue.Enqueue(t);
        }
        for (int i = 1; i <= LookBehind; i++)
        {
            int t = idx - i;
            if (t >= 0 && !_cache.ContainsKey(t) && !_failed.ContainsKey(t))
                _queue.Enqueue(t);
        }
    }

    public DecodedImage? TryGet(int idx) => _cache.TryGetValue(idx, out var v) ? v : null;

    /// <summary>取预览图；未缓存时立即解码（优先级高于预取队列）。</summary>
    public Task<DecodedImage?> GetAsync(int idx) =>
        _cache.TryGetValue(idx, out var hit)
            ? Task.FromResult<DecodedImage?>(hit)
            : Task.Run(() => LoadNow(idx));

    /// <summary>按需解全分辨率，供放大时替换。不进 LRU 缓存，由调用方持有。</summary>
    public Task<DecodedImage?> GetFullAsync(int idx) => Task.Run(() =>
    {
        try
        {
            if (idx < 0 || idx >= _files.Length) return null;
            // 长边封顶 4096：核显共享内存，96MB 的单张纹理代价太大，
            // 而 4096 在 1080p / 2K 屏上肉眼已无差别
            return ImageDecoder.Decode(_files[idx], MaxLongEdge, MaxLongEdge);
        }
        catch { return null; }
    });

    /// <summary>按物理内存量决定缓存上限，避免在 4G/8G 机器上把内存吃满。</summary>
    private static long ComputeBudget()
    {
        long totalMb;
        try { totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024; }
        catch { return 280L * 1024 * 1024; }

        long mb = totalMb switch
        {
            < 5000 => 220,
            < 9000 => 320,
            _ => 420,
        };
        return mb * 1024 * 1024;
    }

    private DecodedImage? LoadNow(int idx)
    {
        if (idx < 0 || idx >= _files.Length) return null;
        if (_cache.TryGetValue(idx, out var hit)) return hit;
        if (_failed.ContainsKey(idx)) return null;

        if (!_inflight.TryAdd(idx, 0))
        {
            // 预取线程正在解这张，等它一下，避免重复解码
            for (int i = 0; i < 600; i++)
            {
                if (_cache.TryGetValue(idx, out var v)) return v;
                if (!_inflight.ContainsKey(idx)) break;
                Thread.Sleep(5);
            }
            return _cache.TryGetValue(idx, out var v2) ? v2 : null;
        }

        try
        {
            var d = ImageDecoder.Decode(_files[idx], _previewW, _previewH);
            Put(idx, d);
            return d;
        }
        catch
        {
            _failed[idx] = 0;
            return null;
        }
        finally
        {
            _inflight.TryRemove(idx, out _);
        }
    }

    private void Put(int idx, DecodedImage d)
    {
        if (_cache.TryGetValue(idx, out var old))
            Interlocked.Add(ref _bytes, -old.Bytes);
        _cache[idx] = d;
        Interlocked.Add(ref _bytes, d.Bytes);
        Trim();
    }

    private void Trim()
    {
        if (Interlocked.Read(ref _bytes) <= _budget) return;

        int cursor = Volatile.Read(ref _cursor);
        var victims = _cache.Keys
            .Where(k => k != cursor)
            .OrderByDescending(k => Math.Abs(k - cursor))
            .ToList();

        foreach (var v in victims)
        {
            if (Interlocked.Read(ref _bytes) <= _budget) break;
            if (_cache.TryRemove(v, out var d))
                Interlocked.Add(ref _bytes, -d.Bytes);
        }
    }

    private void WorkerLoop()
    {
        while (!_stop)
        {
            if (!_queue.TryDequeue(out int idx))
            {
                Thread.Sleep(8);
                continue;
            }
            if (_cache.ContainsKey(idx) || _inflight.ContainsKey(idx) || _failed.ContainsKey(idx))
                continue;

            int cur = Volatile.Read(ref _cursor);
            if (Math.Abs(idx - cur) > LookAhead + 4) continue;  // 光标已经跑远，丢弃过期任务

            if (!_inflight.TryAdd(idx, 0)) continue;
            try
            {
                var d = ImageDecoder.Decode(_files[idx], _previewW, _previewH);
                Put(idx, d);
            }
            catch
            {
                _failed[idx] = 0;
            }
            finally
            {
                _inflight.TryRemove(idx, out _);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var t in _workers)
            t.Join(500);
        _cache.Clear();
    }
}
