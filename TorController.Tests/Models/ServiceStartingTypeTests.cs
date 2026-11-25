using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class ServiceStartingTypeTests
{
    [TestMethod]
    public void SerializationPreservesStartingTypeIdAndName()
    {
        var startingType = new ServiceStartingType { Id = Guid.NewGuid(), Name = "Automatic" };
        var restored = JsonSerializer.Deserialize<ServiceStartingType>(JsonSerializer.Serialize(startingType));

        Assert.IsNotNull(restored);
        Assert.AreEqual(startingType.Id, restored.Id);
        Assert.AreEqual(startingType.Name, restored.Name);
    }
}
