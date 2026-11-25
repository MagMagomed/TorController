using TorController.App.Models;
using TorController.App.Services;
using TorController.App.Storage;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorServiceSeekerTests
{
    [TestMethod]
    public async Task ExecuteAddsUnknownServiceAndRelatedRecords()
    {
        var stores = new StoreSet();
        var catalog = new FakeServiceCatalog(
            new WindowsServiceInfo(
                "tor",
                "Tor Win32 Service",
                "Running",
                "Automatic",
                @"NT AUTHORITY\LocalService",
                "\"C:\\Tor\\tor.exe\" --nt-service -f \"C:\\Tor\\torrc\""));
        var reader = new FakeConfigurationReader(new TorConfiguration
        {
            SocksPorts = [9050],
            UseBridges = true,
            Bridges = ["obfs4 192.0.2.10:443 FINGERPRINT cert=example iat-mode=0"]
        });
        var seeker = stores.CreateSeeker(catalog, reader);

        var result = await seeker.Execute();

        Assert.IsFalse(result.HasError);
        Assert.AreEqual(1, stores.Services.Items.Count);
        var service = stores.Services.Items[0];
        Assert.AreEqual("tor", service.Name);
        Assert.AreEqual("Tor Win32 Service", service.DisplayName);
        Assert.AreEqual("C:\\Tor\\tor.exe", service.TorExecutablePath);
        Assert.AreEqual("C:\\Tor\\torrc", service.ConfigurationFilePath);
        Assert.AreEqual(@"NT AUTHORITY\LocalService", service.ServiceAccount);
        Assert.IsNull(service.ApplicationCreatedTorrcPath);
        Assert.IsNull(service.ApplicationCreatedDataDirectory);
        Assert.IsFalse(service.WindowsServiceRemoved);
        Assert.AreEqual("Running", stores.Stages.Items.Single(item => item.Id == service.Stage).Name);
        Assert.AreEqual("Automatic", stores.StartingTypes.Items.Single(item => item.Id == service.StartingType).Name);
        Assert.AreEqual(9050, stores.Configurations.Items.Single(item => item.Id == service.Configuration).SocksPorts.Single());
    }

    [TestMethod]
    public async Task ExecuteUpdatesExistingServiceByNameAndPreservesId()
    {
        var existingId = Guid.NewGuid();
        var stores = new StoreSet();
        stores.Services.Items.Add(new TorService
        {
            Id = existingId,
            Name = "tor",
            DisplayName = "Old name",
            ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc",
            ApplicationCreatedDataDirectory = @"C:\Tor\Data",
            WindowsServiceRemoved = true
        });
        var seeker = stores.CreateSeeker(
            new FakeServiceCatalog(new WindowsServiceInfo(
                "tor", "Tor Win32 Service", "Running", "Automatic", "LocalService",
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc\"")),
            new FakeConfigurationReader(new TorConfiguration { SocksPorts = [9050] }));

        var result = await seeker.Execute();

        Assert.IsFalse(result.HasError);
        Assert.AreEqual(1, stores.Services.Items.Count);
        Assert.AreEqual(existingId, stores.Services.Items[0].Id);
        Assert.AreEqual("Tor Win32 Service", stores.Services.Items[0].DisplayName);
        Assert.AreEqual(@"C:\Tor\Data\torrc", stores.Services.Items[0].ApplicationCreatedTorrcPath);
        Assert.AreEqual(@"C:\Tor\Data", stores.Services.Items[0].ApplicationCreatedDataDirectory);
        Assert.IsFalse(stores.Services.Items[0].WindowsServiceRemoved);
    }

    [TestMethod]
    public async Task ExecuteKeepsSuccessfulServiceWhenAnotherConfigurationFails()
    {
        var stores = new StoreSet();
        var catalog = new FakeServiceCatalog(
            new WindowsServiceInfo("tor", "Tor", "Running", "Automatic", "LocalService",
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc\""),
            new WindowsServiceInfo("tor2", "Tor 2", "Stopped", "Manual", "LocalSystem",
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc2\""));
        var reader = new FakeConfigurationReader(new TorConfiguration { SocksPorts = [9050] });
        reader.FailingPaths.Add("C:\\Tor\\torrc2");
        var seeker = stores.CreateSeeker(catalog, reader);

        var result = await seeker.Execute();

        Assert.IsTrue(result.HasError);
        Assert.IsTrue(result.Message.Contains("tor2", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(stores.Services.Items.Any(service => service.Name == "tor"));
        Assert.IsTrue(stores.Services.Items.Any(service => service.Name == "tor2"));
    }

    [TestMethod]
    public async Task ExecuteDoesNotDuplicateServicesOnRepeatedSearch()
    {
        var stores = new StoreSet();
        var catalog = new FakeServiceCatalog(
            new WindowsServiceInfo("tor", "Tor", "Running", "Automatic", "LocalService",
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc\""));
        var seeker = stores.CreateSeeker(catalog, new FakeConfigurationReader(new TorConfiguration()));

        await seeker.Execute();
        var firstId = stores.Services.Items.Single().Id;
        await seeker.Execute();

        Assert.AreEqual(1, stores.Services.Items.Count);
        Assert.AreEqual(firstId, stores.Services.Items.Single().Id);
    }

    [TestMethod]
    public async Task ExecuteReportsFieldsThatCouldNotBeRead()
    {
        var stores = new StoreSet();
        var seeker = stores.CreateSeeker(
            new FakeServiceCatalog(new WindowsServiceInfo(
                "tor",
                "Tor",
                "Unknown",
                "Unknown",
                string.Empty,
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc\"")),
            new FakeConfigurationReader(new TorConfiguration()));

        var result = await seeker.Execute();

        Assert.IsTrue(result.HasError);
        StringAssert.Contains(result.Message, "state");
        StringAssert.Contains(result.Message, "startup type");
        StringAssert.Contains(result.Message, "account");
    }

    [TestMethod]
    public async Task ExecuteRetainsStoredConfigurationWhenReadingUpdatedTorrcFails()
    {
        var stores = new StoreSet();
        var serviceId = Guid.NewGuid();
        var configurationId = Guid.NewGuid();
        stores.Configurations.Items.Add(new TorConfiguration
        {
            Id = configurationId,
            SocksPorts = [9050],
            SocksPortEndpoints = ["127.0.0.1:9050"]
        });
        stores.Services.Items.Add(new TorService
        {
            Id = serviceId,
            Name = "tor",
            Configuration = configurationId
        });
        var reader = new FakeConfigurationReader(new TorConfiguration { SocksPorts = [9052] });
        reader.FailingPaths.Add("C:\\Tor\\torrc");
        var seeker = stores.CreateSeeker(
            new FakeServiceCatalog(new WindowsServiceInfo(
                "tor", "Tor", "Running", "Automatic", "LocalService",
                "\"C:\\Tor\\tor.exe\" -f \"C:\\Tor\\torrc\"")),
            reader);

        var result = await seeker.Execute();

        Assert.IsTrue(result.HasError);
        Assert.AreEqual(serviceId, stores.Services.Items.Single().Id);
        Assert.AreEqual(configurationId, stores.Services.Items.Single().Configuration);
        Assert.AreEqual(9050, stores.Configurations.Items.Single().SocksPorts.Single());
        Assert.AreEqual("127.0.0.1:9050", stores.Configurations.Items.Single().SocksPortEndpoints.Single());
    }

    private sealed class FakeServiceCatalog(params WindowsServiceInfo[] services) : IWindowsServiceCatalog
    {
        public Task<IReadOnlyList<WindowsServiceInfo>> FindTorServicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindowsServiceInfo>>(services);

        public Task<bool> ServiceNameExistsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(services.Any(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FakeConfigurationReader(TorConfiguration configuration) : ITorConfigurationReader
    {
        public HashSet<string> FailingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<TorConfiguration> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            FailingPaths.Contains(path)
                ? Task.FromException<TorConfiguration>(new IOException("Cannot read configuration"))
                : Task.FromResult(configuration);
    }

    private sealed class StoreSet
    {
        public MemoryStore<TorService> Services { get; } = new();
        public MemoryStore<ServiceStage> Stages { get; } = new();
        public MemoryStore<ServiceStartingType> StartingTypes { get; } = new();
        public MemoryStore<TorConfiguration> Configurations { get; } = new();

        public TorServiceSeeker CreateSeeker(IWindowsServiceCatalog catalog, ITorConfigurationReader reader) =>
            new(catalog, reader, Services, Stages, StartingTypes, Configurations);
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
}
