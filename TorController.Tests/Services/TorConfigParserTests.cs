using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorConfigParserTests
{
    private readonly TorConfigParser _parser = new();

    [TestMethod]
    public void ParseReadsMultiplePortsBridgesAndLogs()
    {
        var result = _parser.Parse(
            """
            SocksPort 9050
            SocksPort 127.0.0.1:9052
            UseBridges 1
            Bridge obfs4 192.0.2.10:443 FINGERPRINT cert=bridge-cert iat-mode=0
            Bridge obfs4 192.0.2.11:443 OTHERFINGERPRINT cert=other-cert iat-mode=0
            Log notice file C:\Tor\notices.log
            ControlPort 9054
            DataDirectory C:\Tor\Data
            """);

        CollectionAssert.AreEqual(new[] { 9050, 9052 }, result.SocksPorts);
        Assert.IsTrue(result.UseBridges);
        Assert.AreEqual(2, result.Bridges.Count);
        CollectionAssert.AreEqual(new[] { "notice file C:\\Tor\\notices.log" }, result.Logs);
        CollectionAssert.AreEqual(new[] { 9054 }, result.ControlPorts);
        Assert.AreEqual("C:\\Tor\\Data", result.DataDirectory);
    }

    [TestMethod]
    public void ParsePreservesSupportedNonSecretAdditionalDirectives()
    {
        var result = _parser.Parse("DisableNetwork 0\nNickname LocalRelay");

        CollectionAssert.AreEqual(new[] { "0" }, result.AdditionalDirectives["DisableNetwork"]);
        CollectionAssert.AreEqual(new[] { "LocalRelay" }, result.AdditionalDirectives["Nickname"]);
    }

    [TestMethod]
    public void ParseDoesNotRetainSecretDirectives()
    {
        var result = _parser.Parse(
            "HashedControlPassword 16:secret-hash\nBridgePassword bridge-secret\nSocks5ProxyPassword proxy-secret\nCookieAuthentication 1");

        Assert.IsFalse(result.AdditionalDirectives.ContainsKey("HashedControlPassword"));
        Assert.IsFalse(result.AdditionalDirectives.ContainsKey("BridgePassword"));
        Assert.IsFalse(result.AdditionalDirectives.ContainsKey("Socks5ProxyPassword"));
        Assert.IsFalse(result.AdditionalDirectives.ContainsKey("CookieAuthentication"));
        Assert.IsFalse(result.AdditionalDirectives.Values.SelectMany(values => values)
            .Any(value => value.Contains("secret", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ParseIgnoresCommentsAndBlankLines()
    {
        var result = _parser.Parse("# comment\n\n  # indented comment\nSocksPort 9050 # inline comment");

        CollectionAssert.AreEqual(new[] { 9050 }, result.SocksPorts);
        Assert.AreEqual(0, result.AdditionalDirectives.Count);
    }

    [TestMethod]
    public void ParseRejectsInvalidPort()
    {
        Assert.ThrowsException<FormatException>(() => _parser.Parse("SocksPort not-a-port"));
    }

    [TestMethod]
    public void ParseDoesNotEchoInvalidDirectiveValueInErrorMessage()
    {
        var exception = Assert.ThrowsException<FormatException>(() =>
            _parser.Parse("SocksPort private-password-value"));

        Assert.IsFalse(exception.Message.Contains("private-password-value", StringComparison.Ordinal));
        StringAssert.Contains(exception.Message, "SocksPort");
    }

    [TestMethod]
    public void ParsePreservesPortBindingAddressesAsWellAsNumericPorts()
    {
        var result = _parser.Parse("SocksPort 127.0.0.1:9050\nControlPort 127.0.0.1:9054");

        CollectionAssert.AreEqual(new[] { "127.0.0.1:9050" }, result.SocksPortEndpoints);
        CollectionAssert.AreEqual(new[] { 9050 }, result.SocksPorts);
        CollectionAssert.AreEqual(new[] { "127.0.0.1:9054" }, result.ControlPortEndpoints);
        CollectionAssert.AreEqual(new[] { 9054 }, result.ControlPorts);
    }

    [TestMethod]
    public void ParsePreservesPortOptionsAndNonTcpEndpoints()
    {
        var result = _parser.Parse(
            "SocksPort 127.0.0.1:9050 IsolateSOCKSAuth\nSocksPort unix:/tmp/tor-socks");

        CollectionAssert.AreEqual(
            new[] { "127.0.0.1:9050 IsolateSOCKSAuth", "unix:/tmp/tor-socks" },
            result.SocksPortEndpoints);
        CollectionAssert.AreEqual(new[] { 9050 }, result.SocksPorts);
    }
}
