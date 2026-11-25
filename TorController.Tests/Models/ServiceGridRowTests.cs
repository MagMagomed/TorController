using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ServiceGridRowTests
{
    [TestMethod]
    public void ConstructorBuildsReadableServiceSummary()
    {
        var service = new TorService
        {
            Name = "tor",
            DisplayName = "Tor Win32 Service",
            TorExecutablePath = @"C:\Tor\tor.exe",
            ConfigurationFilePath = @"C:\Tor\torrc",
            ServiceAccount = "LocalService"
        };
        var row = new ServiceGridRow(
            service,
            "Running",
            "Automatic",
            new TorConfiguration { SocksPorts = [9050, 9052], Bridges = ["obfs4 bridge-a", "obfs4 bridge-b"] });

        Assert.AreEqual("tor", row.Name);
        Assert.AreEqual("Tor Win32 Service", row.DisplayName);
        Assert.AreEqual("Running", row.StageName);
        Assert.AreEqual("Automatic", row.StartingTypeName);
        Assert.AreEqual("9050, 9052", row.SocksPorts);
        Assert.AreEqual("obfs4 bridge-a" + Environment.NewLine + "obfs4 bridge-b", row.Bridges);
        Assert.AreEqual(@"C:\Tor\tor.exe", row.TorExecutablePath);
        Assert.AreEqual(@"C:\Tor\torrc", row.ConfigurationFilePath);
        Assert.AreEqual("LocalService", row.ServiceAccount);
        Assert.AreSame(service, row.Service);
    }

    [TestMethod]
    public void ConstructorRejectsMissingRelatedData()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new ServiceGridRow(null!, "Running", "Automatic", new TorConfiguration()));
    }
}
