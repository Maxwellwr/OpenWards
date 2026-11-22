using System.Globalization;
using System.Text;
using OpenWards.Content.Settings;

namespace OpenWards.Client;

/// <summary>
/// What the game leaves behind when it fails: a <c>crash.log</c> beside the settings, and a message that says where
/// it is. The game has no console window, so without this a crash just closes it.
/// </summary>
internal static class CrashReport
{
    /// <summary>The crash log's file: beside the settings file.</summary>
    public static string LogPath()
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(GameConfig.DefaultPath())) ?? AppContext.BaseDirectory;
        return Path.Combine(folder, "crash.log");
    }

    /// <summary>Reports a failure on any other thread too.</summary>
    public static void Install() =>
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception && !System.Diagnostics.Debugger.IsAttached)
            {
                Report(exception);
            }
        };

    /// <summary>Writes the failure to the crash log and tells the player.</summary>
    public static void Report(Exception exception)
    {
        var path = LogPath();
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"OpenWards {UpdateCheck.CurrentVersion() ?? "unknown version"} crashed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n")
            .Append(CultureInfo.InvariantCulture, $"{Environment.OSVersion}, .NET {Environment.Version}\n\n")
            .Append(exception)
            .Append('\n')
            .ToString();
        string where;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            where = $"The details are in {path}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            where = $"The details could not be written to {path} ({e.Message}).";
        }

        Console.Error.WriteLine(text);
        NativeDialogs.ShowError(
            "OpenWards",
            $"OpenWards has stopped because of an error.\n\n{exception.Message}\n\n{where}\n"
            + "Please report it at https://github.com/Maxwellwr/OpenWards/issues with that file.");
    }
}
