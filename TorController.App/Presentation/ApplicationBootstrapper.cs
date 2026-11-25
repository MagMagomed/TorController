using TorController.App.Models;
using TorController.App.Services;
using TorController.App.Storage;
using TorController.App.Infrastructure;
using System.IO;
using System.Windows;

namespace TorController.App.Presentation;

public sealed class ApplicationBootstrapper
{
    private readonly string _dataDirectory;
    private readonly IWindowsServiceCatalog _serviceCatalog;
    private readonly WindowsServiceManager _serviceManager;
    private readonly TorConfigurationVerifier _verifier;
    private readonly Func<string, string, Task<bool>> _confirmRestart;
    private readonly IWindowsServiceRegistrar _serviceRegistrar;

    public ApplicationBootstrapper(
        string dataDirectory,
        IWindowsServiceCatalog serviceCatalog,
        WindowsServiceManager serviceManager,
        TorConfigurationVerifier verifier,
        Func<string, string, Task<bool>> confirmRestart,
        IWindowsServiceRegistrar? serviceRegistrar = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = dataDirectory;
        _serviceCatalog = serviceCatalog ?? throw new ArgumentNullException(nameof(serviceCatalog));
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _confirmRestart = confirmRestart ?? throw new ArgumentNullException(nameof(confirmRestart));
        _serviceRegistrar = serviceRegistrar ?? new WindowsServiceRegistrar();
    }

    public async Task<MainWindow> CreateMainWindowAsync(CancellationToken cancellationToken = default)
    {
        await new AppDataInitializer(_dataDirectory).InitializeAsync(cancellationToken);

        var servicesStore = new JsonCollectionStore<TorService>(Path.Combine(_dataDirectory, "tor-services.json"));
        var stagesStore = new JsonCollectionStore<ServiceStage>(Path.Combine(_dataDirectory, "stages.json"));
        var startingTypesStore = new JsonCollectionStore<ServiceStartingType>(Path.Combine(_dataDirectory, "starting-types.json"));
        var configurationsStore = new JsonCollectionStore<TorConfiguration>(Path.Combine(_dataDirectory, "configurations.json"));
        var configurationReader = new TorConfigurationReader(new TorConfigParser());
        var seeker = new TorServiceSeeker(
            _serviceCatalog,
            configurationReader,
            servicesStore,
            stagesStore,
            startingTypesStore,
            configurationsStore);
        var fileSystem = new PhysicalFileSystem();
        var configurationService = new TorConfigurationService(
            fileSystem,
            _verifier,
            new TorConfigDocumentEditor());
        var serviceCreator = new TorServiceCreator(
            fileSystem,
            _serviceCatalog,
            servicesStore,
            configurationsStore,
            _verifier,
            new TorConfigDocumentEditor(),
            _serviceRegistrar,
            seeker);
        var viewModel = new MainViewModel(
            servicesStore,
            stagesStore,
            startingTypesStore,
            configurationsStore,
            seeker,
            _serviceManager,
            configurationService,
            _confirmRestart,
            serviceCreator,
            ShowCreateServiceDialog,
            ConfirmDeleteService,
            fileSystem);

        return new MainWindow(viewModel);
    }

    private static TorServiceCreationRequest? ShowCreateServiceDialog(
        IReadOnlyList<string> suggestedTorExecutablePaths)
    {
        var viewModel = new CreateTorServiceViewModel(
            suggestedTorExecutablePaths,
            new PhysicalFileSystem());
        var dialog = new CreateTorServiceWindow(viewModel);
        if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? viewModel.Request : null;
    }

    private static Task<bool> ConfirmDeleteService(string title, string message) =>
        Task.FromResult(
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
            MessageBoxResult.Yes);
}
