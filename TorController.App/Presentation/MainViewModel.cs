using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Services;
using TorController.App.Storage;

namespace TorController.App.Presentation;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IJsonCollectionStore<TorService> _servicesStore;
    private readonly IJsonCollectionStore<ServiceStage> _stagesStore;
    private readonly IJsonCollectionStore<ServiceStartingType> _startingTypesStore;
    private readonly IJsonCollectionStore<TorConfiguration> _configurationsStore;
    private readonly TorServiceFileCleaner _serviceFileCleaner;
    private readonly ITorServiceSeeker _serviceSeeker;
    private readonly WindowsServiceManager _serviceManager;
    private readonly TorConfigurationService _configurationService;
    private readonly Func<string, string, Task<bool>> _confirmRestart;
    private readonly Func<string, string, Task<bool>> _confirmDelete;
    private readonly ITorServiceCreator _serviceCreator;
    private readonly Func<IReadOnlyList<string>, TorServiceCreationRequest?> _requestServiceCreation;
    private ServiceGridRow? _selectedService;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _socksPortsText = string.Empty;
    private bool _useBridges;
    private string _clientTransportPluginsText = string.Empty;
    private string _bridgesText = string.Empty;
    private string _logsText = string.Empty;
    private string _controlPortsText = string.Empty;
    private string _dataDirectoryText = string.Empty;
    private Task? _onLoadTask;
    private TorService? _pendingDeletedService;

    public MainViewModel(
        IJsonCollectionStore<TorService> servicesStore,
        IJsonCollectionStore<ServiceStage> stagesStore,
        IJsonCollectionStore<ServiceStartingType> startingTypesStore,
        IJsonCollectionStore<TorConfiguration> configurationsStore,
        ITorServiceSeeker serviceSeeker,
        WindowsServiceManager serviceManager,
        TorConfigurationService configurationService,
        Func<string, string, Task<bool>> confirmRestart,
        ITorServiceCreator serviceCreator,
        Func<IReadOnlyList<string>, TorServiceCreationRequest?> requestServiceCreation,
        Func<string, string, Task<bool>> confirmDelete,
        IFileSystem fileSystem)
    {
        _servicesStore = servicesStore ?? throw new ArgumentNullException(nameof(servicesStore));
        _stagesStore = stagesStore ?? throw new ArgumentNullException(nameof(stagesStore));
        _startingTypesStore = startingTypesStore ?? throw new ArgumentNullException(nameof(startingTypesStore));
        _configurationsStore = configurationsStore ?? throw new ArgumentNullException(nameof(configurationsStore));
        _serviceFileCleaner = new TorServiceFileCleaner(
            fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)));
        _serviceSeeker = serviceSeeker ?? throw new ArgumentNullException(nameof(serviceSeeker));
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _confirmRestart = confirmRestart ?? throw new ArgumentNullException(nameof(confirmRestart));
        _serviceCreator = serviceCreator ?? throw new ArgumentNullException(nameof(serviceCreator));
        _requestServiceCreation = requestServiceCreation ?? throw new ArgumentNullException(nameof(requestServiceCreation));
        _confirmDelete = confirmDelete ?? throw new ArgumentNullException(nameof(confirmDelete));

        StartCommand = new AsyncCommand(StartSelectedAsync, () => SelectedService is not null && !IsLoading);
        StopCommand = new AsyncCommand(StopSelectedAsync, () => SelectedService is not null && !IsLoading);
        RestartCommand = new AsyncCommand(RestartSelectedAsync, () => SelectedService is not null && !IsLoading);
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync, () => SelectedService is not null && !IsLoading);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsLoading);
        CreateServiceCommand = new AsyncCommand(CreateServiceAsync, () => !IsLoading);
        DeleteServiceCommand = new AsyncCommand(
            DeleteSelectedServiceAsync,
            () => !IsLoading && (SelectedService is not null || _pendingDeletedService is not null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ServiceGridRow> Services { get; } = [];
    public ServiceGridRow? SelectedService
    {
        get => _selectedService;
        set
        {
            if (ReferenceEquals(_selectedService, value))
            {
                return;
            }

            _selectedService = value;
            LoadConfigurationEditor(value?.Configuration);
            OnPropertyChanged();
            RaiseCommandsCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RaiseCommandsCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string SocksPortsText { get => _socksPortsText; set => SetField(ref _socksPortsText, value); }
    public bool UseBridges { get => _useBridges; set => SetField(ref _useBridges, value); }
    public string ClientTransportPluginsText { get => _clientTransportPluginsText; set => SetField(ref _clientTransportPluginsText, value); }
    public string BridgesText { get => _bridgesText; set => SetField(ref _bridgesText, value); }
    public string LogsText { get => _logsText; set => SetField(ref _logsText, value); }
    public string ControlPortsText { get => _controlPortsText; set => SetField(ref _controlPortsText, value); }
    public string DataDirectoryText { get => _dataDirectoryText; set => SetField(ref _dataDirectoryText, value); }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RestartCommand { get; }
    public AsyncCommand SaveConfigurationCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CreateServiceCommand { get; }
    public AsyncCommand DeleteServiceCommand { get; }

    public Task OnLoad() => _onLoadTask ??= LoadAsync();

    public Task StartSelectedAsync() =>
        ExecuteServiceActionAsync(_serviceManager.StartAsync);

    public Task StopSelectedAsync() =>
        ExecuteServiceActionAsync(_serviceManager.StopAsync);

    public Task RestartSelectedAsync() =>
        ExecuteServiceActionAsync(_serviceManager.RestartAsync);

    public async Task CreateServiceAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var knownServices = await _servicesStore.LoadAsync();
            var suggestedExecutablePaths = knownServices
                .Select(service => service.TorExecutablePath)
                .ToArray();
            var request = _requestServiceCreation(suggestedExecutablePaths);
            if (request is null)
            {
                return;
            }

            var result = await _serviceCreator.CreateAsync(request);
            ErrorMessage = result.Success ? string.Empty : result.Message;
            StatusMessage = result.Success ? result.Message : string.Empty;
            if (!result.ServiceRegistered)
            {
                return;
            }

            try
            {
                await RefreshRowsAsync();
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                ErrorMessage = string.Join(
                    Environment.NewLine,
                    new[] { ErrorMessage, $"The service was registered, but the table could not be refreshed: {exception.Message}" }
                        .Where(message => !string.IsNullOrWhiteSpace(message)));
                StatusMessage = string.Empty;
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = exception.Message;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DeleteSelectedServiceAsync()
    {
        if (IsLoading)
        {
            return;
        }

        var service = _pendingDeletedService ?? SelectedService?.Service;
        if (service is null)
        {
            ErrorMessage = "Select a Tor service before deleting it.";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        var windowsDeletionConfirmed = service.WindowsServiceRemoved;
        string? fileCleanupMessage = null;
        try
        {
            if (_pendingDeletedService is null)
            {
                var fileWarning = HasApplicationCreatedFiles(service)
                    ? $"Созданный приложением torrc: {service.ApplicationCreatedTorrcPath}. " +
                      $"Каталог данных будет удалён целиком: {service.ApplicationCreatedDataDirectory}. " +
                      "Исполняемый файл Tor, плагины и журналы за пределами каталога данных останутся."
                    : "Файлы службы не будут удалены, поскольку приложение не создавало их или не может подтвердить владение.";
                var confirmed = await _confirmDelete(
                    "Удалить службу Tor?",
                    $"Служба '{service.DisplayName}' ({service.Name}) будет удалена из Windows и списка приложения. " +
                    fileWarning + " Продолжить?");
                if (!confirmed)
                {
                    return;
                }
            }

            if (!service.WindowsServiceRemoved)
            {
                var stopResult = await _serviceManager.StopAsync(service.Name);
                if (!stopResult.Success)
                {
                    ErrorMessage = $"Служба '{service.Name}' не удалена, так как её не удалось остановить: {stopResult.Message}";
                    return;
                }

                var deleteResult = await _serviceManager.DeleteAsync(service.Name);
                if (!deleteResult.Success)
                {
                    ErrorMessage =
                        $"Service '{service.Name}' could not be removed from Windows. Its registration and application data were kept; " +
                        "if it was running, it has been stopped. " +
                        deleteResult.Message;
                    return;
                }

                service.WindowsServiceRemoved = true;
            }

            _pendingDeletedService = service;
            windowsDeletionConfirmed = true;
            DeleteServiceCommand.RaiseCanExecuteChanged();
            await PersistWindowsServiceRemovedAsync(service);

            var services = await _servicesStore.LoadAsync();
            var configurations = await _configurationsStore.LoadAsync();
            var cleanupResult = await Task.Run(() =>
                _serviceFileCleaner.Cleanup(service, services, configurations));
            if (!cleanupResult.Success)
            {
                throw new IOException(cleanupResult.Message);
            }

            fileCleanupMessage = cleanupResult.Message;
            await PersistDeletedServiceAsync(service, fileCleanupMessage);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = windowsDeletionConfirmed
                ? $"Windows service '{service.Name}' was already deleted in Windows, but application data could not be fully synchronized. " +
                  $"Use the delete action again to retry synchronization. {exception.Message}" +
                  (fileCleanupMessage is null ? string.Empty : $" File cleanup completed: {fileCleanupMessage}")
                : $"Could not delete Windows service '{service.Name}': {exception.Message}";
            StatusMessage = string.Empty;
            if (windowsDeletionConfirmed)
            {
                try
                {
                    await RefreshRowsAsync();
                }
                catch (Exception refreshException) when (IsRecoverable(refreshException))
                {
                    ErrorMessage += $"{Environment.NewLine}Could not refresh the service table: {refreshException.Message}";
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveConfigurationAsync()
    {
        if (IsLoading)
        {
            return;
        }

        if (SelectedService is null)
        {
            ErrorMessage = "Select a Tor service before editing its configuration.";
            return;
        }

        IsLoading = true;
        try
        {
            var storedConfigurations = (await _configurationsStore.LoadAsync()).ToList();
            var current = storedConfigurations.FirstOrDefault(item =>
                item.Id == SelectedService.Service.Configuration);
            if (current is null)
            {
                ErrorMessage = $"Configuration record for service '{SelectedService.Name}' was not found.";
                return;
            }

            var socksPorts = ParsePortEntries(SocksPortsText, "SocksPort");
            var controlPorts = ParsePortEntries(ControlPortsText, "ControlPort");
            var updated = new TorConfiguration
            {
                Id = current.Id,
                SocksPorts = socksPorts.Ports,
                SocksPortEndpoints = socksPorts.Endpoints,
                UseBridges = UseBridges,
                ClientTransportPlugins = ParseLines(ClientTransportPluginsText),
                Bridges = ParseLines(BridgesText),
                Logs = ParseLines(LogsText),
                ControlPorts = controlPorts.Ports,
                ControlPortEndpoints = controlPorts.Endpoints,
                DataDirectory = string.IsNullOrWhiteSpace(DataDirectoryText) ? null : DataDirectoryText.Trim(),
                AdditionalDirectives = current.AdditionalDirectives.ToDictionary(
                    item => item.Key,
                    item => new List<string>(item.Value),
                    StringComparer.OrdinalIgnoreCase)
            };

            var saveResult = await _configurationService.SaveAsync(SelectedService.Service, updated);
            if (!saveResult.Success)
            {
                ErrorMessage = saveResult.Message;
                StatusMessage = string.Empty;
                return;
            }

            var index = storedConfigurations.FindIndex(item => item.Id == updated.Id);
            if (index < 0)
            {
                ErrorMessage = "The torrc file was saved, but the JSON configuration record is missing.";
                return;
            }

            storedConfigurations[index] = updated;
            try
            {
                await _configurationsStore.SaveAsync(storedConfigurations);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                ErrorMessage = $"The torrc file was saved, but its JSON record could not be updated: {exception.Message}";
                return;
            }

            StatusMessage = saveResult.BackupPath is null
                ? saveResult.Message
                : $"{saveResult.Message} Backup: {saveResult.BackupPath}";
            ErrorMessage = string.Empty;

            var restart = await _confirmRestart(
                "Configuration saved",
                $"The configuration for '{SelectedService.Name}' was verified. Restart the service now?");
            if (restart)
            {
                var restartResult = await _serviceManager.RestartAsync(SelectedService.Name);
                if (!restartResult.Success)
                {
                    ErrorMessage = $"Configuration was saved, but the service could not be restarted: {restartResult.Message}";
                }
                else
                {
                    StatusMessage = $"{StatusMessage} {restartResult.Message}";
                }
            }

            await ReloadAfterChangeAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = $"Could not update configuration: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _serviceSeeker.Execute();
            if (result.HasError)
            {
                ErrorMessage = result.Message;
            }

            await RefreshRowsAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = exception.Message;
            try
            {
                await RefreshRowsAsync();
            }
            catch (Exception refreshException) when (IsRecoverable(refreshException))
            {
                ErrorMessage = $"{ErrorMessage}{Environment.NewLine}{refreshException.Message}".Trim();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        var messages = new List<string>();
        try
        {
            await RefreshRowsAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            messages.Add($"Could not load saved Tor services: {exception.Message}");
        }

        try
        {
            var result = await _serviceSeeker.Execute();
            if (result.HasError && !string.IsNullOrWhiteSpace(result.Message))
            {
                messages.Add(result.Message);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            messages.Add($"Could not discover Tor services: {exception.Message}");
        }

        try
        {
            await RefreshRowsAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            messages.Add($"Could not refresh the service table: {exception.Message}");
        }
        finally
        {
            ErrorMessage = string.Join(Environment.NewLine, messages);
            IsLoading = false;
        }
    }

    private async Task ExecuteServiceActionAsync(
        Func<string, CancellationToken, Task<ServiceOperationResult>> execute)
    {
        if (IsLoading)
        {
            return;
        }

        if (SelectedService is null)
        {
            ErrorMessage = "Select a Tor service first.";
            return;
        }

        IsLoading = true;
        var serviceName = SelectedService.Name;
        try
        {
            var result = await execute(serviceName, CancellationToken.None);
            if (!result.Success)
            {
                ErrorMessage = result.Message;
                StatusMessage = string.Empty;
                return;
            }

            StatusMessage = result.Message;
            ErrorMessage = string.Empty;
            await ReloadAfterChangeAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = exception.Message;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadAfterChangeAsync()
    {
        try
        {
            var result = await _serviceSeeker.Execute();
            if (result.HasError)
            {
                ErrorMessage = result.Message;
            }

            await RefreshRowsAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = $"Configuration was saved, but the service list could not be refreshed: {exception.Message}";
        }
    }

    private async Task RefreshRowsAsync()
    {
        var servicesTask = _servicesStore.LoadAsync();
        var stagesTask = _stagesStore.LoadAsync();
        var startingTypesTask = _startingTypesStore.LoadAsync();
        var configurationsTask = _configurationsStore.LoadAsync();
        await Task.WhenAll(servicesTask, stagesTask, startingTypesTask, configurationsTask);

        var stagesById = (await stagesTask).ToDictionary(item => item.Id);
        var startingTypesById = (await startingTypesTask).ToDictionary(item => item.Id);
        var configurationsById = (await configurationsTask).ToDictionary(item => item.Id);
        var oldSelectedName = SelectedService?.Name;
        var rows = new List<ServiceGridRow>();
        foreach (var service in await servicesTask)
        {
            if (!stagesById.TryGetValue(service.Stage, out var stage) ||
                !startingTypesById.TryGetValue(service.StartingType, out var startingType) ||
                !configurationsById.TryGetValue(service.Configuration, out var configuration))
            {
                throw new InvalidDataException($"Service '{service.Name}' has a missing stage, starting type, or configuration reference.");
            }

            rows.Add(new ServiceGridRow(service, stage.Name, startingType.Name, configuration));
        }

        Services.Clear();
        foreach (var row in rows)
        {
            Services.Add(row);
        }

        SelectedService = rows.FirstOrDefault(row =>
            string.Equals(row.Name, oldSelectedName, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
    }

    private async Task PersistWindowsServiceRemovedAsync(TorService service)
    {
        var services = (await _servicesStore.LoadAsync()).ToList();
        var storedService = services.FirstOrDefault(item => item.Id == service.Id)
            ?? throw new InvalidDataException($"Service '{service.Name}' is missing from the application data.");
        storedService.WindowsServiceRemoved = true;
        service.WindowsServiceRemoved = true;
        await _servicesStore.SaveAsync(services);
    }

    private async Task PersistDeletedServiceAsync(TorService service, string cleanupMessage)
    {
        var services = (await _servicesStore.LoadAsync()).ToList();
        var remainingServices = services
            .Where(item => item.Id != service.Id)
            .ToList();
        var configurations = (await _configurationsStore.LoadAsync()).ToList();
        var removedConfiguration = configurations
            .Where(configuration =>
                configuration.Id == service.Configuration &&
                remainingServices.All(item => item.Configuration != configuration.Id))
            .ToList();

        await _servicesStore.SaveAsync(remainingServices);
        if (removedConfiguration.Count > 0)
        {
            var remainingConfigurations = configurations
                .Except(removedConfiguration)
                .ToList();
            try
            {
                await _configurationsStore.SaveAsync(remainingConfigurations);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                try
                {
                    await _servicesStore.SaveAsync(services);
                }
                catch (Exception rollbackException) when (IsRecoverable(rollbackException))
                {
                    throw new IOException(
                        $"Could not remove the service configuration and could not restore the service record. " +
                        $"Configuration error: {exception.Message}; rollback error: {rollbackException.Message}",
                        rollbackException);
                }

                throw;
            }
        }

        _pendingDeletedService = null;
        DeleteServiceCommand.RaiseCanExecuteChanged();
        try
        {
            await RefreshRowsAsync();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ErrorMessage = $"The service was removed and application data synchronized, but the table could not be refreshed: {exception.Message}";
            StatusMessage = string.Empty;
            return;
        }

        StatusMessage = $"Windows service '{service.Name}' was deleted. {cleanupMessage}";
        ErrorMessage = string.Empty;
    }

    private static bool HasApplicationCreatedFiles(TorService service) =>
        !string.IsNullOrWhiteSpace(service.ApplicationCreatedTorrcPath) &&
        !string.IsNullOrWhiteSpace(service.ApplicationCreatedDataDirectory);

    private void LoadConfigurationEditor(TorConfiguration? configuration)
    {
        SocksPortsText = configuration is null
            ? string.Empty
            : string.Join(", ", configuration.SocksPortEndpoints.Count > 0
                ? configuration.SocksPortEndpoints
                : configuration.SocksPorts.Select(port => port.ToString()));
        UseBridges = configuration?.UseBridges ?? false;
        ClientTransportPluginsText = configuration is null ? string.Empty : string.Join(Environment.NewLine, configuration.ClientTransportPlugins);
        BridgesText = configuration is null ? string.Empty : string.Join(Environment.NewLine, configuration.Bridges);
        LogsText = configuration is null ? string.Empty : string.Join(Environment.NewLine, configuration.Logs);
        ControlPortsText = configuration is null
            ? string.Empty
            : string.Join(", ", configuration.ControlPortEndpoints.Count > 0
                ? configuration.ControlPortEndpoints
                : configuration.ControlPorts.Select(port => port.ToString()));
        DataDirectoryText = configuration?.DataDirectory ?? string.Empty;
    }

    private static (List<int> Ports, List<string> Endpoints) ParsePortEntries(string text, string directive)
    {
        var endpoints = text.Split([',', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (endpoints.Length == 0)
        {
            return ([], []);
        }

        var configuration = new TorConfigParser().Parse(string.Join(
            Environment.NewLine,
            endpoints.Select(endpoint => $"{directive} {endpoint}")));
        return directive == "SocksPort"
            ? (configuration.SocksPorts, configuration.SocksPortEndpoints)
            : (configuration.ControlPorts, configuration.ControlPortEndpoints);
    }

    private static List<string> ParseLines(string text) =>
        text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            FormatException or ArgumentException or InvalidDataException or System.ComponentModel.Win32Exception or
            PlatformNotSupportedException;

    private void RaiseCommandsCanExecuteChanged()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        SaveConfigurationCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        CreateServiceCommand.RaiseCanExecuteChanged();
        DeleteServiceCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
