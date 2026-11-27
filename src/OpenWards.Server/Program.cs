using System.Globalization;
using System.Net;
using OpenWards.Content.Interface;
using OpenWards.Content.Levels;
using OpenWards.Formats;
using OpenWards.Net.Lockstep;
using OpenWards.Net.Transport;
using OpenWards.Server;
using OpenWards.Sim.Economy;

var parseResult = ServerCommandLine.Parse(args);
if (parseResult.Action is not null)
{
    // --help, --version or a mistake on the command line: System.CommandLine prints it.
    return parseResult.Invoke();
}

var options = ServerCommandLine.Bind(parseResult);
var gamePath = options.GamePath;
var level = options.Level;
var mapPath = options.MapPath;
var port = options.Port;
var publicHost = options.PublicHost;
var freeBuild = options.FreeBuild;
var computers = options.Computers;
var seed = options.Seed;
var difficulty = options.Difficulty;
var speed = options.Speed;

LoadedLevelMap loaded;
string levelName;
ThemeHospitalInstall? lobbyInstall;
try
{
    if (mapPath is not null)
    {
        // The game's level files give the money settings, if the game is at hand.
        var settings = gamePath is null ? ThemeHospitalInstall.Locate()
            : ThemeHospitalInstall.TryOpen(gamePath, out var found, out _) ? found : null;
        loaded = LevelMapLoader.LoadFile(mapPath, settings);
        lobbyInstall = settings;
        if (freeBuild)
        {
            loaded = loaded.WithFreeBuild();
        }

        levelName = Path.GetFileName(mapPath);
    }
    else
    {
        ThemeHospitalInstall? install;
        if (gamePath is null)
        {
            install = ThemeHospitalInstall.Locate();
        }
        else if (!ThemeHospitalInstall.TryOpen(gamePath, out install, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (install is null)
        {
            Console.Error.WriteLine($"Theme Hospital data was not found. Pass the game folder or set {ThemeHospitalInstall.PathEnvironmentVariable}.");
            return 1;
        }

        loaded = LevelMapLoader.LoadOriginal(install, level, difficulty);
        levelName = level;
        lobbyInstall = install;
    }
}
catch (Exception e) when (e is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Cannot load the level: {e.Message}");
    return 1;
}

// The host (player 1) may switch everyone to the level served or to any original level of the game at hand.
var offered = new List<LobbyLevel> { new(levelName, () => loaded.StartSimulation(seed)) };
if (lobbyInstall?.FindDirectory("LEVELS") is { } levelsFolder)
{
    foreach (var name in Directory.EnumerateFiles(levelsFolder, "LEVEL.L*").Select(Path.GetFileName).OfType<string>()
        .Select(name => name.ToUpperInvariant())
        .OrderBy(name => int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
        .ThenBy(name => name, StringComparer.Ordinal))
    {
        if (!string.Equals(name, levelName, StringComparison.OrdinalIgnoreCase))
        {
            var install = lobbyInstall;
            offered.Add(new LobbyLevel(name, () => LevelMapLoader.LoadOriginal(install, name, difficulty).StartSimulation(seed)));
        }
    }
}

var server = new LockstepServer(loaded.StartSimulation(seed), levelName, offered) { Log = line => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}"), ComputerOpponents = computers };
server.SetSpeed(speed);
using var runner = new ServerRunner(server, new TcpConnectionListener(publicHost ? IPAddress.Any : IPAddress.Loopback, port));
Console.WriteLine($"Serving {levelName} ({loaded.Map.PlayerCount} players) on port {runner.Port}{(publicHost ? "" : " (this machine only; add --public for others)")}. Press Ctrl+C to stop.");

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};
stop.Wait();
Console.WriteLine("Stopping.");
return 0;
