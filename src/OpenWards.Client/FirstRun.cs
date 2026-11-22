using OpenWards.Content.Settings;
using OpenWards.Formats;

namespace OpenWards.Client;

/// <summary>
/// The first start on a computer where the game data was not found: CorsixTH's app.lua opens its folder browser for
/// <c>theme_hospital_install</c> and saves the folder chosen. Here the system's folder picker asks, as the game's own
/// windows need the game's fonts.
/// </summary>
internal static class FirstRun
{
    /// <summary>Asks for the game's folder until a folder with the game data is chosen or the player gives up.</summary>
    public static ThemeHospitalInstall? AskForInstall(GameConfig config, string configPath)
    {
        if (!NativeDialogs.AskYesNo(
            "OpenWards",
            "OpenWards needs the data files of the original Theme Hospital (GOG, EA app or CD), and could not find them.\n\n"
            + "Choose the Theme Hospital folder now?"))
        {
            return null;
        }

        while (NativeDialogs.PickFolder("Choose the Theme Hospital folder (the one that contains DATA, LEVELS and QDATA)") is { } folder)
        {
            if (ThemeHospitalInstall.TryOpen(folder, out var install, out var error))
            {
                try
                {
                    (config with { ThemeHospitalInstall = install.RootPath }).Save(configPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"The settings file {configPath} could not be written ({e.Message}).");
                }

                return install;
            }

            if (!NativeDialogs.AskYesNo("OpenWards", error + "\n\nChoose another folder?"))
            {
                return null;
            }
        }

        return null;
    }
}
