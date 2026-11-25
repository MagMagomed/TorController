using TorController.App.Models;
using TorController.App.Storage;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TorController.App.Services;

public sealed class TorServiceSeeker : ITorServiceSeeker
{
    private readonly IWindowsServiceCatalog _serviceCatalog;
    private readonly ITorConfigurationReader _configurationReader;
    private readonly IJsonCollectionStore<TorService> _servicesStore;
    private readonly IJsonCollectionStore<ServiceStage> _stagesStore;
    private readonly IJsonCollectionStore<ServiceStartingType> _startingTypesStore;
    private readonly IJsonCollectionStore<TorConfiguration> _configurationsStore;

    public TorServiceSeeker(
        IWindowsServiceCatalog serviceCatalog,
        ITorConfigurationReader configurationReader,
        IJsonCollectionStore<TorService> servicesStore,
        IJsonCollectionStore<ServiceStage> stagesStore,
        IJsonCollectionStore<ServiceStartingType> startingTypesStore,
        IJsonCollectionStore<TorConfiguration> configurationsStore)
    {
        _serviceCatalog = serviceCatalog ?? throw new ArgumentNullException(nameof(serviceCatalog));
        _configurationReader = configurationReader ?? throw new ArgumentNullException(nameof(configurationReader));
        _servicesStore = servicesStore ?? throw new ArgumentNullException(nameof(servicesStore));
        _stagesStore = stagesStore ?? throw new ArgumentNullException(nameof(stagesStore));
        _startingTypesStore = startingTypesStore ?? throw new ArgumentNullException(nameof(startingTypesStore));
        _configurationsStore = configurationsStore ?? throw new ArgumentNullException(nameof(configurationsStore));
    }

