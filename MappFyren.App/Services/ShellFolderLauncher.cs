using System.Diagnostics;
using System.IO;

namespace MappFyren.App.Services;

public sealed class ShellFolderLauncher : IFolderLauncher
{
  public bool TryOpen(string path, out string? error)
  {
    error = null;

    if (string.IsNullOrWhiteSpace(path))
    {
      error = "Sökvägen är tom.";
      return false;
    }

    if (!Directory.Exists(path))
    {
      error = $"Mappen finns inte: {path}";
      return false;
    }

    try
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = path,
        UseShellExecute = true
      });

      return true;
    }
    catch (Exception ex)
    {
      error = $"Kunde inte öppna mappen. {ex.Message}";
      return false;
    }
  }
}
