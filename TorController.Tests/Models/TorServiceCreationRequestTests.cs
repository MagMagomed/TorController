using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class TorServiceCreationRequestTests
{
    [TestMethod]
    public void RequestRetainsAllProvidedValues()
    {
        var configuration = new TorConfiguration { SocksPorts = [9050] };
        var request = new TorServiceCreationRequest(
            "TorNew",
            "New Tor Service",
            @"C:\Tor\tor.exe",
            @"C:\Tor\NewData",
            "Manual",
            configuration);

        Assert.AreEqual("TorNew", request.ServiceName);
        Assert.AreEqual("New Tor Service", request.DisplayName);
        Assert.AreEqual(@"C:\Tor\tor.exe", request.TorExecutablePath);
        Assert.AreEqual(@"C:\Tor\NewData", request.DataDirectory);
        Assert.AreEqual("Manual", request.StartingType);
        Assert.AreSame(configuration, request.Configuration);
    }
}
