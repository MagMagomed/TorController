using TorController.App.Models;
using System.IO;

namespace TorController.App.Services;

public sealed class TorConfigDocumentEditor
{
    private static readonly string[] ManagedDirectiveOrder =
    [
        "SocksPort",
        "UseBridges",
        "ClientTransportPlugin",
        "Bridge",
        "Log",
        "ControlPort",
        "DataDirectory"
    ];

    public string Apply(string originalContent, TorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(originalContent);
        ArgumentNullException.ThrowIfNull(configuration);

        var replacements = CreateReplacements(configuration);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        using var reader = new StringReader(originalContent);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var directive = GetDirective(line);
            if (directive is null || !replacements.ContainsKey(directive))
            {
                output.Add(line);
                continue;
            }

            if (emitted.Add(directive))
            {
                output.AddRange(replacements[directive]);
            }
        }

        foreach (var directive in ManagedDirectiveOrder)
        {
            if (emitted.Add(directive))
            {
                output.AddRange(replacements[directive]);
            }
        }

        return string.Join(Environment.NewLine, output);
    }

    private static Dictionary<string, IReadOnlyList<string>> CreateReplacements(TorConfiguration configuration) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SocksPort"] = configuration.SocksPortEndpoints.Count > 0
                ? configuration.SocksPortEndpoints.Select(endpoint => $"SocksPort {endpoint}").ToArray()
                : configuration.SocksPorts.Select(port => $"SocksPort {port}").ToArray(),
            ["UseBridges"] = [$"UseBridges {(configuration.UseBridges ? "1" : "0")}"],
            ["ClientTransportPlugin"] = configuration.ClientTransportPlugins
                .Select(value => $"ClientTransportPlugin {value}").ToArray(),
            ["Bridge"] = configuration.Bridges.Select(value => $"Bridge {value}").ToArray(),
            ["Log"] = configuration.Logs.Select(value => $"Log {value}").ToArray(),
            ["ControlPort"] = configuration.ControlPortEndpoints.Count > 0
                ? configuration.ControlPortEndpoints.Select(endpoint => $"ControlPort {endpoint}").ToArray()
                : configuration.ControlPorts.Select(port => $"ControlPort {port}").ToArray(),
            ["DataDirectory"] = string.IsNullOrWhiteSpace(configuration.DataDirectory)
                ? []
                : [$"DataDirectory {QuoteIfNeeded(configuration.DataDirectory)}"]
        };

    private static string? GetDirective(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return null;
        }

        var separator = trimmed.IndexOfAny([' ', '\t']);
        return separator < 0 ? trimmed : trimmed[..separator];
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) && !(value.StartsWith('"') && value.EndsWith('"'))
            ? $"\"{value}\""
            : value;
}
