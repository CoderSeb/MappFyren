using CommunityToolkit.Mvvm.ComponentModel;
using MappFyren.Core.Monitoring;

namespace MappFyren.App.ViewModels;

public sealed partial class FolderRowViewModel : ObservableObject
{
    public FolderRowViewModel(string id, string name, string path, string? description, string groupName, int? min, int? max)
    {
        Id = id;
        Name = name;
        Path = path;
        Description = description;
        GroupName = groupName;
        Min = min;
        Max = max;
    }

    public string Id { get; }
    public string Name { get; }
    public string Path { get; }
    public string? Description { get; }
    public string GroupName { get; }
    public int? Min { get; }
    public int? Max { get; }

    [ObservableProperty] private int? count;
    [ObservableProperty] private AlarmState state;
    [ObservableProperty] private string? message;
    [ObservableProperty] private DateTimeOffset? lastCheckedUtc;
}
