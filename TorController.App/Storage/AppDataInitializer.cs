using TorController.App.Models;
using System.IO;

namespace TorController.App.Storage;

public sealed class AppDataInitializer
{
    private readonly string _dataDirectory;

    public AppDataInitializer(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dataDirectory);
        var servicesPath = Path.Combine(_dataDirectory, "tor-services.json");
        var stagesPath = Path.Combine(_dataDirectory, "stages.json");
        var startingTypesPath = Path.Combine(_dataDirectory, "starting-types.json");
        var configurationsPath = Path.Combine(_dataDirectory, "configurations.json");
        var isFirstRun = !File.Exists(servicesPath);

        var servicesStore = new JsonCollectionStore<TorService>(servicesPath);
        var stagesStore = new JsonCollectionStore<ServiceStage>(stagesPath);
        var startingTypesStore = new JsonCollectionStore<ServiceStartingType>(startingTypesPath);
        var configurationsStore = new JsonCollectionStore<TorConfiguration>(configurationsPath);

        var services = (await servicesStore.LoadAsync(cancellationToken)).ToList();
        var stages = (await stagesStore.LoadAsync(cancellationToken)).ToList();
        var startingTypes = (await startingTypesStore.LoadAsync(cancellationToken)).ToList();
        var configurations = (await configurationsStore.LoadAsync(cancellationToken)).ToList();

        var stagesChanged = EnsureEntries(stages, ["Running", "Stopped", "StartPending", "StopPending", "Paused", "Unknown"],
            name => new ServiceStage { Id = Guid.NewGuid(), Name = name }, item => item.Name);
        var startingTypesChanged = EnsureEntries(
            startingTypes,
            ["Automatic", "Automatic (Delayed Start)", "Manual", "Disabled", "Unknown"],
            name => new ServiceStartingType { Id = Guid.NewGuid(), Name = name },
            item => item.Name);

        ValidateReferences(services, stages, startingTypes, configurations);

        if (stagesChanged || !File.Exists(stagesPath))
        {
            await stagesStore.SaveAsync(stages, cancellationToken);
        }

        if (startingTypesChanged || !File.Exists(startingTypesPath))
        {
            await startingTypesStore.SaveAsync(startingTypes, cancellationToken);
        }

        if (isFirstRun || !File.Exists(configurationsPath))
        {
            await configurationsStore.SaveAsync(configurations, cancellationToken);
        }

        if (isFirstRun)
        {
            await servicesStore.SaveAsync(services, cancellationToken);
        }
    }

    private static bool EnsureEntries<T>(
        ICollection<T> items,
        IEnumerable<string> names,
        Func<string, T> create,
        Func<T, string> getName)
    {
        var changed = false;
        foreach (var name in names)
        {
            if (items.Any(item => string.Equals(getName(item), name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(create(name));
            changed = true;
        }

        return changed;
    }

    private static void ValidateReferences(
        IEnumerable<TorService> services,
        IReadOnlyCollection<ServiceStage> stages,
        IReadOnlyCollection<ServiceStartingType> startingTypes,
        IReadOnlyCollection<TorConfiguration> configurations)
    {
        foreach (var service in services)
        {
            if (!stages.Any(item => item.Id == service.Stage) ||
                !startingTypes.Any(item => item.Id == service.StartingType) ||
                !configurations.Any(item => item.Id == service.Configuration))
            {
                throw new InvalidDataException(
                    $"Service '{service.Name}' contains a reference to a missing stage, starting type, or configuration.");
            }
        }
    }
}
