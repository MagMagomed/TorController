using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ConfigurationSaveResultTests
{
    [TestMethod]
    public void ResultPreservesOutcomeMessageAndBackupPath()
    {
        var result = new ConfigurationSaveResult(true, "verified", @"C:\Tor\torrc.bak");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("verified", result.Message);
        Assert.AreEqual(@"C:\Tor\torrc.bak", result.BackupPath);
    }
}
