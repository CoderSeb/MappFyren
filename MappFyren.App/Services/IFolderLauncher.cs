namespace MappFyren.App.Services;

public interface IFolderLauncher
{
  bool TryOpen(string path, out string? error);
}
