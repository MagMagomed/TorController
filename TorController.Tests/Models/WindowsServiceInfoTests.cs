using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class WindowsServiceInfoTests
{
    [TestMethod]
    public void SerializationPreservesServiceMetadata()
    {
        var info = new WindowsServiceInfo("tor", "Tor", "Running", "Automatic", "LocalSystem", "tor.exe -f torrc");
        var restored = JsonSerializer.Deserialize<WindowsServiceInfo>(JsonSerializer.Serialize(info));

        Assert.IsNotNull(restored);
        Assert.AreEqual(info, restored);
    }
}
