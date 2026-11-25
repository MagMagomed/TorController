namespace TorController.App.Models;

public sealed class ServiceGridRow
{
    public ServiceGridRow(
        TorService service,
        string stageName,
        string startingTypeName,
        TorConfiguration configuration)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        StageName = stageName ?? throw new ArgumentNullException(nameof(stageName));
        StartingTypeName = startingTypeName ?? throw new ArgumentNullException(nameof(startingTypeName));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        SocksPorts = string.Join(", ",
            configuration.SocksPortEndpoints.Count > 0
                ? configuration.SocksPortEndpoints
                : configuration.SocksPorts.Select(port => port.ToString()));
        Bridges = string.Join(Environment.NewLine, configuration.Bridges);
    }

    public TorService Service { get; }
    public TorConfiguration Configuration { get; }
    public string Name => Service.Name;
    public string DisplayName => Service.DisplayName;
    public string StageName { get; }
    public string StartingTypeName { get; }
    public string SocksPorts { get; }
    public string Bridges { get; }
    public string TorExecutablePath => Service.TorExecutablePath;
    public string ConfigurationFilePath => Service.ConfigurationFilePath;
    public string ServiceAccount => Service.ServiceAccount;
}
