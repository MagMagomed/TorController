using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ServiceOperationResultTests
{
    [TestMethod]
    public void ResultPreservesOperationOutcomeAndMessage()
    {
        var result = new ServiceOperationResult(false, "access denied");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("access denied", result.Message);
    }
}
