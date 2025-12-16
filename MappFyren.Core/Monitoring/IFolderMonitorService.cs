using MappFyren.Core.Configuration;

namespace MappFyren.Core.Monitoring;

public interface IFolderMonitorService
{
  event EventHandler<FolderSnapshot>? SnapshotUpdated;

  Task StartAsync(IReadOnlyList<FolderSettings> folders, MonitoringSettings monitoring, CancellationToken ct);
  void Stop();
}
