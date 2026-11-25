using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Storage;

namespace TorController.App.Services;

public sealed class TorServiceCreator : ITorServiceCreator
{
    private static readonly HashSet<string> SupportedStartingTypes = new(StringComparer.Ordinal)
    {
        "Automatic",
        "Automatic (Delayed Start)",
        "Manual",
        "Disabled"
    };
    private static readonly Regex SensitiveConfigurationPattern = new(
        @"password|passphrase|private[\s_-]*key|cookie|credential|secret|onionauth|-----BEGIN [A-Z ]*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem;
    private readonly IWindowsServiceCatalog _serviceCatalog;
    private readonly IJsonCollectionStore<TorService> _servicesStore;
    private readonly IJsonCollectionStore<TorConfiguration> _configurationsStore;
    private readonly TorConfigurationVerifier _verifier;
    private readonly TorConfigDocumentEditor _documentEditor;
    private readonly IWindowsServiceRegistrar _serviceRegistrar;
    private readonly ITorServiceSeeker _serviceSeeker;

    public TorServiceCreator(
        IFileSystem fileSystem,
        IWindowsServiceCatalog serviceCatalog,
        IJsonCollectionStore<TorService> servicesStore,
        IJsonCollectionStore<TorConfiguration> configurationsStore,
        TorConfigurationVerifier verifier,
        TorConfigDocumentEditor documentEditor,
        IWindowsServiceRegistrar serviceRegistrar,
        ITorServiceSeeker serviceSeeker)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _serviceCatalog = serviceCatalog ?? throw new ArgumentNullException(nameof(serviceCatalog));
        _servicesStore = servicesStore ?? throw new ArgumentNullException(nameof(servicesStore));
        _configurationsStore = configurationsStore ?? throw new ArgumentNullException(nameof(configurationsStore));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _documentEditor = documentEditor ?? throw new ArgumentNullException(nameof(documentEditor));
        _serviceRegistrar = serviceRegistrar ?? throw new ArgumentNullException(nameof(serviceRegistrar));
        _serviceSeeker = serviceSeeker ?? throw new ArgumentNullException(nameof(serviceSeeker));
    }

