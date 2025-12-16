using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MappFyren.App.Services;
using MappFyren.Core.Configuration;
using MappFyren.Core.Monitoring;

namespace MappFyren.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
  private readonly IOptionsMonitor<AppSettings> _settings;
  private readonly IFolderMonitorService _monitor;
  private readonly IFolderLauncher _folderLauncher;

  private CancellationTokenSource? _cts;

  public MainViewModel(IOptionsMonitor<AppSettings> settings, IFolderMonitorService monitor, IFolderLauncher folderLauncher)
  {
    _settings = settings;
    _monitor = monitor;
    _folderLauncher = folderLauncher;

    _monitor.SnapshotUpdated += OnSnapshotUpdated;

    BuildGroupsFromSettings(_settings.CurrentValue);

    _settings.OnChange(s =>
    {
      BuildGroupsFromSettings(s);
      StartMonitoring(); // viktigt när settings reloadas
    });

    SelectedGroup = Groups.FirstOrDefault();
    StartMonitoring();
  }

  public ObservableCollection<GroupViewModel> Groups { get; } = new();

  [ObservableProperty] private GroupViewModel? selectedGroup;
  [ObservableProperty] private FolderRowViewModel? selectedFolder;

  [ObservableProperty] private string? statusMessage;

  [RelayCommand]
  private void OpenFolder(FolderRowViewModel? folder)
  {
    folder ??= SelectedFolder;
    if (folder is null) return;

    if (_folderLauncher.TryOpen(folder.Path, out var error))
    {
      StatusMessage = $"Öppnade: {folder.Path}";
    }
    else
    {
      StatusMessage = error;
    }
  }

  [RelayCommand]
  private void RefreshNow() => StartMonitoring();

  private void BuildGroupsFromSettings(AppSettings s)
  {
    Groups.Clear();

    if (s.Shared.Folders.Count > 0)
    {
      var sharedName = string.IsNullOrWhiteSpace(s.Shared.Name) ? "Gemensam" : s.Shared.Name;
      var shared = new GroupViewModel(sharedName);

      foreach (var f in s.Shared.Folders)
        shared.Folders.Add(new FolderRowViewModel(f.Id, f.Name, f.Path, f.Description, shared.Name, f.Thresholds.Min, f.Thresholds.Max));

      Groups.Add(shared);
    }
    SelectedGroup = Groups.FirstOrDefault();
    SelectedFolder = SelectedGroup?.Folders.FirstOrDefault();

    foreach (var g in s.Groups)
    {
      var group = new GroupViewModel(g.Name);

      foreach (var f in g.Folders)
        group.Folders.Add(new FolderRowViewModel(f.Id, f.Name, f.Path, f.Description, g.Name, f.Thresholds.Min, f.Thresholds.Max));

      Groups.Add(group);
    }

    SelectedGroup ??= Groups.FirstOrDefault();
  }

  private void StartMonitoring()
  {
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = new CancellationTokenSource();

    var s = _settings.CurrentValue;

    var folders = Groups.SelectMany(g => g.Folders)
        .Select(vm => new FolderSettings
        {
          Id = vm.Id,
          Name = vm.Name,
          Description = vm.Description,
          Path = vm.Path,
          Thresholds = new ThresholdSettings { Min = vm.Min, Max = vm.Max }
        })
        .ToList();

    _monitor.StartAsync(folders, s.Monitoring, _cts.Token);
    StatusMessage = $"Övervakning aktiv. Intervall: {Math.Max(1, s.Monitoring.IntervalSeconds)}s";
  }

  private void OnSnapshotUpdated(object? sender, FolderSnapshot snap)
  {
    App.Current.Dispatcher.Invoke(() =>
    {
      var folder = Groups.SelectMany(g => g.Folders).FirstOrDefault(f => f.Id == snap.FolderId);
      if (folder is null) return;

      folder.Count = snap.Count;
      folder.State = snap.State;
      folder.Message = snap.Message;
      folder.LastCheckedUtc = snap.CheckedAtUtc;
    });
  }

  partial void OnSelectedGroupChanged(GroupViewModel? value)
  {
    SelectedFolder = value?.Folders.FirstOrDefault();
  }

}
