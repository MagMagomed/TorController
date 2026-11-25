using TorController.App.Models;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorConfigDocumentEditorTests
{
    [TestMethod]
    public void ApplyReplacesManagedValuesAndPreservesUnmanagedDirectives()
    {
        var editor = new TorConfigDocumentEditor();
        var original = """
            # user comment
            SocksPort 9050
            SocksPort 9051
            UseBridges 0
            Bridge old-bridge
            HashedControlPassword do-not-show
            DisableNetwork 0
            """;

        var result = editor.Apply(original, new TorConfiguration
        {
            SocksPorts = [9150],
            UseBridges = true,
            Bridges = ["obfs4 192.0.2.2:443 NEWFINGERPRINT cert=public"],
            Logs = ["notice file new.log"],
            AdditionalDirectives = new() { ["DisableNetwork"] = ["0"] }
        });

        Assert.IsTrue(result.Contains("# user comment", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("HashedControlPassword do-not-show", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("DisableNetwork 0", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("SocksPort 9150", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("SocksPort 9050", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("SocksPort 9051", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("UseBridges 1", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("Bridge obfs4 192.0.2.2:443 NEWFINGERPRINT cert=public", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("Log notice file new.log", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyAddsManagedDirectivesThatWereNotPresent()
    {
        var result = new TorConfigDocumentEditor().Apply(string.Empty, new TorConfiguration
        {
            SocksPorts = [9050],
            UseBridges = false,
            ControlPorts = [9051],
            DataDirectory = @"C:\Tor\Data"
        });

        Assert.IsTrue(result.Contains("SocksPort 9050", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("UseBridges 0", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("ControlPort 9051", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("DataDirectory C:\\Tor\\Data", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyRemovesManagedDirectiveWhenEditedValueIsEmpty()
    {
        var result = new TorConfigDocumentEditor().Apply(
            "Bridge old\nDataDirectory C:\\Old\nDisableNetwork 0",
            new TorConfiguration { UseBridges = false });

        Assert.IsFalse(result.Contains("Bridge ", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("DataDirectory ", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("DisableNetwork 0", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyPreservesExplicitLoopbackBindings()
    {
        var result = new TorConfigDocumentEditor().Apply(
            "SocksPort 127.0.0.1:9050\nControlPort 127.0.0.1:9054",
            new TorConfiguration
            {
                SocksPorts = [9050],
                SocksPortEndpoints = ["127.0.0.1:9050"],
                ControlPorts = [9054],
                ControlPortEndpoints = ["127.0.0.1:9054"]
            });

        Assert.IsTrue(result.Contains("SocksPort 127.0.0.1:9050", StringComparison.Ordinal));
        Assert.IsTrue(result.Contains("ControlPort 127.0.0.1:9054", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("SocksPort 9050", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("ControlPort 9054", StringComparison.Ordinal));
    }
}
