using TorController.App.Models;
using TorController.App.Storage;

namespace TorController.Tests.Storage;

[TestClass]
public sealed class AppDataInitializerTests
{
    [TestMethod]
    public async Task InitializeAsyncCreatesEmptyServiceCollectionsAndReferenceCatalogs()
    {
        using var directory = new TemporaryDirectory();
        var initializer = new AppDataInitializer(directory.Path);

        await initializer.InitializeAsync();

        var services = await Load<TorService>(directory.Path, "tor-services.json");
        var stages = await Load<ServiceStage>(directory.Path, "stages.json");
        var startingTypes = await Load<ServiceStartingType>(directory.Path, "starting-types.json");
        var configurations = await Load<TorConfiguration>(directory.Path, "configurations.json");
        Assert.AreEqual(0, services.Count);
        Assert.AreEqual(0, configurations.Count);
        Assert.IsTrue(stages.Any(item => item.Name == "Running"));
        Assert.IsTrue(startingTypes.Any(item => item.Name == "Automatic"));
        Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "tor-services.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "stages.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "starting-types.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "configurations.json")));
        await initializer.InitializeAsync();
        Assert.AreEqual(0, (await Load<TorService>(directory.Path, "tor-services.json")).Count);
        Assert.AreEqual(0, (await Load<TorConfiguration>(directory.Path, "configurations.json")).Count);
    }

    [TestMethod]
    public async Task InitializeAsyncPreservesExistingIdsAndDoesNotAddTorToExistingDatabase()
    {
        using var directory = new TemporaryDirectory();
        var initializer = new AppDataInitializer(directory.Path);
        await initializer.InitializeAsync();

        var store = new JsonCollectionStore<TorService>(Path.Combine(directory.Path, "tor-services.json"));
        var stages = await Load<ServiceStage>(directory.Path, "stages.json");
        var startingTypes = await Load<ServiceStartingType>(directory.Path, "starting-types.json");
        var configurationsStore = new JsonCollectionStore<TorConfiguration>(
            Path.Combine(directory.Path, "configurations.json"));
        var configuration = new TorConfiguration { Id = Guid.NewGuid(), SocksPorts = [9050] };
        await configurationsStore.SaveAsync([configuration]);
        var service = new TorService
        {
            Id = Guid.NewGuid(),
            Name = "mock-tor",
            DisplayName = "Mock Tor Service",
            Stage = stages.Single(item => item.Name == "Stopped").Id,
            StartingType = startingTypes.Single(item => item.Name == "Manual").Id,
            Configuration = configuration.Id,
            TorExecutablePath = @"C:\MockTor\tor.exe",
            ConfigurationFilePath = @"C:\MockTor\torrc",
            ServiceAccount = "MockAccount"
        };
        await store.SaveAsync([service]);

        await initializer.InitializeAsync();

        var reloaded = await store.LoadAsync();
        Assert.AreEqual(1, reloaded.Count);
        Assert.AreEqual(service.Id, reloaded[0].Id);
        Assert.AreEqual("Mock Tor Service", reloaded[0].DisplayName);
    }

    [TestMethod]
    public async Task InitializeAsyncReportsInvalidReferences()
    {
        using var directory = new TemporaryDirectory();
        var initializer = new AppDataInitializer(directory.Path);
        await initializer.InitializeAsync();
        var store = new JsonCollectionStore<TorService>(Path.Combine(directory.Path, "tor-services.json"));
        var startingTypes = await Load<ServiceStartingType>(directory.Path, "starting-types.json");
        var configurationsStore = new JsonCollectionStore<TorConfiguration>(
            Path.Combine(directory.Path, "configurations.json"));
        var configuration = new TorConfiguration { Id = Guid.NewGuid() };
        await configurationsStore.SaveAsync([configuration]);
        await store.SaveAsync([
            new TorService
            {
                Id = Guid.NewGuid(),
                Name = "mock-tor",
                Stage = Guid.NewGuid(),
                StartingType = startingTypes.Single(item => item.Name == "Manual").Id,
                Configuration = configuration.Id
            }
        ]);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => initializer.InitializeAsync());
    }

    [TestMethod]
    public async Task InitializeAsyncDoesNotSeedAServiceCollectionThatAlreadyExists()
    {
        using var directory = new TemporaryDirectory();
        var servicesStore = new JsonCollectionStore<TorService>(Path.Combine(directory.Path, "tor-services.json"));
        await servicesStore.SaveAsync([]);
        var initializer = new AppDataInitializer(directory.Path);

        await initializer.InitializeAsync();

        Assert.AreEqual(0, (await servicesStore.LoadAsync()).Count);
    }

    private static Task<IReadOnlyList<T>> Load<T>(string directory, string fileName) =>
        new JsonCollectionStore<T>(Path.Combine(directory, fileName)).LoadAsync();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
