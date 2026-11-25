using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ServiceStageTests
{
    [TestMethod]
    public void SerializationPreservesStageIdAndName()
    {
        var stage = new ServiceStage { Id = Guid.NewGuid(), Name = "Running" };
        var restored = JsonSerializer.Deserialize<ServiceStage>(JsonSerializer.Serialize(stage));

        Assert.IsNotNull(restored);
        Assert.AreEqual(stage.Id, restored.Id);
        Assert.AreEqual(stage.Name, restored.Name);
    }
}
