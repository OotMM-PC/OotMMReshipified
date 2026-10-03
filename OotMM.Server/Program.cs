using OotMM.Core;
using OotMM.Core.Relay;

// OoTMM dedicated multiworld server: console host for the relay engine. Config
// from server.properties next to the exe; /help lists admin commands.

var exeDir = AppContext.BaseDirectory;
var propsPath = Path.Combine(exeDir, "server.properties");
var config = ServerProperties.Load(propsPath);
var storeDir = Path.Combine(exeDir, "world_state");

if (args.Contains("--register-manifest"))
{
    var seeds = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--register-manifest")
        {
            continue;
        }

        if (args[i] == "--world-state")
        {
            if (++i >= args.Length)
            {
                Console.Error.WriteLine("ootmm_server: --world-state requires a directory");
                return 2;
            }

            storeDir = Path.GetFullPath(args[i]);
        }
        else if (args[i].StartsWith('-'))
        {
            Console.Error.WriteLine($"ootmm_server: invalid offline option '{args[i]}'");
            return 2;
        }
        else
        {
            seeds.Add(args[i]);
        }
    }

    var report = new RelayEngine(storeDir, config.Rules).ProvisionRoomManifests(seeds);
    Console.WriteLine($"[provision] {report.Message}");
    return report.Success ? 0 : 1;
}

var portSet = false;
foreach (var arg in args)
{
    switch (arg)
    {
        case "-h" or "--help" or "/?":
            Console.WriteLine("OoTMM dedicated multiworld server (protocol v4)\n"
                              + "usage: ootmm_server [port] [--pvp] [--friendly-fire] [--death-link]\n"
                              + "       ootmm_server --register-manifest [--world-state <dir>]\n"
                              + "                    <player1-seed> ... <playerN-seed>\n\n"
                              + "Normal settings load from server.properties next to the executable.");
            return 0;
        case "--pvp":
            config.Rules.Pvp = true;
            continue;
        case "--friendly-fire":
            config.Rules.FriendlyFire = true;
            continue;
        case "--death-link":
            config.Rules.DeathLink = true;
            continue;
    }

    if (portSet || !ushort.TryParse(arg, out var port) || port == 0)
    {
        Console.Error.WriteLine($"ootmm_server: invalid argument '{arg}' (expected a port 1-65535)");
        return 2;
    }

    config.Port = port;
    portSet = true;
}

Console.WriteLine("OoTMM dedicated multiworld server");
Console.WriteLine("[server] type /help for admin commands");

var engine = new RelayEngine(storeDir, config.Rules);
var console = new Thread(() =>
{
    while (Console.ReadLine() is { } line)
    {
        if (line.Length > 0)
        {
            engine.SubmitCommand(line);
        }
    }
})
{ IsBackground = true };
console.Start();

return engine.Run(config.Port, CancellationToken.None);

/// <summary>server.properties: created with defaults when missing; malformed
/// values warn and keep their defaults.</summary>
file sealed class ServerProperties
{
    public RelayRules Rules { get; } = new();
    public int Port { get; set; } = RelayProtocol.DefaultPort;

    public static ServerProperties Load(string path)
    {
        var config = new ServerProperties();
        if (!File.Exists(path))
        {
            WriteDefaults(path, config);
            Console.WriteLine($"[server] created default settings file: {path}");
            return config;
        }

        var lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                Console.Error.WriteLine($"[server] server.properties line {lineNo}: expected key=value, got '{line}'");
                continue;
            }

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "port" when ushort.TryParse(value, out var port) && port > 0:
                    config.Port = port;
                    break;
                case "port":
                    Console.Error.WriteLine($"[server] server.properties line {lineNo}: invalid port '{value}'");
                    break;
                case "pvp" or "friendly-fire" or "death-link" or "timer-enabled" or "manifest-bootstrap":
                {
                    var lowered = value.ToLowerInvariant();
                    var parsed = lowered is "true" or "1" or "yes" or "on";
                    if (!parsed && lowered is not ("false" or "0" or "no" or "off"))
                    {
                        Console.Error.WriteLine(
                            $"[server] server.properties line {lineNo}: invalid {key} value '{value}'");
                        break;
                    }

                    switch (key)
                    {
                        case "pvp": config.Rules.Pvp = parsed; break;
                        case "friendly-fire": config.Rules.FriendlyFire = parsed; break;
                        case "death-link": config.Rules.DeathLink = parsed; break;
                        case "manifest-bootstrap": config.Rules.AllowManifestBootstrap = parsed; break;
                        default: config.Rules.TimerEnabled = parsed; break;
                    }

                    break;
                }
                case "timer-min" or "timer-max" when int.TryParse(value, out var percent)
                                                     && percent is >= 0 and <= 1000:
                    if (key == "timer-min")
                    {
                        config.Rules.TimerMinPercent = percent;
                    }
                    else
                    {
                        config.Rules.TimerMaxPercent = percent;
                    }

                    break;
                case "timer-min" or "timer-max":
                    Console.Error.WriteLine(
                        $"[server] server.properties line {lineNo}: invalid {key} value '{value}' (expected 0-1000)");
                    break;
                default:
                    Console.Error.WriteLine($"[server] server.properties line {lineNo}: unknown key '{key}'");
                    break;
            }
        }

        return config;
    }

    private static void WriteDefaults(string path, ServerProperties config)
    {
        try
        {
            File.WriteAllText(path,
                "# OoTMM multiworld server settings\n"
                + "# Players connect by entering <this machine's address>:<port> in the\n"
                + "# launcher's multiworld host box. Clients are roomed by seed, so one\n"
                + "# server can host any number of concurrent multiworld sessions.\n\n"
                + "# TCP port the relay listens on (1-65535).\n"
                + $"port={config.Port}\n\n"
                + "# Player-vs-player combat for every session on this server. A server\n"
                + "# rule; players cannot override it from their launchers.\n"
                + $"pvp={(config.Rules.Pvp ? "true" : "false")}\n\n"
                + "# With PvP on, whether teammates can hurt each other.\n"
                + $"friendly-fire={(config.Rules.FriendlyFire ? "true" : "false")}\n\n"
                + "# Teammates always share deaths. Global death link also kills every\n"
                + "# player on every other team in the session.\n"
                + $"death-link={(config.Rules.DeathLink ? "true" : "false")}\n\n"
                + "# Timer (game speed) feature: each launcher offers a game-speed slider\n"
                + "# constrained to [timer-min, timer-max] percent (100 = vanilla speed).\n"
                + $"timer-enabled={(config.Rules.TimerEnabled ? "true" : "false")}\n"
                + $"timer-min={config.Rules.TimerMinPercent}\n"
                + $"timer-max={config.Rules.TimerMaxPercent}\n\n"
                + "# Register each player's seed manifest automatically when they first join.\n"
                + "# Set false only when the room was provisioned with --register-manifest.\n"
                + $"manifest-bootstrap={(config.Rules.AllowManifestBootstrap ? "true" : "false")}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
