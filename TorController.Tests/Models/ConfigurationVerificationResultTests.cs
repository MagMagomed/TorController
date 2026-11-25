using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ConfigurationVerificationResultTests
{
    [TestMethod]
    public void ResultPreservesValidationOutcomeAndMessage()
    {
        var result = new ConfigurationVerificationResult(false, "verification failed");

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("verification failed", result.Message);
    }
}
