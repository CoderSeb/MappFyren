namespace MappFyren.Core.Monitoring;

public sealed record FolderSnapshot(
    string FolderId,
    string Path,
    int? Count,
    AlarmState State,
    string? Message,
    DateTimeOffset CheckedAtUtc
);
