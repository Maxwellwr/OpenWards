using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using OpenWards.Content.Interface;
using OpenWards.Content.Levels;
using OpenWards.Sim.Economy;

namespace OpenWards.Server;

/// <summary>What the server was asked to do on the command line.</summary>
internal sealed record ServerOptions
{
    public string? GamePath { get; init; }
    public string Level { get; init; } = ServerCommandLine.DefaultLevel;
    public string? MapPath { get; init; }
    public bool FreeBuild { get; init; }
    public Difficulty Difficulty { get; init; } = Difficulty.Medium;
    public int Port { get; init; } = ServerCommandLine.DefaultPort;
    public bool PublicHost { get; init; }
    public bool Computers { get; init; } = true;
    public ulong Seed { get; init; } = 1;
    public GameSpeed Speed { get; init; } = GameSpeed.Normal;
}

/// <summary>The server's command line, parsed with System.CommandLine.</summary>
internal static class ServerCommandLine
{
    public const int DefaultPort = 7777;
    public const string DefaultLevel = "LEVEL.L20";

    private static readonly GameSpeed[] StartSpeeds = [.. Enum.GetValues<GameSpeed>().Where(speed => speed != GameSpeed.SpeedUp)];

    private static readonly Argument<string?> GamePathArgument = new("game folder")
    {
        Description = "The Theme Hospital folder (default: wherever the game is found)",
        Arity = ArgumentArity.ZeroOrOne,
        Validators =
        {
            result =>
            {
                // System.CommandLine would take an unknown option for the game folder.
                if (result.Tokens is [{ Value: var text }] && text.StartsWith("--", StringComparison.Ordinal))
                {
                    result.AddError($"Unknown option {text}.");
                }
            },
        },
    };

    private static readonly Option<string> LevelOption = new("--level")
    {
        Description = "Original level to play (the default is a two-player map)",
        HelpName = "NAME",
        DefaultValueFactory = _ => DefaultLevel,
    };

    private static readonly Option<string?> MapOption = new("--map")
    {
        Description = "Map file to play instead, e.g. a CorsixTH .map or .level file",
        HelpName = "PATH",
    };

    private static readonly Option<bool> FreeBuildOption = new("--free-build")
    {
        Description = "Play the map file without money (CorsixTH's free build mode)",
    };

    private static readonly Option<Difficulty> DifficultyOption = new("--difficulty")
    {
        Description = "Difficulty of an original level: Easy, Medium or Hard",
        HelpName = "NAME",
        DefaultValueFactory = _ => Difficulty.Medium,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (LevelRulesLoader.TryParseDifficulty(text, out var difficulty))
            {
                return difficulty;
            }

            result.AddError($"Unknown difficulty {text}; use Easy, Medium or Hard.");
            return Difficulty.Medium;
        },
    };

    private static readonly Option<int> PortOption = new("--port")
    {
        Description = "TCP port",
        HelpName = "PORT",
        DefaultValueFactory = _ => DefaultPort,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (int.TryParse(text, CultureInfo.InvariantCulture, out var port))
            {
                return port;
            }

            result.AddError($"The port {text} is not a number.");
            return DefaultPort;
        },
    };

    private static readonly Option<bool> PublicOption = new("--public")
    {
        Description = "Accept connections from other machines, not only this one",
    };

    private static readonly Option<bool> NoComputersOption = new("--no-computers")
    {
        Description = "Leave the places nobody holds empty; by default a computer opponent plays each until a person joins (not in the original's network game)",
    };

    private static readonly Option<ulong> SeedOption = new("--seed")
    {
        Description = "Seed for the simulation's random numbers",
        HelpName = "N",
        DefaultValueFactory = _ => 1,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (ulong.TryParse(text, CultureInfo.InvariantCulture, out var seed))
            {
                return seed;
            }

            result.AddError($"The seed {text} is not a whole number from 0 up.");
            return 1;
        },
    };

    private static readonly Option<GameSpeed> SpeedOption = new("--speed")
    {
        Description = $"Speed the game starts at: {string.Join(", ", StartSpeeds)} (Pause waits until a player starts it)",
        HelpName = "NAME",
        DefaultValueFactory = _ => GameSpeed.Normal,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (Enum.TryParse<GameSpeed>(text, ignoreCase: true, out var speed) && speed != GameSpeed.SpeedUp)
            {
                return speed;
            }

            result.AddError($"Unknown speed {text}; use {string.Join(", ", StartSpeeds)}.");
            return GameSpeed.Normal;
        },
    };

    /// <summary>Builds the root command. A new one each time, since a parse result keeps hold of it.</summary>
    public static RootCommand CreateCommand() => new(
        "OpenWards headless network game server. Player 1 may switch everyone to the level served or another "
        + "original level of the game (F3 in the client); it starts paused.")
    {
        GamePathArgument,
        LevelOption,
        MapOption,
        FreeBuildOption,
        DifficultyOption,
        PortOption,
        PublicOption,
        NoComputersOption,
        SeedOption,
        SpeedOption,
    };

    public static ParseResult Parse(IReadOnlyList<string> args) => CreateCommand().Parse(args);

    /// <summary>The options of a parse without errors.</summary>
    public static ServerOptions Bind(ParseResult result) => new()
    {
        GamePath = result.GetValue(GamePathArgument),
        Level = result.GetValue(LevelOption) ?? DefaultLevel,
        MapPath = result.GetValue(MapOption),
        FreeBuild = result.GetValue(FreeBuildOption),
        Difficulty = result.GetValue(DifficultyOption),
        Port = result.GetValue(PortOption),
        PublicHost = result.GetValue(PublicOption),
        Computers = !result.GetValue(NoComputersOption),
        Seed = result.GetValue(SeedOption),
        Speed = result.GetValue(SpeedOption),
    };
}
