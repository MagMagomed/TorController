using System.IO;
using System.ComponentModel;
using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Services;
using TorController.App.Storage;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorServiceCreatorTests
{
    [TestMethod]
    public async Task CreateAsyncPreparesVerifiesRegistersAndDiscoversService()
    {
        var fixture = new Fixture();
        var request = CreateRequest();
        var order = new List<string>();
        fixture.Verifier = new TorConfigurationVerifier((_, path, _) =>
        {
            order.Add("verify");
            Assert.AreEqual(@"C:\Tor\NewData\torrc", path);
            return Task.FromResult(0);
        });
        fixture.Registrar.Register = (_, _, _, _, _, _) =>
        {
            order.Add("register");
            return Task.CompletedTask;
        };
        fixture.Seeker.ExecuteAction = _ =>
        {
            order.Add("discover");
            fixture.Services.Items.Add(new TorService
            {
                Id = Guid.NewGuid(),
                Name = "TorNew",
                Configuration = Guid.NewGuid()
            });
            return Task.FromResult(new SeekResult(false, string.Empty));
        };
        var creator = fixture.CreateCreator();

        var result = await creator.CreateAsync(request);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.ServiceRegistered);
        Assert.AreEqual(@"C:\Tor\NewData\torrc", result.ConfigurationPath);
        Assert.AreEqual(@"C:\Tor\NewData", result.DataDirectory);
        StringAssert.Contains(fixture.FileSystem.Files[result.ConfigurationPath!], "DataDirectory C:\\Tor\\NewData");
        StringAssert.Contains(fixture.FileSystem.Files[result.ConfigurationPath!], "SocksPort 127.0.0.1:9060");
        Assert.IsTrue(fixture.FileSystem.Directories.Contains(@"C:\Tor\NewData"));
        CollectionAssert.AreEqual(new[] { "verify", "register", "discover" }, order);
        Assert.AreEqual("TorNew", fixture.Registrar.ServiceName);
        Assert.AreEqual("Manual", fixture.Registrar.StartingType);
        Assert.AreEqual(@"C:\Tor\NewData\torrc", fixture.Services.Items.Single().ApplicationCreatedTorrcPath);
        Assert.AreEqual(@"C:\Tor\NewData", fixture.Services.Items.Single().ApplicationCreatedDataDirectory);
    }

    [TestMethod]
    public async Task CreateAsyncRejectsInvalidRequiredFieldsAndPaths()
    {
        var fixture = new Fixture();
        var creator = fixture.CreateCreator();
        var invalidRequests = new[]
        {
            CreateRequest() with { ServiceName = "bad/name" },
            CreateRequest() with { ServiceName = new string('a', 257) },
            CreateRequest() with { DisplayName = " " },
            CreateRequest() with { TorExecutablePath = @"C:\Missing\tor.exe" },
            CreateRequest() with { TorExecutablePath = @"C:\Tor\not-tor.txt" },
            CreateRequest() with { TorExecutablePath = @"C:\Tor\not-tor.exe" },
            CreateRequest() with { DataDirectory = "relative-data" },
            CreateRequest() with { StartingType = "Unknown" },
            CreateRequest() with { Configuration = null! }
        };

        foreach (var request in invalidRequests)
        {
            var result = await creator.CreateAsync(request);
            Assert.IsFalse(result.Success, request.ServiceName);
            Assert.IsFalse(result.ServiceRegistered, request.ServiceName);
        }

        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncHonorsCancellationBeforeFileOperations()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            fixture.CreateCreator().CreateAsync(CreateRequest(), cancellation.Token));

        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncRejectsServiceNameAlreadyInWindowsOrJson()
    {
        var fixture = new Fixture();
        var creator = fixture.CreateCreator();
        fixture.Catalog.Services.Add(new WindowsServiceInfo(
            "TORNEW", "Existing", "Stopped", "Manual", "LocalSystem", string.Empty));

        var windowsConflict = await creator.CreateAsync(CreateRequest());
        Assert.IsFalse(windowsConflict.Success);

        fixture.Catalog.Services.Clear();
        fixture.Services.Items.Add(new TorService { Name = "tornew" });
        var jsonConflict = await creator.CreateAsync(CreateRequest());

        Assert.IsFalse(jsonConflict.Success);
        Assert.IsFalse(jsonConflict.ServiceRegistered);
        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncRejectsDataDirectoryAlreadyInUseWithoutChangingIt()
    {
        var fixture = new Fixture();
        fixture.FileSystem.Directories.Add(@"C:\Tor\NewData");

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncRejectsPathsReferencedBySavedServices()
    {
        var fixture = new Fixture();
        fixture.Services.Items.Add(new TorService
        {
            Name = "TorOld",
            ConfigurationFilePath = @"C:\Tor\NewData\old-torrc"
        });

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncRejectsDataDirectoryReferencedBySavedConfiguration()
    {
        var fixture = new Fixture();
        fixture.Configurations.Items.Add(new TorConfiguration { DataDirectory = @"C:\Tor\NewData\OldInstance" });

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncDoesNotRegisterWhenConfigurationVerificationFails()
    {
        var fixture = new Fixture
        {
            Verifier = new TorConfigurationVerifier((_, _, _) => Task.FromResult(1))
        };

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        Assert.AreEqual(@"C:\Tor\NewData\torrc", result.ConfigurationPath);
        Assert.IsTrue(fixture.FileSystem.FileExists(result.ConfigurationPath!));
        Assert.AreEqual(0, fixture.Registrar.CallCount);
        Assert.AreEqual(0, fixture.Seeker.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncRetainsPreparedFilesAndReportsPathsWhenRegistrationFails()
    {
        var fixture = new Fixture();
        fixture.Registrar.Register = (_, _, _, _, _, _) =>
            Task.FromException(new UnauthorizedAccessException("UAC cancelled"));

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        Assert.IsTrue(result.Message.Contains(@"C:\Tor\NewData", StringComparison.Ordinal));
        Assert.IsTrue(fixture.FileSystem.FileExists(@"C:\Tor\NewData\torrc"));
        Assert.AreEqual(0, fixture.Seeker.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncReportsUacCancellationWithoutExposingNativeExceptionDetails()
    {
        var fixture = new Fixture();
        fixture.Registrar.Register = (_, _, _, _, _, _) =>
            Task.FromException(new Win32Exception(1223));

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        StringAssert.Contains(result.Message, "Administrator approval was cancelled");
        Assert.IsTrue(fixture.FileSystem.FileExists(@"C:\Tor\NewData\torrc"));
        Assert.AreEqual(0, fixture.Seeker.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncReportsRegisteredServiceWhenDiscoveryFails()
    {
        var fixture = new Fixture();
        fixture.Seeker.ExecuteAction = _ => Task.FromResult(new SeekResult(true, "JSON write failed"));

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ServiceRegistered);
        StringAssert.Contains(result.Message, "TorNew");
        StringAssert.Contains(result.Message, "JSON write failed");
        Assert.AreEqual(1, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncReportsFilePreparationErrorsWithoutRegistering()
    {
        var fixture = new Fixture();
        fixture.FileSystem.WriteFailure = new IOException("disk full");

        var result = await fixture.CreateCreator().CreateAsync(CreateRequest());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        StringAssert.Contains(result.Message, "disk full");
        Assert.AreEqual(0, fixture.Registrar.CallCount);
    }

    [TestMethod]
    public async Task CreateAsyncDoesNotCopyUnmanagedSecretDirectivesIntoNewTorrc()
    {
        var fixture = new Fixture();
        fixture.Seeker.ExecuteAction = _ =>
        {
            fixture.Services.Items.Add(new TorService { Name = "TorNew" });
            return Task.FromResult(new SeekResult(false, string.Empty));
        };
        var request = CreateRequest() with
        {
            Configuration = new TorConfiguration
            {
                SocksPorts = [9060],
                SocksPortEndpoints = ["9060"],
                AdditionalDirectives = new() { ["HashedControlPassword"] = ["private-hash"] }
            }
        };

        var result = await fixture.CreateCreator().CreateAsync(request);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(fixture.FileSystem.Files[result.ConfigurationPath!].Contains("private-hash", StringComparison.Ordinal));
        Assert.IsFalse(result.Message.Contains("private-hash", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CreateAsyncRejectsSecretLikeValuesWithoutEchoingThem()
    {
        var fixture = new Fixture();
        var request = CreateRequest() with
        {
            Configuration = new TorConfiguration
            {
                Bridges = ["BridgePassword private-password-value"]
            }
        };

        var result = await fixture.CreateCreator().CreateAsync(request);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.ServiceRegistered);
        Assert.IsFalse(result.Message.Contains("private-password-value", StringComparison.Ordinal));
        Assert.AreEqual(0, fixture.FileSystem.WriteCount);
    }

    [TestMethod]
    public void ConstructorRejectsMissingDependencies()
    {
        var fixture = new Fixture();
        var creator = fixture.CreateCreator();

        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(null!, fixture.Catalog, fixture.Services, fixture.Configurations, fixture.Verifier,
                new TorConfigDocumentEditor(), fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, null!, fixture.Services, fixture.Configurations, fixture.Verifier,
                new TorConfigDocumentEditor(), fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, null!, fixture.Configurations, fixture.Verifier,
                new TorConfigDocumentEditor(), fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, fixture.Services, null!, fixture.Verifier,
                new TorConfigDocumentEditor(), fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, fixture.Services, fixture.Configurations, null!,
                new TorConfigDocumentEditor(), fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, fixture.Services, fixture.Configurations, fixture.Verifier,
                null!, fixture.Registrar, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, fixture.Services, fixture.Configurations, fixture.Verifier,
                new TorConfigDocumentEditor(), null!, fixture.Seeker));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TorServiceCreator(fixture.FileSystem, fixture.Catalog, fixture.Services, fixture.Configurations, fixture.Verifier,
                new TorConfigDocumentEditor(), fixture.Registrar, null!));
    }

    private static TorServiceCreationRequest CreateRequest() =>
        new(
            "TorNew",
            "New Tor Service",
            @"C:\Tor\tor.exe",
            @"C:\Tor\NewData",
            "Manual",
            new TorConfiguration
            {
                SocksPorts = [9060],
                SocksPortEndpoints = ["127.0.0.1:9060"],
                UseBridges = true,
                Bridges = ["obfs4 192.0.2.1:443 FINGERPRINT"],
                ClientTransportPlugins = [@"obfs4 exec C:\Tor\lyrebird.exe"],
                Logs = [@"notice file C:\Tor\NewData\notices.log"],
                ControlPorts = [9061],
                ControlPortEndpoints = ["127.0.0.1:9061"]
            });

    private sealed class Fixture
    {
        public FakeFileSystem FileSystem { get; } = new();
        public FakeCatalog Catalog { get; } = new();
        public MemoryStore<TorService> Services { get; } = new();
        public MemoryStore<TorConfiguration> Configurations { get; } = new();
        public FakeRegistrar Registrar { get; } = new();
        public FakeSeeker Seeker { get; } = new();
        public TorConfigurationVerifier Verifier { get; set; } = new((_, _, _) => Task.FromResult(0));

        public Fixture() => FileSystem.Files.Add(@"C:\Tor\tor.exe", "mock executable");

        public TorServiceCreator CreateCreator() =>
            new(
                FileSystem,
                Catalog,
                Services,
                Configurations,
                Verifier,
                new TorConfigDocumentEditor(),
                Registrar,
                Seeker);
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int WriteCount { get; private set; }
        public IOException? WriteFailure { get; set; }
        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public void CreateDirectory(string path) => Directories.Add(path);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files[path]);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            if (WriteFailure is not null)
            {
                return Task.FromException(WriteFailure);
            }

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

    private sealed class FakeCatalog : IWindowsServiceCatalog
    {
        public List<WindowsServiceInfo> Services { get; } = [];
        public Task<IReadOnlyList<WindowsServiceInfo>> FindTorServicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindowsServiceInfo>>(Services.ToArray());
        public Task<bool> ServiceNameExistsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Services.Any(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)));
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

    private sealed class FakeRegistrar : IWindowsServiceRegistrar
    {
        public int CallCount { get; private set; }
        public string? ServiceName { get; private set; }
        public string? StartingType { get; private set; }
        public Func<string, string, string, string, string, CancellationToken, Task> Register { get; set; } =
            (_, _, _, _, _, _) => Task.CompletedTask;
        public Task RegisterAsync(
            string serviceName,
            string displayName,
            string torExecutablePath,
            string configurationPath,
            string startingType,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ServiceName = serviceName;
            StartingType = startingType;
            return Register(serviceName, displayName, torExecutablePath, configurationPath, startingType, cancellationToken);
        }
    }

    private sealed class FakeSeeker : ITorServiceSeeker
    {
        public int CallCount { get; private set; }
        public Func<CancellationToken, Task<SeekResult>> ExecuteAction { get; set; } =
            _ => Task.FromResult(new SeekResult(false, string.Empty));
        public Task<SeekResult> Execute(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ExecuteAction(cancellationToken);
        }
    }
}
