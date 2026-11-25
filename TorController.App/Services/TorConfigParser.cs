using TorController.App.Models;
using System.Globalization;
using System.IO;

namespace TorController.App.Services;

public sealed class TorConfigParser
{
    private static readonly HashSet<string> SecretDirectiveFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passphrase",
        "secret",
        "privatekey",
        "cookie",
        "authenticator",
        "credential",
        "onionauth"
    };

    public TorConfiguration Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var configuration = new TorConfiguration();
        using var reader = new StringReader(content);
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var directiveLine = RemoveComment(line).Trim();
            if (directiveLine.Length == 0)
            {
                continue;
            }

            var separator = directiveLine.IndexOfAny([' ', '\t']);
            if (separator <= 0)
            {
                AddAdditional(configuration, directiveLine, string.Empty);
                continue;
            }

            var directive = directiveLine[..separator];
            var value = directiveLine[(separator + 1)..].Trim();
            if (IsSecretDirective(directive))
            {
                continue;
            }

            try
            {
                switch (directive.ToLowerInvariant())
                {
                    case "socksport":
                        var socksEndpoint = ParsePortEndpoint(value);
                        configuration.SocksPortEndpoints.Add(socksEndpoint.Endpoint);
                        if (socksEndpoint.Port.HasValue)
                        {
                            configuration.SocksPorts.Add(socksEndpoint.Port.Value);
                        }

                        break;
                    case "usebridges":
                        configuration.UseBridges = ParseBoolean(value);
                        break;
                    case "clienttransportplugin":
                        configuration.ClientTransportPlugins.Add(value);
                        break;
                    case "bridge":
                        configuration.Bridges.Add(value);
                        break;
                    case "log":
                        configuration.Logs.Add(value);
                        break;
                    case "controlport":
                        var controlEndpoint = ParsePortEndpoint(value);
                        configuration.ControlPortEndpoints.Add(controlEndpoint.Endpoint);
                        if (controlEndpoint.Port.HasValue)
                        {
                            configuration.ControlPorts.Add(controlEndpoint.Port.Value);
                        }

                        break;
                    case "datadirectory":
                        configuration.DataDirectory = Unquote(value);
                        break;
                    default:
                        AddAdditional(configuration, directive, value);
                        break;
                }
            }
            catch (FormatException)
            {
                throw new FormatException($"Invalid {directive} directive on line {lineNumber}; check its format.");
            }
        }

        return configuration;
    }

    private static (string Endpoint, int? Port) ParsePortEndpoint(string value)
    {
        var firstArgument = GetFirstArgument(value);
        if (firstArgument.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            firstArgument.StartsWith("unix:", StringComparison.OrdinalIgnoreCase))
        {
            return (value.Trim(), null);
        }

        var portText = firstArgument.Contains(':')
            ? firstArgument[(firstArgument.LastIndexOf(':') + 1)..]
            : firstArgument;

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new FormatException($"'{firstArgument}' is not a valid TCP port.");
        }

        return (value.Trim(), port);
    }

    private static bool ParseBoolean(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => throw new FormatException($"'{value}' is not a valid boolean value.")
        };
    }

    private static string GetFirstArgument(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("A value is required.");
        }

        if (trimmed[0] != '"')
        {
            var separator = trimmed.IndexOfAny([' ', '\t']);
            return separator < 0 ? trimmed : trimmed[..separator];
        }

        var endQuote = trimmed.IndexOf('"', 1);
        if (endQuote < 0)
        {
            throw new FormatException("The quoted value is not closed.");
        }

        return trimmed[1..endQuote];
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static bool IsSecretDirective(string directive)
    {
        return SecretDirectiveFragments.Any(fragment => directive.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddAdditional(TorConfiguration configuration, string directive, string value)
    {
        if (!configuration.AdditionalDirectives.TryGetValue(directive, out var values))
        {
            values = [];
            configuration.AdditionalDirectives.Add(directive, values);
        }

        values.Add(value);
    }

    private static string RemoveComment(string line)
    {
        var inQuotes = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == '#' && !inQuotes && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index];
            }
        }

        return line;
    }
}
