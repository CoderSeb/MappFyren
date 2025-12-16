using System.Collections.Concurrent;
using MappFyren.Core.Configuration;

namespace MappFyren.Core.Monitoring;

public sealed class FolderMonitorService : IFolderMonitorService
{
  private readonly ConcurrentDictionary<string, FolderSettings> _folders = new();
  private readonly ConcurrentDictionary<string, byte> _inFlight = new();
  private readonly ConcurrentDictionary<string, int> _failures = new();
  private readonly ConcurrentDictionary<string, DateTimeOffset> _nextAllowedUtc = new();

  private CancellationTokenSource? _cts;
  private Task? _loop;

  public event EventHandler<FolderSnapshot>? SnapshotUpdated;

  public Task StartAsync(IReadOnlyList<FolderSettings> folders, MonitoringSettings monitoring, CancellationToken ct)
  {
    Stop();

    foreach (var f in folders)
      _folders[f.Id] = f;

    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _loop = RunAsync(monitoring, _cts.Token);
    return Task.CompletedTask;
  }

  public void Stop()
  {
    try { _cts?.Cancel(); } catch { /* ignore */ }
    _cts?.Dispose();
    _cts = null;
    _loop = null;

    _folders.Clear();
    _inFlight.Clear();
    _failures.Clear();
    _nextAllowedUtc.Clear();
  }

  private async Task RunAsync(MonitoringSettings monitoring, CancellationToken ct)
  {
    var interval = TimeSpan.FromSeconds(Math.Max(1, monitoring.IntervalSeconds));
    using var timer = new PeriodicTimer(interval);

    using var throttler = new SemaphoreSlim(Math.Max(1, monitoring.MaxParallelism));

    // Kör direkt en gång
    ScheduleTick(monitoring, throttler, ct);

    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
    {
      ScheduleTick(monitoring, throttler, ct);
    }
  }

  private void ScheduleTick(MonitoringSettings monitoring, SemaphoreSlim throttler, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    foreach (var folder in _folders.Values)
    {
      if (ct.IsCancellationRequested) break;

      // Backoff: hoppa över tills nextAllowedUtc
      if (_nextAllowedUtc.TryGetValue(folder.Id, out var next) && now < next)
        continue;

      // In-flight: starta inte ny räkning om en redan pågår (t.ex. hängande nätverk)
      if (!_inFlight.TryAdd(folder.Id, 0))
        continue;

      _ = CheckOneAsync(folder, monitoring, throttler, ct);
    }
  }

  private async Task CheckOneAsync(FolderSettings folder, MonitoringSettings monitoring, SemaphoreSlim throttler, CancellationToken ct)
  {
    try
    {
      await throttler.WaitAsync(ct).ConfigureAwait(false);

      var timeout = TimeSpan.FromSeconds(Math.Max(1, monitoring.PerFolderTimeoutSeconds));
      var snapshot = await TryCountWithTimeoutAsync(folder, monitoring, timeout, ct).ConfigureAwait(false);

      ApplyBackoff(folder.Id, snapshot, monitoring);
      SnapshotUpdated?.Invoke(this, snapshot);
    }
    catch (OperationCanceledException)
    {
      // ignore
    }
    catch (Exception ex)
    {
      var snap = new FolderSnapshot(folder.Id, folder.Path, null, AlarmState.Error, ex.Message, DateTimeOffset.UtcNow);
      ApplyBackoff(folder.Id, snap, monitoring);
      SnapshotUpdated?.Invoke(this, snap);
    }
    finally
    {
      throttler.Release();
      _inFlight.TryRemove(folder.Id, out _);
    }
  }

  private static async Task<FolderSnapshot> TryCountWithTimeoutAsync(
      FolderSettings folder,
      MonitoringSettings monitoring,
      TimeSpan timeout,
      CancellationToken ct)
  {
    try
    {
      var searchOpt = monitoring.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

      // Kör på threadpool och lägg timeout "utanpå"
      var countTask = Task.Run(() =>
      {
        // Count undermappar (ej filer)
        return Directory.EnumerateDirectories(folder.Path, "*", searchOpt).Count();
      }, ct);

      var count = await countTask.WaitAsync(timeout, ct).ConfigureAwait(false);

      var state = Evaluate(folder.Thresholds, count);
      return new FolderSnapshot(folder.Id, folder.Path, count, state, null, DateTimeOffset.UtcNow);
    }
    catch (TimeoutException)
    {
      return new FolderSnapshot(folder.Id, folder.Path, null, AlarmState.Error, $"Timeout efter {timeout.TotalSeconds:0}s", DateTimeOffset.UtcNow);
    }
    catch (Exception ex)
    {
      return new FolderSnapshot(folder.Id, folder.Path, null, AlarmState.Error, ex.Message, DateTimeOffset.UtcNow);
    }
  }

  private void ApplyBackoff(string folderId, FolderSnapshot snap, MonitoringSettings monitoring)
  {
    if (snap.State == AlarmState.Ok || snap.State == AlarmState.TooLow || snap.State == AlarmState.TooHigh)
    {
      _failures[folderId] = 0;
      _nextAllowedUtc[folderId] = DateTimeOffset.UtcNow; // redo nästa tick
      return;
    }

    // Error/Timeout => exponentiell backoff
    var failures = _failures.AddOrUpdate(folderId, 1, (_, prev) => Math.Min(prev + 1, 8));
    var baseSeconds = Math.Max(1, monitoring.ErrorBackoffSeconds);
    var delaySeconds = baseSeconds * (int)Math.Pow(2, failures - 1); // 30,60,120,240...
    _nextAllowedUtc[folderId] = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
  }

  private static AlarmState Evaluate(ThresholdSettings t, int count)
  {
    if (t.Min is not null && count < t.Min.Value) return AlarmState.TooLow;
    if (t.Max is not null && count > t.Max.Value) return AlarmState.TooHigh;
    return AlarmState.Ok;
  }
}
