using System.IO;
using System.Windows;

namespace GarlicSaveMgr;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteFatal("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Garlic SaveMgr - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        try
        {
            Infrastructure.AppPaths.EnsureDirectories();
            Infrastructure.LogService.Initialize();
            Infrastructure.LogService.Write("Aplicación iniciada.", "INFO");

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
