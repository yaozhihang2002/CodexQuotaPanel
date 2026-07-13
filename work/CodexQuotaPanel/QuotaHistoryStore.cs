using System.Text.Json;

namespace CodexQuotaPanel;

internal sealed record QuotaHistoryPoint(long UtcMinute, int Slot, int WindowMinutes, int RemainingTenths)
{
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeSeconds(UtcMinute * 60);
    public double RemainingPercent => RemainingTenths / 10d;
}

internal sealed record HistoryFileModel(int v, List<int[]> p);

internal sealed class QuotaHistoryStore
{
    private const int RetentionMinutes = 24 * 60 + 15;
    private const int MaximumPoints = 1024;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<QuotaHistoryPoint> _points;
    private bool _enabled;

    public QuotaHistoryStore(string? path = null, bool enabled = true)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaPanel",
            "history-v1.json");
        _enabled = enabled;
        _points = Load(_path);
        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var originalCount = _points.Count;
        _points.RemoveAll(point => point.UtcMinute < nowMinute - RetentionMinutes || point.UtcMinute > nowMinute + 1);
        if (_enabled && _points.Count != originalCount) Persist();
    }

    internal string StoragePath => _path;

    public bool Enabled
    {
        get
        {
            lock (_gate) return _enabled;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate) return _points.Count;
        }
    }

    public DateTimeOffset? LastRecordedAt
    {
        get
        {
            lock (_gate)
            {
                if (_points.Count == 0) return null;
                return _points.MaxBy(point => point.UtcMinute)!.Timestamp;
            }
        }
    }

    /// <summary>
    /// Enables or disables future history samples. Existing samples are retained by default.
    /// </summary>
    /// <returns>False only when existing data was requested to be deleted but a file could not be removed.</returns>
    public bool SetEnabled(bool enabled, bool retainExisting = true)
    {
        lock (_gate)
        {
            _enabled = enabled;
            if (!enabled && !retainExisting) return ClearCore();
            return true;
        }
    }

    public bool Record(QuotaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (!_enabled) return false;

            var minute = snapshot.ObservedAt.ToUniversalTime().ToUnixTimeSeconds() / 60;
            var changed = false;
            changed |= RecordBucket(minute, 0, snapshot.Primary);
            changed |= RecordBucket(minute, 1, snapshot.Secondary);
            if (!changed) return false;

            Trim(minute);
            Persist();
            return true;
        }
    }

    public IReadOnlyList<QuotaHistoryPoint> GetRecent(DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            var currentMinute = (now ?? DateTimeOffset.Now).ToUniversalTime().ToUnixTimeSeconds() / 60;
            var cutoff = currentMinute - RetentionMinutes;
            return _points.Where(point => point.UtcMinute >= cutoff).OrderBy(point => point.UtcMinute).ToArray();
        }
    }

    /// <summary>
    /// Clears in-memory history and removes this store's history file. Failures are reported without throwing.
    /// </summary>
    public bool Clear()
    {
        lock (_gate) return ClearCore();
    }

    private bool RecordBucket(long minute, int slot, LimitBucket? bucket)
    {
        if (bucket?.WindowMinutes is not > 0) return false;
        var remaining = Math.Clamp((int)Math.Round(bucket.RemainingPercent * 10d), 0, 1000);
        var lastIndex = _points.FindLastIndex(point => point.Slot == slot && point.WindowMinutes == bucket.WindowMinutes.Value);
        if (lastIndex >= 0)
        {
            var last = _points[lastIndex];
            if (minute < last.UtcMinute) return false;
            if (minute == last.UtcMinute)
            {
                if (last.RemainingTenths == remaining) return false;
                _points[lastIndex] = last with { RemainingTenths = remaining };
                return true;
            }
            if (minute - last.UtcMinute < 5 && Math.Abs(remaining - last.RemainingTenths) < 5)
                return false;
        }

        _points.Add(new QuotaHistoryPoint(minute, slot, bucket.WindowMinutes.Value, remaining));
        return true;
    }

    private void Trim(long currentMinute)
    {
        var cutoff = currentMinute - RetentionMinutes;
        _points.RemoveAll(point => point.UtcMinute < cutoff);
        if (_points.Count <= MaximumPoints) return;
        _points.RemoveRange(0, _points.Count - MaximumPoints);
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var model = new HistoryFileModel(1, _points
                .OrderBy(point => point.UtcMinute)
                .Select(point => new[]
                {
                    checked((int)(point.UtcMinute - 28_000_000L)),
                    point.Slot,
                    point.WindowMinutes,
                    point.RemainingTenths
                })
                .ToList());
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(model));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or OverflowException)
        {
        }
    }

    private bool ClearCore()
    {
        _points.Clear();
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static List<QuotaHistoryPoint> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var model = JsonSerializer.Deserialize<HistoryFileModel>(File.ReadAllText(path));
            if (model?.v != 1 || model.p is null) return [];
            return model.p
                .Where(item => item.Length == 4 && item[1] is 0 or 1 && item[2] > 0 && item[3] is >= 0 and <= 1000)
                .Select(item => new QuotaHistoryPoint(item[0] + 28_000_000L, item[1], item[2], item[3]))
                .OrderBy(point => point.UtcMinute)
                .TakeLast(MaximumPoints)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or JsonException or OverflowException)
        {
            return [];
        }
    }
}
