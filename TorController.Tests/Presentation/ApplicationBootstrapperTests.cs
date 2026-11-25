using System.Windows;
using System.Windows.Threading;
using TorController.App;
using TorController.App.Models;
using TorController.App.Presentation;
using TorController.App.Services;

namespace TorController.Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class ApplicationBootstrapperTests
{
    [TestMethod]
    public void CreateMainWindowAsyncBuildsWindowOnTemporaryDataAndMockServices()
    {
        RunOnSta(async () =>
        {
            using var directory = new TemporaryDirectory();
            var bootstrapper = CreateBootstrapper(directory.Path, []);

            var window = await bootstrapper.CreateMainWindowAsync();

            Assert.IsNotNull(window.DataContext);
            Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "tor-services.json")));
            var viewModel = (MainViewModel)window.DataContext;
            Assert.AreEqual(0, viewModel.Services.Count);
            window.Close();
        });
    }

    [TestMethod]
    public void CreateMainWindowAsyncSurfacesStorageErrors()
    {
        RunOnSta(async () =>
        {
            using var directory = new TemporaryDirectory();
            var invalidDataPath = Path.Combine(directory.Path, "file");
            await File.WriteAllTextAsync(invalidDataPath, "not a directory");
            var bootstrapper = CreateBootstrapper(invalidDataPath, []);

            await Assert.ThrowsExceptionAsync<IOException>(() => bootstrapper.CreateMainWindowAsync());
        });
    }

    private static ApplicationBootstrapper CreateBootstrapper(
        string dataDirectory,
        IReadOnlyList<WindowsServiceInfo> services) =>
        new(
            dataDirectory,
            new WindowsServiceCatalog(() => services),
            new WindowsServiceManager((_, _, _) => Task.CompletedTask),
            new TorConfigurationVerifier((_, _, _) => Task.FromResult(0)),
            (_, _) => Task.FromResult(false),
            new FakeServiceRegistrar());

    private static void RunOnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var frame = new DispatcherFrame();
                var task = action();
                _ = task.ContinueWith(
                    _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeServiceRegistrar : IWindowsServiceRegistrar
    {
        public Task RegisterAsync(
            string serviceName,
            string displayName,
            string torExecutablePath,
            string configurationPath,
            string startingType,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
