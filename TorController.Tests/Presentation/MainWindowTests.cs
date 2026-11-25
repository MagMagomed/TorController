using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TorController.App;
using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Presentation;
using TorController.App.Services;
using TorController.App.Storage;

namespace TorController.Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class MainWindowTests
{
    [TestMethod]
    public void WindowBindsServiceGridEditorAndActionsToViewModel()
    {
        RunOnSta(() =>
        {
            var fixture = new Fixture();
            var viewModel = fixture.CreateViewModel();
            var window = new MainWindow(viewModel);

            Assert.AreSame(viewModel, window.DataContext);
            Assert.IsInstanceOfType(window.FindName("ServicesGrid"), typeof(DataGrid));
            Assert.IsInstanceOfType(window.FindName("ErrorBanner"), typeof(TextBlock));
            Assert.IsInstanceOfType(window.FindName("SaveConfigurationButton"), typeof(Button));
            var deleteServiceButton = (Button)window.FindName("DeleteServiceButton")!;
            Assert.IsNotNull(deleteServiceButton);
            Assert.IsTrue(BindingOperations.IsDataBound(deleteServiceButton, Button.CommandProperty));
            var createServiceButton = (Button)window.FindName("CreateServiceButton")!;
            Assert.IsNotNull(createServiceButton);
            Assert.IsTrue(BindingOperations.IsDataBound(createServiceButton, Button.CommandProperty));
            Assert.IsTrue(((DataGrid)window.FindName("ServicesGrid")!).Columns.Count >= 5);
        });
    }

    [TestMethod]
    public void LoadedEventStartsViewModelOnLoad()
    {
        RunOnSta(() =>
        {
            var fixture = new Fixture();
            var viewModel = fixture.CreateViewModel();
            var window = new MainWindow(viewModel);

            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.AreEqual(1, fixture.Seeker.CallCount);
            Assert.IsNotNull(window.LoadTask);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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

    private sealed class Fixture
    {
        public MemoryStore<TorService> Services { get; } = new();
        public MemoryStore<ServiceStage> Stages { get; } = new();
        public MemoryStore<ServiceStartingType> StartingTypes { get; } = new();
        public MemoryStore<TorConfiguration> Configurations { get; } = new();
        public FakeSeeker Seeker { get; } = new();
        public FakeFileSystem FileSystem { get; } = new();
        public FakeTorServiceCreator ServiceCreator { get; } = new();

        public Fixture()
        {
            var stage = new ServiceStage { Id = Guid.NewGuid(), Name = "Running" };
            var startingType = new ServiceStartingType { Id = Guid.NewGuid(), Name = "Automatic" };
            var configuration = new TorConfiguration { Id = Guid.NewGuid(), SocksPorts = [9050] };
            Services.Items.Add(new TorService
            {
                Name = "tor",
                DisplayName = "Tor Win32 Service",
                Stage = stage.Id,
                StartingType = startingType.Id,
                Configuration = configuration.Id,
                TorExecutablePath = @"C:\Tor\tor.exe",
                ConfigurationFilePath = @"C:\Tor\torrc"
            });
            Stages.Items.Add(stage);
            StartingTypes.Items.Add(startingType);
            Configurations.Items.Add(configuration);
            FileSystem.Files[@"C:\Tor\torrc"] = "SocksPort 9050";
        }

        public MainViewModel CreateViewModel() =>
            new(
                Services,
                Stages,
                StartingTypes,
                Configurations,
                Seeker,
                new WindowsServiceManager((_, _, _) => Task.CompletedTask),
                new TorConfigurationService(
                    FileSystem,
                    new TorConfigurationVerifier((_, _, _) => Task.FromResult(0)),
                    new TorConfigDocumentEditor()),
                (_, _) => Task.FromResult(false),
                ServiceCreator,
                _ => null,
                (_, _) => Task.FromResult(false),
                FileSystem);
    }

    private sealed class MemoryStore<T> : IJsonCollectionStore<T>
    {
        public List<T> Items { get; } = [];
        public Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.ToArray());
        public Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
        {
            Items.Clear();
            Items.AddRange(items);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSeeker : ITorServiceSeeker
    {
        public int CallCount { get; private set; }
        public Task<SeekResult> Execute(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new SeekResult(false, string.Empty));
        }
    }

    private sealed class FakeTorServiceCreator : ITorServiceCreator
    {
        public Task<TorServiceCreationResult> CreateAsync(
            TorServiceCreationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TorServiceCreationResult(true, true, "Registered.", null, null));
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => false;
        public void CreateDirectory(string path) { }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Files[path]);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Files[path] = content;
            return Task.CompletedTask;
        }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => Files[destinationPath] = Files[sourcePath];
        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            Files[destinationPath] = Files[sourcePath];
            Files.Remove(sourcePath);
        }
        public void DeleteFile(string path) => Files.Remove(path);
        public void DeleteDirectory(string path) { }
        public bool HasReparsePoints(string path) => false;
    }
}