    public async Task<SeekResult> Execute(CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        List<TorService> services;
        List<ServiceStage> stages;
        List<ServiceStartingType> startingTypes;
        List<TorConfiguration> configurations;

        try
        {
            services = (await _servicesStore.LoadAsync(cancellationToken)).ToList();
            stages = (await _stagesStore.LoadAsync(cancellationToken)).ToList();
            startingTypes = (await _startingTypesStore.LoadAsync(cancellationToken)).ToList();
            configurations = (await _configurationsStore.LoadAsync(cancellationToken)).ToList();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new SeekResult(true, $"Could not load Tor service data: {exception.Message}");
        }

        IReadOnlyList<WindowsServiceInfo> discoveredServices;
        try
        {
            discoveredServices = await _serviceCatalog.FindTorServicesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new SeekResult(true, $"Could not enumerate Windows services: {exception.Message}");
        }

        foreach (var serviceGroup in discoveredServices.GroupBy(service => service.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serviceInfo = serviceGroup.First();
            if (string.IsNullOrWhiteSpace(serviceInfo.Name))
            {
                messages.Add("A Tor-related Windows service had no system name and could not be stored.");
                continue;
            }

            if (serviceGroup.Skip(1).Any())
            {
                messages.Add($"Multiple Tor service entries were reported for '{serviceInfo.Name}'; only one was processed.");
            }
            if (string.IsNullOrWhiteSpace(serviceInfo.DisplayName))
            {
                messages.Add($"The display name for service '{serviceInfo.Name}' could not be determined.");
            }
            if (string.IsNullOrWhiteSpace(serviceInfo.ServiceAccount))
            {
                messages.Add($"The account for service '{serviceInfo.Name}' could not be determined.");
            }
            if (!IsKnownStage(serviceInfo.State))
            {
                messages.Add($"The current state for service '{serviceInfo.Name}' could not be determined.");
            }
            if (!IsKnownStartingType(serviceInfo.StartingType))
            {
                messages.Add($"The startup type for service '{serviceInfo.Name}' could not be determined.");
            }

            var existing = services.FirstOrDefault(service =>
                string.Equals(service.Name, serviceInfo.Name, StringComparison.OrdinalIgnoreCase));
            string executablePath;
            string configurationPath;
            try
            {
                (executablePath, configurationPath) = ParseServiceCommandLine(serviceInfo.CommandLine);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                executablePath = string.Empty;
                configurationPath = string.Empty;
                messages.Add($"Could not parse the start command for service '{serviceInfo.Name}': {exception.Message}");
            }
            if (executablePath.Length == 0)
            {
                messages.Add($"Could not determine tor.exe for service '{serviceInfo.Name}'.");
            }

            TorConfiguration? configuration = null;
            if (configurationPath.Length == 0)
            {
                messages.Add($"Could not determine the torrc path for service '{serviceInfo.Name}'.");
            }
            else
            {
                try
                {
                    configuration = await _configurationReader.ReadAsync(configurationPath, cancellationToken);
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    messages.Add($"Could not read configuration for service '{serviceInfo.Name}': {exception.Message}");
                }
            }

            var existingConfiguration = existing is null
                ? null
                : configurations.FirstOrDefault(item => item.Id == existing.Configuration);
            var configurationRecord = configuration is null
                ? existingConfiguration ?? new TorConfiguration { Id = Guid.NewGuid() }
                : CopyConfiguration(configuration, existingConfiguration?.Id ?? Guid.NewGuid());

            if (existingConfiguration is null || configuration is not null)
            {
                Upsert(configurations, configurationRecord, item => item.Id);
            }

            var serviceRecord = existing ?? new TorService
            {
                Id = Guid.NewGuid(),
                Name = serviceInfo.Name
            };
            serviceRecord.Name = serviceInfo.Name;
            serviceRecord.DisplayName = serviceInfo.DisplayName;
            serviceRecord.Stage = EnsureStage(stages, serviceInfo.State).Id;
            serviceRecord.StartingType = EnsureStartingType(startingTypes, serviceInfo.StartingType).Id;
            serviceRecord.Configuration = configurationRecord.Id;
            serviceRecord.TorExecutablePath = executablePath.Length > 0 ? executablePath : existing?.TorExecutablePath ?? string.Empty;
            serviceRecord.ConfigurationFilePath = configurationPath.Length > 0
                ? configurationPath
                : existing?.ConfigurationFilePath ?? string.Empty;
            serviceRecord.ServiceAccount = serviceInfo.ServiceAccount;
            serviceRecord.WindowsServiceRemoved = false;
            Upsert(services, serviceRecord, item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        await SaveCollectionAsync(_servicesStore, services, "services", messages, cancellationToken);
        await SaveCollectionAsync(_stagesStore, stages, "service stages", messages, cancellationToken);
        await SaveCollectionAsync(_startingTypesStore, startingTypes, "service starting types", messages, cancellationToken);
        await SaveCollectionAsync(_configurationsStore, configurations, "Tor configurations", messages, cancellationToken);

        return new SeekResult(messages.Count > 0, string.Join(Environment.NewLine, messages));
    }

    private async Task SaveCollectionAsync<T>(
        IJsonCollectionStore<T> store,
        IReadOnlyCollection<T> items,
        string description,
        ICollection<string> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(items, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            messages.Add($"Could not save {description}: {exception.Message}");
        }
    }

    private static ServiceStage EnsureStage(ICollection<ServiceStage> stages, string name)
    {
        var normalizedName = (name ?? string.Empty).ToLowerInvariant() switch
        {
            "running" => "Running",
            "stopped" => "Stopped",
            "startpending" => "StartPending",
            "stoppending" => "StopPending",
            "paused" => "Paused",
            _ => "Unknown"
        };
        var existing = stages.FirstOrDefault(stage =>
            string.Equals(stage.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var stage = new ServiceStage { Id = Guid.NewGuid(), Name = normalizedName };
        stages.Add(stage);
        return stage;
    }

    private static ServiceStartingType EnsureStartingType(ICollection<ServiceStartingType> startingTypes, string name)
    {
        var normalizedName = (name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "auto" or "automatic" => "Automatic",
            "automatic (delayed start)" or "automatic (delayed)" => "Automatic (Delayed Start)",
            "manual" => "Manual",
            "disabled" => "Disabled",
            _ => "Unknown"
        };
        var existing = startingTypes.FirstOrDefault(type =>
            string.Equals(type.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var startingType = new ServiceStartingType { Id = Guid.NewGuid(), Name = normalizedName };
        startingTypes.Add(startingType);
        return startingType;
    }

    private static TorConfiguration CopyConfiguration(TorConfiguration source, Guid id) =>
        new()
        {
            Id = id,
            SocksPorts = [.. source.SocksPorts],
            SocksPortEndpoints = [.. source.SocksPortEndpoints],
            UseBridges = source.UseBridges,
            ClientTransportPlugins = [.. source.ClientTransportPlugins],
            Bridges = [.. source.Bridges],
            Logs = [.. source.Logs],
            ControlPorts = [.. source.ControlPorts],
            ControlPortEndpoints = [.. source.ControlPortEndpoints],
            DataDirectory = source.DataDirectory,
            AdditionalDirectives = source.AdditionalDirectives.ToDictionary(
                item => item.Key,
                item => new List<string>(item.Value),
                StringComparer.OrdinalIgnoreCase)
        };

    private static void Upsert<T, TKey>(
        ICollection<T> items,
        T item,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        var existing = items.FirstOrDefault(candidate =>
            (comparer ?? EqualityComparer<TKey>.Default).Equals(keySelector(candidate), keySelector(item)));
        if (existing is not null)
        {
            var index = items.ToList().IndexOf(existing);
            if (items is IList<T> list)
            {
                list[index] = item;
                return;
            }

            items.Remove(existing);
        }

        items.Add(item);
    }

    private static (string ExecutablePath, string ConfigurationPath) ParseServiceCommandLine(string commandLine)
    {
        var arguments = ParseArguments(commandLine);
        var executable = arguments.FirstOrDefault(argument =>
            string.Equals(Path.GetFileName(argument), "tor.exe", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var configuration = string.Empty;
        for (var index = 0; index < arguments.Count; index++)
        {
            if ((arguments[index] is "-f" or "--torrc-file") && index + 1 < arguments.Count)
            {
                configuration = arguments[index + 1];
                break;
            }

            if (arguments[index].StartsWith("-f", StringComparison.Ordinal) && arguments[index].Length > 2)
            {
                configuration = arguments[index][2..];
                break;
            }
        }

        return (
            executable.Length > 0 ? Path.GetFullPath(executable) : string.Empty,
            configuration.Length > 0 ? Path.GetFullPath(configuration) : string.Empty);
    }

    private static List<string> ParseArguments(string commandLine)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaped = false;

        foreach (var character in commandLine)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaped = true;
                current.Append(character);
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static bool IsKnownStage(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Stopped", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("StartPending", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("StopPending", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Paused", StringComparison.OrdinalIgnoreCase));

    private static bool IsKnownStartingType(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Automatic (Delayed Start)", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Automatic (Delayed)", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Disabled", StringComparison.OrdinalIgnoreCase));

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or FormatException or
            InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception;
}
