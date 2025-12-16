using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MappFyren.App.ViewModels;

public sealed partial class GroupViewModel : ObservableObject
{
    public GroupViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public ObservableCollection<FolderRowViewModel> Folders { get; } = new();
}
