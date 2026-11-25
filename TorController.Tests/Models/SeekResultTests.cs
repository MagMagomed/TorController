using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class SeekResultTests
{
    [TestMethod]
    public void SerializationPreservesErrorAndMessage()
    {
        var result = new SeekResult(true, "Partial discovery");
        var restored = JsonSerializer.Deserialize<SeekResult>(JsonSerializer.Serialize(result));

        Assert.IsNotNull(restored);
        Assert.IsTrue(restored.HasError);
        Assert.AreEqual("Partial discovery", restored.Message);
    }
}
