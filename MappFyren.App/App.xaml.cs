using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MappFyren.App.Services;
using MappFyren.App.ViewModels;
using MappFyren.App.Views;
using MappFyren.Core.Configuration;
using MappFyren.Core.Monitoring;

namespace MappFyren.App;

public partial class App : Application
{
  private IHost? _host;
  private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "MappFyren.log");

  protected override void OnStartup(StartupEventArgs e)
  {
    // Fånga fel som annars kan kännas “tysta”
    DispatcherUnhandledException += OnDispatcherUnhandledException;
    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
      try
      {
        File.AppendAllText(LogPath, $"[Unhandled] {DateTime.Now:O}\n{args.ExceptionObject}\n\n");
      }
      catch { /* ignore */ }
    };

    base.OnStartup(e);

    try
    {
      _host = Host.CreateDefaultBuilder()
          .ConfigureAppConfiguration(cfg =>
          {
            cfg.SetBasePath(AppContext.BaseDirectory);
            cfg.AddJsonFile("settings.json", optional: false, reloadOnChange: true);
          })
          .ConfigureServices((ctx, services) =>
          {
            services.AddOptions<AppSettings>()
                      .Bind(ctx.Configuration.GetSection("MappFyren"));

            services.AddSingleton<IFolderMonitorService, FolderMonitorService>();
            services.AddSingleton<IFolderLauncher, ShellFolderLauncher>();

            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
          })
          .Build();

      _host.Start();

      var window = _host.Services.GetRequiredService<MainWindow>();
      MainWindow = window;
      ShutdownMode = ShutdownMode.OnMainWindowClose;

      window.Show(); // <- om denna saknas stängs appen direkt
    }
    catch (Exception ex)
    {
      try
      {
        File.AppendAllText(LogPath, $"[Startup] {DateTime.Now:O}\n{ex}\n\n");
      }
      catch { /* ignore */ }

      MessageBox.Show(
          $"Startup-fel:\n\n{ex.Message}\n\nLogg: {LogPath}",
          "MappFyren",
          MessageBoxButton.OK,
          MessageBoxImage.Error);

      Shutdown(-1);
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    try
    {
      _host?.StopAsync().GetAwaiter().GetResult();
      _host?.Dispose();
    }
    catch { /* ignore */ }

    base.OnExit(e);
  }

  private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
  {
    try
    {
      File.AppendAllText(LogPath, $"[Dispatcher] {DateTime.Now:O}\n{e.Exception}\n\n");
    }
    catch { /* ignore */ }

    MessageBox.Show(
        $"Fel:\n\n{e.Exception.Message}\n\nLogg: {LogPath}",
        "MappFyren",
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    e.Handled = true;
  }
}
