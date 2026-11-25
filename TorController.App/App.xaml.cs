using System.IO;
using System.Text.Json;
using System.Windows;
using TorController.App.Presentation;
using TorController.App.Services;

namespace TorController.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = StartApplicationAsync(
            CreateMainWindowAsync,
            window => MainWindow = window,
            window => window.Show(),
            ShowStartupError,
            exitCode => Shutdown(exitCode));
    }

    internal static async Task StartApplicationAsync(
        Func<Task<Window>> createMainWindow,
        Action<Window> setMainWindow,
        Action<Window> showWindow,
        Action<string> showStartupError,
        Action<int> shutdown)
    {
        ArgumentNullException.ThrowIfNull(createMainWindow);
        ArgumentNullException.ThrowIfNull(setMainWindow);
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(showStartupError);
        ArgumentNullException.ThrowIfNull(shutdown);

        try
        {
            var mainWindow = await createMainWindow();
            setMainWindow(mainWindow);
            showWindow(mainWindow);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            showStartupError(
                $"Tor Controller could not start because its local data could not be initialized.{Environment.NewLine}{exception.Message}");
            shutdown(-1);
        }
    }

    private static async Task<Window> CreateMainWindowAsync()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TorController");
        var bootstrapper = new ApplicationBootstrapper(
            dataDirectory,
            new WindowsServiceCatalog(),
            new WindowsServiceManager(),
            new TorConfigurationVerifier(),
            ConfirmRestartAsync);
        return await bootstrapper.CreateMainWindowAsync();
    }

    private static void ShowStartupError(string message) =>
        MessageBox.Show(message, "Tor Controller", MessageBoxButton.OK, MessageBoxImage.Error);

    private static Task<bool> ConfirmRestartAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
