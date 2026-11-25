namespace TorController.App.Models;

public sealed class TorService
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid Stage { get; set; }
    public Guid StartingType { get; set; }
    public Guid Configuration { get; set; }
    public string TorExecutablePath { get; set; } = string.Empty;
    public string ConfigurationFilePath { get; set; } = string.Empty;
    public string ServiceAccount { get; set; } = string.Empty;
    public string? ApplicationCreatedTorrcPath { get; set; }
    public string? ApplicationCreatedDataDirectory { get; set; }
    public bool WindowsServiceRemoved { get; set; }
}
