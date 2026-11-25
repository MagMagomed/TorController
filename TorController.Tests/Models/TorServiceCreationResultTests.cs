using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class TorServiceCreationResultTests
{
    [TestMethod]
    public void ResultRetainsSuccessRegistrationAndPathInformation()
    {
        var result = new TorServiceCreationResult(
            false,
            true,
            "Service registered, but discovery failed.",
            @"C:\Tor\NewData\torrc",
            @"C:\Tor\NewData");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ServiceRegistered);
        Assert.AreEqual("Service registered, but discovery failed.", result.Message);
        Assert.AreEqual(@"C:\Tor\NewData\torrc", result.ConfigurationPath);
        Assert.AreEqual(@"C:\Tor\NewData", result.DataDirectory);
    }
}
