using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Presentation;
using TorController.App.Services;
using TorController.App.Storage;

namespace TorController.Tests.Presentation;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task OnLoadDisplaysSavedRowsBeforeDiscoveryCompletesAndRefreshesAfterward()
    {
        var fixture = new Fixture();
        var seekerCompletion = new TaskCompletionSource<SeekResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Seeker.ResultTask = seekerCompletion.Task;
        var viewModel = fixture.CreateViewModel();

        var loadTask = viewModel.OnLoad();

        Assert.IsTrue(viewModel.IsLoading);
        Assert.AreEqual("Old display name", viewModel.Services.Single().DisplayName);
        fixture.Services.Items[0].DisplayName = "Updated by discovery";
        seekerCompletion.SetResult(new SeekResult(false, string.Empty));
        await loadTask;

        Assert.AreEqual("Updated by discovery", viewModel.Services.Single().DisplayName);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public async Task OnLoadDisplaysPartialErrorAndKeepsSuccessfullyDiscoveredRows()
    {
        var fixture = new Fixture();
        fixture.Seeker.ResultTask = Task.FromResult(new SeekResult(true, "Tor2 config unavailable"));
        var viewModel = fixture.CreateViewModel();

        await viewModel.OnLoad();

        Assert.AreEqual("Tor2 config unavailable", viewModel.ErrorMessage);
        Assert.AreEqual(1, viewModel.Services.Count);
    }

    [TestMethod]
    public async Task StartSelectedAsyncRunsServiceActionAndRefreshesDiscovery()
    {
        var fixture = new Fixture();
        var actions = new List<WindowsServiceAction>();
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.StartSelectedAsync();

        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Start }, actions);
        Assert.AreEqual(2, fixture.Seeker.CallCount);
    }

    [TestMethod]
    public async Task StopSelectedAsyncRunsStopAction()
    {
        var fixture = new Fixture();
        var actions = new List<WindowsServiceAction>();
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.StopSelectedAsync();

        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Stop }, actions);
    }

    [TestMethod]
    public async Task RestartSelectedAsyncRunsRestartAction()
    {
        var fixture = new Fixture();
        var actions = new List<WindowsServiceAction>();
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.RestartSelectedAsync();

        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Restart }, actions);
    }

    [TestMethod]
    public async Task RefreshAsyncRunsDiscoveryAndUpdatesErrorBanner()
    {
        var fixture = new Fixture();
        fixture.Seeker.ResultTask = Task.FromResult(new SeekResult(true, "Discovery warning"));
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshAsync();

        Assert.AreEqual(1, fixture.Seeker.CallCount);
        Assert.AreEqual("Discovery warning", viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public async Task CreateServiceAsyncRegistersServiceAndRefreshesRows()
    {
        var fixture = new Fixture
        {
            CreationRequest = new TorServiceCreationRequest(
                "TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\NewData", "Manual",
                new TorConfiguration())
        };
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        var stage = fixture.Stages.Items.Single();
        var startingType = fixture.StartingTypes.Items.Single();
        var configuration = new TorConfiguration { Id = Guid.NewGuid(), SocksPorts = [9060] };
        fixture.Configurations.Items.Add(configuration);
        fixture.Services.Items.Add(new TorService
        {
            Id = Guid.NewGuid(),
            Name = "TorNew",
            DisplayName = "New Tor Service",
            Stage = stage.Id,
            StartingType = startingType.Id,
            Configuration = configuration.Id
        });

        await viewModel.CreateServiceAsync();

        Assert.AreEqual(1, fixture.ServiceCreator.CallCount);
        Assert.AreEqual("TorNew", fixture.ServiceCreator.Request!.ServiceName);
        CollectionAssert.Contains(fixture.SuggestedExecutablePaths!.ToArray(), @"C:\Tor\tor.exe");
        Assert.IsTrue(viewModel.Services.Any(row => row.Name == "TorNew"));
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public async Task CreateServiceAsyncDoesNotRegisterWhenDialogIsCancelled()
    {
        var fixture = new Fixture { CreationRequest = null };
        var viewModel = fixture.CreateViewModel();

        await viewModel.CreateServiceAsync();

        Assert.AreEqual(0, fixture.ServiceCreator.CallCount);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncCancelsWithoutChangingServicesWhenConfirmationIsDeclined()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(false) };
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(0, fixture.ServiceActions.Count);
        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.AreEqual(1, fixture.Configurations.Items.Count);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncStopsAndDeletesServiceThenRemovesUnusedConfiguration()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        var torrcPath = @"C:\Tor\torrc";

        await viewModel.DeleteSelectedServiceAsync();

        CollectionAssert.AreEqual(
            new[] { WindowsServiceAction.Stop, WindowsServiceAction.Delete },
            fixture.ServiceActions);
        StringAssert.Contains(fixture.ConfirmedDeleteMessage!, "tor");
        StringAssert.Contains(fixture.ConfirmedDeleteMessage!, "Файлы службы");
        StringAssert.Contains(fixture.ConfirmedDeleteMessage!, "не будут удалены");
        Assert.AreEqual(0, fixture.Services.Items.Count);
        Assert.AreEqual(0, fixture.Configurations.Items.Count);
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(torrcPath));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(@"C:\Tor\tor.exe"));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(@"C:\Tor\Logs\notices.log"));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(@"C:\Tor\Data\cached-certs"));
        Assert.AreEqual(0, viewModel.Services.Count);
        Assert.IsNull(viewModel.SelectedService);
        Assert.IsTrue(viewModel.StatusMessage.Contains("tor", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncDeletesOnlyApplicationOwnedFiles()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        var service = fixture.Services.Items[0];
        service.ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc";
        service.ApplicationCreatedDataDirectory = @"C:\Tor\Data";
        fixture.Configurations.Items[0].DataDirectory = @"C:\Tor\Data";
        fixture.Configurations.Items[0].ClientTransportPlugins = [@"obfs4 exec C:\Tor\lyrebird.exe"];
        fixture.Configurations.Items[0].Logs = [@"notice file C:\Tor\Logs\notices.log"];
        fixture.FileSystem.Files[service.ApplicationCreatedTorrcPath] = "managed torrc";
        fixture.FileSystem.Files[@"C:\Tor\Data\cached-certs"] = "managed state";
        fixture.FileSystem.Directories.Add(@"C:\Tor\Data");
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.IsTrue(fixture.ConfirmedDeleteMessage!.Contains(@"C:\Tor\Data\torrc", StringComparison.Ordinal));
        Assert.IsTrue(fixture.ConfirmedDeleteMessage.Contains(@"C:\Tor\Data", StringComparison.Ordinal));
        Assert.IsFalse(fixture.FileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath));
        Assert.IsFalse(fixture.FileSystem.Directories.Contains(@"C:\Tor\Data"));
        Assert.IsFalse(fixture.FileSystem.Files.ContainsKey(@"C:\Tor\Data\cached-certs"));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(service.TorExecutablePath));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(@"C:\Tor\Logs\notices.log"));
        Assert.AreEqual(0, fixture.Services.Items.Count);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncPersistsWindowsDeletionAndRetriesFileCleanupAfterRestart()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        var service = fixture.Services.Items[0];
        service.ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc";
        service.ApplicationCreatedDataDirectory = @"C:\Tor\Data";
        fixture.Configurations.Items[0].DataDirectory = @"C:\Tor\Data";
        fixture.FileSystem.Files[service.ApplicationCreatedTorrcPath] = "managed torrc";
        fixture.FileSystem.Files[@"C:\Tor\Data\cached-certs"] = "managed state";
        fixture.FileSystem.Directories.Add(@"C:\Tor\Data");
        fixture.FileSystem.DirectoryDeleteFailure = new IOException("directory is locked");
        var firstViewModel = fixture.CreateViewModel();
        await firstViewModel.OnLoad();

        await firstViewModel.DeleteSelectedServiceAsync();

        Assert.IsTrue(service.WindowsServiceRemoved);
        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.IsTrue(firstViewModel.ErrorMessage.Contains("directory is locked", StringComparison.Ordinal));
        Assert.AreEqual(2, fixture.ServiceActions.Count);

        fixture.FileSystem.DirectoryDeleteFailure = null;
        var restartedViewModel = fixture.CreateViewModel();
        await restartedViewModel.OnLoad();
        await restartedViewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(2, fixture.ServiceActions.Count);
        Assert.AreEqual(0, fixture.Services.Items.Count);
        Assert.AreEqual(0, fixture.Configurations.Items.Count);
        Assert.IsFalse(fixture.FileSystem.Directories.Contains(@"C:\Tor\Data"));
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(service.TorExecutablePath));
        Assert.AreEqual(string.Empty, restartedViewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncKeepsRecordAndFilesWhenOwnedDirectoryIsShared()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        var service = fixture.Services.Items[0];
        service.ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc";
        service.ApplicationCreatedDataDirectory = @"C:\Tor\Data";
        fixture.Configurations.Items[0].DataDirectory = @"C:\Tor\Data";
        var otherConfiguration = new TorConfiguration
        {
            Id = Guid.NewGuid(),
            DataDirectory = @"C:\Tor\Data"
        };
        fixture.Configurations.Items.Add(otherConfiguration);
        fixture.Services.Items.Add(new TorService
        {
            Id = Guid.NewGuid(),
            Name = "tor-other",
            Stage = service.Stage,
            StartingType = service.StartingType,
            Configuration = otherConfiguration.Id,
            ConfigurationFilePath = @"C:\Tor\Data\other-torrc"
        });
        fixture.FileSystem.Files[service.ApplicationCreatedTorrcPath] = "managed torrc";
        fixture.FileSystem.Directories.Add(@"C:\Tor\Data");
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(2, fixture.Services.Items.Count);
        Assert.IsTrue(service.WindowsServiceRemoved);
        Assert.IsTrue(fixture.FileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath));
        Assert.IsTrue(fixture.FileSystem.Directories.Contains(@"C:\Tor\Data"));
        Assert.IsTrue(viewModel.ErrorMessage.Contains("referenced", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DeleteServiceAsyncChecksStopStateEvenWhenSavedStageIsStopped()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        fixture.Stages.Items[0].Name = "Stopped";
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        CollectionAssert.AreEqual(
            new[] { WindowsServiceAction.Stop, WindowsServiceAction.Delete },
            fixture.ServiceActions);
        Assert.AreEqual(0, fixture.Services.Items.Count);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncDoesNotDeleteWhenStoppingFails()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            fixture.ServiceActions.Add(action);
            return action == WindowsServiceAction.Stop
                ? Task.FromException(new InvalidOperationException("stop failed"))
                : Task.CompletedTask;
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Stop }, fixture.ServiceActions);
        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.AreEqual(1, fixture.Configurations.Items.Count);
        StringAssert.Contains(viewModel.ErrorMessage, "stop failed");
    }

    [TestMethod]
    public async Task DeleteServiceAsyncKeepsJsonWhenWindowsDeletionFails()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            fixture.ServiceActions.Add(action);
            return action == WindowsServiceAction.Delete
                ? Task.FromException(new InvalidOperationException("delete failed"))
                : Task.CompletedTask;
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.AreEqual(1, fixture.Configurations.Items.Count);
        StringAssert.Contains(viewModel.ErrorMessage, "delete failed");
    }

    [TestMethod]
    public async Task DeleteServiceAsyncKeepsConfigurationReferencedByAnotherService()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        var service = fixture.Services.Items[0];
        fixture.Services.Items.Add(new TorService
        {
            Id = Guid.NewGuid(),
            Name = "TorOther",
            DisplayName = "Other Tor",
            Stage = service.Stage,
            StartingType = service.StartingType,
            Configuration = service.Configuration
        });
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        viewModel.SelectedService = viewModel.Services.Single(row => row.Name == "tor");

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.AreEqual("TorOther", fixture.Services.Items.Single().Name);
        Assert.AreEqual(1, fixture.Configurations.Items.Count);
        Assert.AreEqual("TorOther", viewModel.Services.Single().Name);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncReportsPartialResultWhenJsonSaveFails()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        fixture.Services.SaveFailure = new IOException("JSON is read-only");
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(1, fixture.Services.Items.Count);
        StringAssert.Contains(viewModel.ErrorMessage, "already deleted in Windows");
        StringAssert.Contains(viewModel.ErrorMessage, "tor");

        fixture.Services.SaveFailure = null;
        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(0, fixture.Services.Items.Count);
        Assert.AreEqual(0, fixture.Configurations.Items.Count);
        Assert.AreEqual(2, fixture.ServiceActions.Count);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceAsyncCanRetryUnusedConfigurationCleanupAfterPartialJsonSave()
    {
        var fixture = new Fixture { ConfirmDelete = (_, _) => Task.FromResult(true) };
        fixture.Configurations.SaveFailure = new IOException("JSON is read-only");
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(1, fixture.Services.Items.Count);
        Assert.AreEqual(1, fixture.Configurations.Items.Count);
        StringAssert.Contains(viewModel.ErrorMessage, "already deleted in Windows");
        Assert.IsTrue(viewModel.DeleteServiceCommand.CanExecute(null));

        fixture.Configurations.SaveFailure = null;
        var restartedViewModel = fixture.CreateViewModel();
        await restartedViewModel.OnLoad();
        await restartedViewModel.DeleteSelectedServiceAsync();

        Assert.AreEqual(0, fixture.Services.Items.Count);
        Assert.AreEqual(0, fixture.Configurations.Items.Count);
        Assert.AreEqual(2, fixture.ServiceActions.Count);
        Assert.AreEqual(string.Empty, restartedViewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteServiceCommandIsDisabledWithoutASelectedService()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        viewModel.SelectedService = null;

        Assert.IsFalse(viewModel.DeleteServiceCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CreateServiceAsyncReportsRegistrationFailure()
    {
        var fixture = new Fixture();
        fixture.ServiceCreator.Result = new TorServiceCreationResult(
            false, false, "UAC approval was cancelled.", null, null);
        var viewModel = fixture.CreateViewModel();

        await viewModel.CreateServiceAsync();

        Assert.AreEqual("UAC approval was cancelled.", viewModel.ErrorMessage);
        Assert.AreEqual(string.Empty, viewModel.StatusMessage);
        Assert.AreEqual(1, fixture.Services.Items.Count);
    }

    [TestMethod]
    public async Task CreateServiceAsyncRefreshesRowsAndShowsWarningAfterPartialRegistration()
    {
        var fixture = new Fixture();
        fixture.ServiceCreator.Result = new TorServiceCreationResult(
            false, true, "Registered but JSON update failed.", @"C:\Tor\NewData\torrc", @"C:\Tor\NewData");
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        fixture.Services.Items[0].DisplayName = "Updated after registration";

        await viewModel.CreateServiceAsync();

        Assert.AreEqual("Registered but JSON update failed.", viewModel.ErrorMessage);
        Assert.AreEqual("Updated after registration", viewModel.Services.Single().DisplayName);
    }

    [TestMethod]
    public async Task SaveConfigurationAsyncValidatesPortsAndStoresVerifiedConfiguration()
    {
        var fixture = new Fixture();
        fixture.ConfirmRestart = (_, _) => Task.FromResult(false);
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        viewModel.SocksPortsText = "9052,9053";
        viewModel.UseBridges = true;
        viewModel.BridgesText = "obfs4 new-bridge";

        await viewModel.SaveConfigurationAsync();

        Assert.IsTrue(fixture.FileSystem.Files[@"C:\Tor\torrc"].Contains("SocksPort 9052", StringComparison.Ordinal));
        Assert.AreEqual(2, fixture.Configurations.Items.Single().SocksPorts.Count);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task SaveConfigurationAsyncPreservesLoopbackPortBindings()
    {
        var fixture = new Fixture();
        fixture.Configurations.Items[0].SocksPortEndpoints = ["127.0.0.1:9050"];
        fixture.Configurations.Items[0].ControlPorts = [9054];
        fixture.Configurations.Items[0].ControlPortEndpoints = ["127.0.0.1:9054"];
        fixture.FileSystem.Files[@"C:\Tor\torrc"] =
            "SocksPort 127.0.0.1:9050\nControlPort 127.0.0.1:9054";
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        viewModel.SocksPortsText = "127.0.0.1:9052";
        viewModel.ControlPortsText = "127.0.0.1:9054";

        await viewModel.SaveConfigurationAsync();

        Assert.IsTrue(fixture.FileSystem.Files[@"C:\Tor\torrc"].Contains("SocksPort 127.0.0.1:9052", StringComparison.Ordinal));
        Assert.IsTrue(fixture.FileSystem.Files[@"C:\Tor\torrc"].Contains("ControlPort 127.0.0.1:9054", StringComparison.Ordinal));
        Assert.AreEqual(9052, fixture.Configurations.Items.Single().SocksPorts.Single());
        Assert.AreEqual("127.0.0.1:9052", fixture.Configurations.Items.Single().SocksPortEndpoints.Single());
    }

    [TestMethod]
    public async Task SaveConfigurationAsyncDoesNotRestartWithoutExplicitConfirmation()
    {
        var fixture = new Fixture();
        var actionCount = 0;
        fixture.ServiceManager = new WindowsServiceManager((_, _, _) =>
        {
            actionCount++;
            return Task.CompletedTask;
        });
        fixture.ConfirmRestart = (_, _) => Task.FromResult(false);
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.SaveConfigurationAsync();

        Assert.AreEqual(0, actionCount);
        Assert.IsTrue(viewModel.StatusMessage.Contains("saved", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SaveConfigurationAsyncRestartsOnlyAfterUserConfirms()
    {
        var fixture = new Fixture();
        var actions = new List<WindowsServiceAction>();
        fixture.ServiceManager = new WindowsServiceManager((_, action, _) =>
        {
            actions.Add(action);
            return Task.CompletedTask;
        });
        fixture.ConfirmRestart = (_, _) => Task.FromResult(true);
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();

        await viewModel.SaveConfigurationAsync();

        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Restart }, actions);
    }

    [TestMethod]
    public async Task SaveConfigurationAsyncRejectsInvalidPortInput()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.OnLoad();
        viewModel.SocksPortsText = "abc";

        await viewModel.SaveConfigurationAsync();

        Assert.IsTrue(viewModel.ErrorMessage.Contains("port", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("SocksPort 9050", fixture.FileSystem.Files[@"C:\Tor\torrc"]);
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
        public WindowsServiceManager ServiceManager { get; set; } = new((_, _, _) => Task.CompletedTask);
        public Func<string, string, Task<bool>> ConfirmRestart { get; set; } = (_, _) => Task.FromResult(false);
        public Func<string, string, Task<bool>> ConfirmDelete { get; set; } = (_, _) => Task.FromResult(false);
        public List<WindowsServiceAction> ServiceActions { get; } = [];
        public string? ConfirmedDeleteMessage { get; private set; }
        public TorServiceCreationRequest? CreationRequest { get; set; } =
            new("TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\NewData", "Manual", new TorConfiguration());
        public IReadOnlyList<string>? SuggestedExecutablePaths { get; private set; }

        public Fixture()
        {
            var stage = new ServiceStage { Id = Guid.NewGuid(), Name = "Running" };
            var startingType = new ServiceStartingType { Id = Guid.NewGuid(), Name = "Automatic" };
            var configuration = new TorConfiguration { Id = Guid.NewGuid(), SocksPorts = [9050] };
            var service = new TorService
            {
                Id = Guid.NewGuid(),
                Name = "tor",
                DisplayName = "Old display name",
                Stage = stage.Id,
                StartingType = startingType.Id,
                Configuration = configuration.Id,
                TorExecutablePath = @"C:\Tor\tor.exe",
                ConfigurationFilePath = @"C:\Tor\torrc",
                ServiceAccount = "LocalService"
            };
            Services.Items.Add(service);
            Stages.Items.Add(stage);
            StartingTypes.Items.Add(startingType);
            Configurations.Items.Add(configuration);
            FileSystem.Files[@"C:\Tor\torrc"] = "SocksPort 9050";
            FileSystem.Files[@"C:\Tor\tor.exe"] = "fake executable";
            FileSystem.Files[@"C:\Tor\Logs\notices.log"] = "fake log";
            FileSystem.Files[@"C:\Tor\Data\cached-certs"] = "fake data";
            FileSystem.Directories.Add(@"C:\Tor\Data");
            ServiceManager = new WindowsServiceManager((_, action, _) =>
            {
                ServiceActions.Add(action);
                return Task.CompletedTask;
            });
        }

        public MainViewModel CreateViewModel() =>
            new(
                Services,
                Stages,
                StartingTypes,
                Configurations,
                Seeker,
                ServiceManager,
                new TorConfigurationService(FileSystem, new TorConfigurationVerifier((_, _, _) => Task.FromResult(0)),
                    new TorConfigDocumentEditor()),
                ConfirmRestart,
                ServiceCreator,
                GetCreationRequest,
                ConfirmDeleteService,
                FileSystem);

        private Task<bool> ConfirmDeleteService(string title, string message)
        {
            ConfirmedDeleteMessage = message;
            return ConfirmDelete(title, message);
        }

        private TorServiceCreationRequest? GetCreationRequest(IReadOnlyList<string> suggestedExecutablePaths)
        {
            SuggestedExecutablePaths = suggestedExecutablePaths;
            return CreationRequest;
        }
    }

    private sealed class MemoryStore<T> : IJsonCollectionStore<T>
    {
        public List<T> Items { get; } = [];
        public Exception? SaveFailure { get; set; }

        public Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.ToArray());

        public Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
        {
            if (SaveFailure is not null)
            {
                return Task.FromException(SaveFailure);
            }

            Items.Clear();
            Items.AddRange(items);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSeeker : ITorServiceSeeker
    {
        public Task<SeekResult> ResultTask { get; set; } = Task.FromResult(new SeekResult(false, string.Empty));
        public int CallCount { get; private set; }

        public Task<SeekResult> Execute(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ResultTask;
        }
    }

    private sealed class FakeTorServiceCreator : ITorServiceCreator
    {
        public int CallCount { get; private set; }
        public TorServiceCreationRequest? Request { get; private set; }
        public TorServiceCreationResult Result { get; set; } =
            new(true, true, "Service registered.", @"C:\Tor\NewData\torrc", @"C:\Tor\NewData");

        public Task<TorServiceCreationResult> CreateAsync(
            TorServiceCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IOException? DirectoryDeleteFailure { get; set; }

        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public void CreateDirectory(string path) => Directories.Add(path);
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
        public void DeleteDirectory(string path)
        {
            if (DirectoryDeleteFailure is not null)
            {
                throw DirectoryDeleteFailure;
            }

            var prefix = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Files.Keys.Where(file =>
                         Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Files.Remove(file);
            }

            foreach (var directory in Directories.Where(directory =>
                         Path.GetFullPath(directory).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFullPath(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Directories.Remove(directory);
            }
        }
        public bool HasReparsePoints(string path) => false;
    }
}
