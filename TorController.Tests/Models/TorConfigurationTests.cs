using System.Text.Json;
using TorController.App.Models;

namespace TorController.Tests.Models;

[TestClass]
public sealed class TorConfigurationTests
{
    [TestMethod]
    public void SerializationPreservesAllNonSecretConfiguration()
    {
        var configuration = new TorConfiguration
        {
            Id = Guid.NewGuid(),
            SocksPorts = [9050, 9052],
            SocksPortEndpoints = ["127.0.0.1:9050", "127.0.0.1:9052"],
            UseBridges = true,
            ClientTransportPlugins = ["obfs4 exec lyrebird.exe"],
            Bridges = ["obfs4 192.0.2.1:443 FINGERPRINT cert=public"],
            Logs = ["notice file notices.log"],
            ControlPorts = [9054],
            ControlPortEndpoints = ["127.0.0.1:9054"],
            DataDirectory = @"C:\Tor\Data",
            AdditionalDirectives = new() { ["DisableNetwork"] = ["0"] }
        };

        var restored = JsonSerializer.Deserialize<TorConfiguration>(JsonSerializer.Serialize(configuration));

        Assert.IsNotNull(restored);
        Assert.AreEqual(configuration.Id, restored.Id);
        CollectionAssert.AreEqual(configuration.SocksPorts, restored.SocksPorts);
        CollectionAssert.AreEqual(configuration.SocksPortEndpoints, restored.SocksPortEndpoints);
        Assert.AreEqual(configuration.UseBridges, restored.UseBridges);
        CollectionAssert.AreEqual(configuration.ClientTransportPlugins, restored.ClientTransportPlugins);
        CollectionAssert.AreEqual(configuration.Bridges, restored.Bridges);
        CollectionAssert.AreEqual(configuration.Logs, restored.Logs);
        CollectionAssert.AreEqual(configuration.ControlPorts, restored.ControlPorts);
        CollectionAssert.AreEqual(configuration.ControlPortEndpoints, restored.ControlPortEndpoints);
        Assert.AreEqual(configuration.DataDirectory, restored.DataDirectory);
        CollectionAssert.AreEqual(configuration.AdditionalDirectives["DisableNetwork"], restored.AdditionalDirectives["DisableNetwork"]);
    }
}
