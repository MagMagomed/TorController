namespace TorController.App.Models;

public sealed class TorConfiguration
{
    public Guid Id { get; set; }
    public List<int> SocksPorts { get; set; } = [];
    public List<string> SocksPortEndpoints { get; set; } = [];
    public bool UseBridges { get; set; }
    public List<string> ClientTransportPlugins { get; set; } = [];
    public List<string> Bridges { get; set; } = [];
    public List<string> Logs { get; set; } = [];
    public List<int> ControlPorts { get; set; } = [];
    public List<string> ControlPortEndpoints { get; set; } = [];
    public string? DataDirectory { get; set; }
    public Dictionary<string, List<string>> AdditionalDirectives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
