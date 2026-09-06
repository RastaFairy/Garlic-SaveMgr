using System.IO;
using System.Windows;

namespace GarlicSaveMgr;

public partial class App : Application
{
    private System.Windows.Threading.DispatcherTimer? _telemetryHeartbeat;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Infrastructure.ApplicationTelemetryService.RecordException("DispatcherUnhandledException", args.Exception, true);
            WriteFatal("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Garlic SaveMgr - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Infrastructure.ApplicationTelemetryService.RecordException("AppDomain.UnhandledException", args.ExceptionObject as Exception, true);
            WriteFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Infrastructure.ApplicationTelemetryService.RecordException("TaskScheduler.UnobservedTaskException", args.Exception, true);
            args.SetObserved();
        };

        try
        {
            Infrastructure.AppPaths.EnsureDirectories();
            Infrastructure.LogService.Initialize();
            Infrastructure.LogService.Write("Aplicación iniciada.", "INFO");
            Infrastructure.ApplicationTelemetryService.Heartbeat("HOST");
            _telemetryHeartbeat = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
            _telemetryHeartbeat.Tick += (_, _) => Infrastructure.ApplicationTelemetryService.Heartbeat("HOST");
            _telemetryHeartbeat.Start();
            Infrastructure.ThemeManager.Initialize();

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            WriteFatal("Startup", ex);
            MessageBox.Show(
                $"No se pudo iniciar Garlic SaveMgr.\n\n{ex}\n\nSe ha creado un archivo de diagnóstico en la carpeta de datos de la aplicación.",
                "Garlic SaveMgr - Error de inicio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _telemetryHeartbeat?.Stop();
        base.OnExit(e);
    }

    private static void WriteFatal(string source, Exception? ex)
    {
        try
        {
            var dir = Infrastructure.AppPaths.LogsDirectory;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "startup_error.log");
            File.AppendAllText(file,
                $"[{DateTime.Now:O}] {source}\n{ex}\n\n");
        }
        catch
        {
            // Nunca provocar un segundo fallo intentando registrar el primero.
        }
    }
}
