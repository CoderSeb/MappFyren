namespace MappFyren.Core.Configuration;

public sealed class AppSettings
{
  public MonitoringSettings Monitoring { get; init; } = new();
  public FolderGroupSettings Shared { get; init; } = new() { Name = "Gemensam" };
  public List<FolderGroupSettings> Groups { get; init; } = new();
}

public sealed class MonitoringSettings
{
  public int IntervalSeconds { get; init; } = 10;
  public bool Recursive { get; init; } = false;

  // Nätverksvänliga skydd
  public int MaxParallelism { get; init; } = 4;            // hur många mappar som räknas samtidigt
  public int PerFolderTimeoutSeconds { get; init; } = 5;   // timeout per mapp-räkning
  public int ErrorBackoffSeconds { get; init; } = 30;      // bas-backoff vid fel/timeout
}


public sealed class FolderGroupSettings
{
  public string Name { get; init; } = "";
  public List<FolderSettings> Folders { get; init; } = new();
}

public sealed class FolderSettings
{
  public string Id { get; init; } = "";
  public string Name { get; init; } = "";
  public string? Description { get; init; }
  public string Path { get; init; } = "";
  public ThresholdSettings Thresholds { get; init; } = new();
}

public sealed class ThresholdSettings
{
  public int? Min { get; init; }
  public int? Max { get; init; }
}
