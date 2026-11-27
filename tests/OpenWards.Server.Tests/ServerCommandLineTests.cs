using System.CommandLine;
using OpenWards.Content.Interface;
using OpenWards.Server;
using OpenWards.Sim.Economy;

namespace OpenWards.Server.Tests;

public sealed class ServerCommandLineTests
{
    private static ServerOptions Bind(params string[] args)
    {
        var result = ServerCommandLine.Parse(args);
        Assert.Empty(result.Errors);
        Assert.Null(result.Action);
        return ServerCommandLine.Bind(result);
    }

    private static void AssertError(params string[] args) => Assert.NotEmpty(ServerCommandLine.Parse(args).Errors);

    [Fact]
    public void No_arguments_give_the_defaults()
    {
        var options = Bind();

        Assert.Equal(new ServerOptions(), options);
        Assert.Null(options.GamePath);
        Assert.Equal("LEVEL.L20", options.Level);
        Assert.Null(options.MapPath);
        Assert.False(options.FreeBuild);
        Assert.Equal(Difficulty.Medium, options.Difficulty);
        Assert.Equal(7777, options.Port);
        Assert.False(options.PublicHost);
        Assert.True(options.Computers);
        Assert.Equal(1UL, options.Seed);
        Assert.Equal(GameSpeed.Normal, options.Speed);
    }

    [Fact]
    public void Every_option_is_read()
    {
        var options = Bind(
            @"C:\Games\Theme Hospital",
            "--level", "LEVEL.L3",
            "--map", "a.map",
            "--free-build",
            "--difficulty", "hard",
            "--port", "9000",
            "--public",
            "--no-computers",
            "--seed", "18446744073709551615",
            "--speed", "pause");

        Assert.Equal(@"C:\Games\Theme Hospital", options.GamePath);
        Assert.Equal("LEVEL.L3", options.Level);
        Assert.Equal("a.map", options.MapPath);
        Assert.True(options.FreeBuild);
        Assert.Equal(Difficulty.Hard, options.Difficulty);
        Assert.Equal(9000, options.Port);
        Assert.True(options.PublicHost);
        Assert.False(options.Computers);
        Assert.Equal(ulong.MaxValue, options.Seed);
        Assert.Equal(GameSpeed.Pause, options.Speed);
    }

    [Fact]
    public void The_game_folder_may_come_after_the_options()
    {
        var options = Bind("--port", "9000", "Games");

        Assert.Equal("Games", options.GamePath);
        Assert.Equal(9000, options.Port);
    }

    [Theory]
    [InlineData("--port", "big")]
    [InlineData("--seed", "-1")]
    [InlineData("--speed", "SpeedUp")]
    [InlineData("--speed", "fast")]
    [InlineData("--difficulty", "brutal")]
    [InlineData("--level")]
    [InlineData("--unknown")]
    [InlineData("Games", "Other")]
    public void Mistakes_are_errors(params string[] args) => AssertError(args);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_lists_the_options(string option)
    {
        var output = new StringWriter();
        var exitCode = ServerCommandLine.Parse([option]).Invoke(new InvocationConfiguration { Output = output });

        Assert.Equal(0, exitCode);
        var help = output.ToString();
        foreach (var name in new[] { "--level", "--map", "--free-build", "--difficulty", "--port", "--public", "--no-computers", "--seed", "--speed" })
        {
            Assert.Contains(name, help, StringComparison.Ordinal);
        }
    }
}