    public async Task<TorServiceCreationResult> CreateAsync(
        TorServiceCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string? validationMessage;
        try
        {
            validationMessage = ValidateRequest(request);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure($"The service request contains an invalid path or value: {exception.Message}");
        }

        if (validationMessage is not null)
        {
            return Failure(validationMessage);
        }

        string torExecutablePath;
        string dataDirectory;
        string configurationPath;
        try
        {
            torExecutablePath = Path.GetFullPath(request.TorExecutablePath);
            dataDirectory = Path.GetFullPath(request.DataDirectory);
            configurationPath = Path.Combine(dataDirectory, "torrc");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return Failure($"The service paths are invalid: {exception.Message}");
        }

        try
        {
            if (!_fileSystem.FileExists(torExecutablePath))
            {
                return Failure("The selected tor.exe file does not exist.");
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure($"Could not access the selected tor.exe file: {exception.Message}");
        }

        List<TorService> existingServices;
        List<TorConfiguration> existingConfigurations;
        try
        {
            existingServices = (await _servicesStore.LoadAsync(cancellationToken)).ToList();
            existingConfigurations = (await _configurationsStore.LoadAsync(cancellationToken)).ToList();
            if (existingServices.Any(service =>
                    string.Equals(service.Name, request.ServiceName, StringComparison.OrdinalIgnoreCase)))
            {
                return Failure($"A service named '{request.ServiceName}' already exists in the application data.");
            }

            if (await _serviceCatalog.ServiceNameExistsAsync(request.ServiceName, cancellationToken))
            {
                return Failure($"A Windows service named '{request.ServiceName}' is already registered.");
            }

            if (_fileSystem.DirectoryExists(dataDirectory) ||
                _fileSystem.FileExists(dataDirectory) ||
                _fileSystem.FileExists(configurationPath))
            {
                return Failure(
                    $"The selected data directory or configuration file already exists; no files were changed. Path: {dataDirectory}",
                    configurationPath,
                    dataDirectory);
            }

            if (existingServices.Any(service =>
                    PathsOverlap(service.ConfigurationFilePath, dataDirectory) ||
                    PathsOverlap(service.ConfigurationFilePath, configurationPath)) ||
                existingConfigurations.Any(configuration => PathsOverlap(configuration.DataDirectory, dataDirectory)))
            {
                return Failure(
                    $"The selected paths are already referenced by a saved Tor service. Path: {dataDirectory}",
                    configurationPath,
                    dataDirectory);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure($"Could not check for existing services or paths: {exception.Message}");
        }

        var configuration = CopyConfiguration(request.Configuration, dataDirectory);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.CreateDirectory(dataDirectory);
            var document = _documentEditor.Apply(string.Empty, configuration);
            await _fileSystem.WriteAllTextAsync(configurationPath, document, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure(
                $"Could not prepare the new service files. Any created files were left in place. Data directory: {dataDirectory}; torrc: {configurationPath}. {exception.Message}",
                configurationPath,
                dataDirectory);
        }

        ConfigurationVerificationResult verification;
        try
        {
            verification = await _verifier.VerifyAsync(torExecutablePath, configurationPath, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure(
                $"The torrc file was prepared but could not be verified. No service was registered. Data directory: {dataDirectory}; torrc: {configurationPath}. {exception.Message}",
                configurationPath,
                dataDirectory);
        }

        if (!verification.IsValid)
        {
            return Failure(
                $"The torrc file was prepared but failed validation. No service was registered. {verification.Message} Data directory: {dataDirectory}; torrc: {configurationPath}",
                configurationPath,
                dataDirectory);
        }

        try
        {
            await _serviceRegistrar.RegisterAsync(
                request.ServiceName,
                request.DisplayName,
                torExecutablePath,
                configurationPath,
                request.StartingType,
                cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure(
                $"Windows service '{request.ServiceName}' was not registered. Prepared files were retained. Data directory: {dataDirectory}; torrc: {configurationPath}. {GetSafeRegistrationMessage(exception)}",
                configurationPath,
                dataDirectory);
        }

        SeekResult? seekResult = null;
        string? discoveryError = null;
        try
        {
            seekResult = await _serviceSeeker.Execute(cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception) || exception is OperationCanceledException)
        {
            discoveryError = exception.Message;
        }

        string? ownershipError = null;
        try
        {
            await MarkApplicationCreatedFilesAsync(
                request.ServiceName,
                configurationPath,
                dataDirectory,
                cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception) || exception is OperationCanceledException)
        {
            ownershipError = exception.Message;
        }

        if (discoveryError is not null || ownershipError is not null || seekResult?.HasError == true)
        {
            var details = new[]
            {
                seekResult?.HasError == true ? seekResult.Message : null,
                discoveryError is null ? null : $"Service discovery: {discoveryError}",
                ownershipError is null ? null : $"File ownership metadata: {ownershipError}"
            }.Where(message => !string.IsNullOrWhiteSpace(message));
            return new TorServiceCreationResult(
                false,
                true,
                $"Windows service '{request.ServiceName}' is registered but its details could not be fully saved. " +
                $"Refresh the service list to retry. {string.Join(" ", details)}",
                configurationPath,
                dataDirectory);
        }

        return new TorServiceCreationResult(
            true,
            true,
            $"Windows service '{request.ServiceName}' was registered and added to the service list. It remains stopped until you start it.",
            configurationPath,
            dataDirectory);
    }

    private async Task MarkApplicationCreatedFilesAsync(
        string serviceName,
        string configurationPath,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        var services = (await _servicesStore.LoadAsync(cancellationToken)).ToList();
        var service = services.FirstOrDefault(item =>
            string.Equals(item.Name, serviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Service '{serviceName}' was not found in application data after discovery.");
        service.ApplicationCreatedTorrcPath = configurationPath;
        service.ApplicationCreatedDataDirectory = dataDirectory;
        service.WindowsServiceRemoved = false;
        await _servicesStore.SaveAsync(services, cancellationToken);
    }

    private static string? ValidateRequest(TorServiceCreationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceName) ||
            request.ServiceName.Length > 256 ||
            !char.IsAsciiLetterOrDigit(request.ServiceName[0]) ||
            request.ServiceName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            return "Enter a valid Windows service name (1-256 letters, digits, '.', '_' or '-').";
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) ||
            request.DisplayName.Length > 256 ||
            request.DisplayName.Any(char.IsControl))
        {
            return "Enter a display name of 1-256 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.TorExecutablePath) ||
            !Path.IsPathFullyQualified(request.TorExecutablePath) ||
            !string.Equals(Path.GetFileName(request.TorExecutablePath), "tor.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Select an absolute path to tor.exe.";
        }

        if (string.IsNullOrWhiteSpace(request.DataDirectory) ||
            !Path.IsPathFullyQualified(request.DataDirectory))
        {
            return "Enter an absolute path for the new service data directory.";
        }

        if (!SupportedStartingTypes.Contains(request.StartingType))
        {
            return "Select a supported Windows service startup type.";
        }

        if (request.Configuration is null)
        {
            return "Tor configuration is required.";
        }

        if (request.Configuration.SocksPorts is null ||
            request.Configuration.SocksPortEndpoints is null ||
            request.Configuration.ClientTransportPlugins is null ||
            request.Configuration.Bridges is null ||
            request.Configuration.Logs is null ||
            request.Configuration.ControlPorts is null ||
            request.Configuration.ControlPortEndpoints is null)
        {
            return "Tor configuration contains missing values.";
        }

        var values = request.Configuration.SocksPortEndpoints
            .Concat(request.Configuration.ClientTransportPlugins)
            .Concat(request.Configuration.Bridges)
            .Concat(request.Configuration.Logs)
            .Concat(request.Configuration.ControlPortEndpoints);
        if (values.Any(value => value is null || value.Contains('\r') || value.Contains('\n')))
        {
            return "Each Tor configuration entry must contain exactly one directive value.";
        }

        if (values.Any(value => SensitiveConfigurationPattern.IsMatch(value)))
        {
            return "Tor configuration entries must not contain passwords, keys, cookies, or other credentials.";
        }

        return null;
    }

    private static TorConfiguration CopyConfiguration(TorConfiguration source, string dataDirectory) =>
        new()
        {
            SocksPorts = [.. source.SocksPorts],
            SocksPortEndpoints = [.. source.SocksPortEndpoints],
            UseBridges = source.UseBridges,
            ClientTransportPlugins = [.. source.ClientTransportPlugins],
            Bridges = [.. source.Bridges],
            Logs = [.. source.Logs],
            ControlPorts = [.. source.ControlPorts],
            ControlPortEndpoints = [.. source.ControlPortEndpoints],
            DataDirectory = dataDirectory
        };

    private static bool PathsOverlap(string? firstPath, string secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath))
        {
            return false;
        }

        var first = Path.GetFullPath(firstPath);
        var second = Path.GetFullPath(secondPath);
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
               IsChildPath(first, second) ||
               IsChildPath(second, first);
    }

    private static bool IsChildPath(string path, string parent)
    {
        var parentWithSeparator = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static TorServiceCreationResult Failure(
        string message,
        string? configurationPath = null,
        string? dataDirectory = null) =>
        new(false, false, message, configurationPath, dataDirectory);

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            ArgumentException or NotSupportedException or Win32Exception or FormatException;

    private static string GetSafeRegistrationMessage(Exception exception) =>
        exception is Win32Exception { NativeErrorCode: 1223 }
            ? "Administrator approval was cancelled."
            : exception.Message;
}
