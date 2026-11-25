using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class TorServiceTests
{
    [TestMethod]
    public void SerializationPreservesAllServiceFields()
    {
        var service = new TorService
        {
            Id = Guid.NewGuid(),
            Name = "tor",
            DisplayName = "Tor Win32 Service",
            Stage = Guid.NewGuid(),
            StartingType = Guid.NewGuid(),
            Configuration = Guid.NewGuid(),
            TorExecutablePath = @"C:\Tor\tor.exe",
            ConfigurationFilePath = @"C:\Tor\torrc",
            ServiceAccount = @"NT AUTHORITY\LocalService",
            ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc",
            ApplicationCreatedDataDirectory = @"C:\Tor\Data",
            WindowsServiceRemoved = true
        };

        var restored = JsonSerializer.Deserialize<TorService>(JsonSerializer.Serialize(service));

        Assert.IsNotNull(restored);
        Assert.AreEqual(service.Id, restored.Id);
        Assert.AreEqual(service.Name, restored.Name);
        Assert.AreEqual(service.DisplayName, restored.DisplayName);
        Assert.AreEqual(service.Stage, restored.Stage);
        Assert.AreEqual(service.StartingType, restored.StartingType);
        Assert.AreEqual(service.Configuration, restored.Configuration);
        Assert.AreEqual(service.TorExecutablePath, restored.TorExecutablePath);
        Assert.AreEqual(service.ConfigurationFilePath, restored.ConfigurationFilePath);
        Assert.AreEqual(service.ServiceAccount, restored.ServiceAccount);
        Assert.AreEqual(service.ApplicationCreatedTorrcPath, restored.ApplicationCreatedTorrcPath);
        Assert.AreEqual(service.ApplicationCreatedDataDirectory, restored.ApplicationCreatedDataDirectory);
        Assert.IsTrue(restored.WindowsServiceRemoved);
    }

    [TestMethod]
    public void OlderServiceJsonDefaultsToUnownedFilesAndRegisteredWindowsService()
    {
        var restored = JsonSerializer.Deserialize<TorService>(
            """{"Id":"00000000-0000-0000-0000-000000000001","Name":"tor"}""");

        Assert.IsNotNull(restored);
        Assert.IsNull(restored.ApplicationCreatedTorrcPath);
        Assert.IsNull(restored.ApplicationCreatedDataDirectory);
        Assert.IsFalse(restored.WindowsServiceRemoved);
    }
}
