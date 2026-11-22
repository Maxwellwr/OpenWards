using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenWards.Client;

/// <summary>
/// The operating system's own message box and folder picker, for the moments before the game's window and the game's
/// fonts exist: the game data not found and a crash. The game runs without a console window, so a message written to
/// the console alone is never seen. Elsewhere than on Windows the message goes to the console only.
/// </summary>
internal static partial class NativeDialogs
{
    private const uint MbOk = 0x0;
    private const uint MbYesNo = 0x4;
    private const uint MbIconError = 0x10;
    private const uint MbIconQuestion = 0x20;
    private const int IdYes = 6;
    private const uint BifReturnOnlyFsDirs = 0x1;
    private const uint BifNewDialogStyle = 0x40;
    private const int MaxPath = 260;

    /// <summary>Shows an error with an OK button (and writes it to the console).</summary>
    public static void ShowError(string title, string message)
    {
        Console.Error.WriteLine(message);
        if (OperatingSystem.IsWindows())
        {
            _ = MessageBoxW(IntPtr.Zero, message, title, MbOk | MbIconError);
        }
    }

    /// <summary>Asks a yes or no question; false where there is no message box.</summary>
    public static bool AskYesNo(string title, string message)
    {
        Console.Error.WriteLine(message);
        return OperatingSystem.IsWindows() && MessageBoxW(IntPtr.Zero, message, title, MbYesNo | MbIconQuestion) == IdYes;
    }

    /// <summary>Lets the player pick a folder; null when cancelled or where there is no folder picker.</summary>
    public static string? PickFolder(string instruction) => OperatingSystem.IsWindows() ? PickFolderOnWindows(instruction) : null;

    [SupportedOSPlatform("windows")]
    private static string? PickFolderOnWindows(string instruction)
    {
        // The shell's folder picker wants a single-threaded apartment, which the program's main thread is not.
        string? chosen = null;
        var thread = new Thread(() => chosen = BrowseForFolder(instruction));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return chosen;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string? BrowseForFolder(string instruction)
    {
        _ = CoInitializeEx(IntPtr.Zero, 0x2 /* COINIT_APARTMENTTHREADED */);
        var displayName = stackalloc char[MaxPath];
        var path = stackalloc char[MaxPath];
        fixed (char* title = instruction)
        {
            var info = new BrowseInfo
            {
                DisplayName = displayName,
                Title = title,
                Flags = BifReturnOnlyFsDirs | BifNewDialogStyle,
            };
            var list = SHBrowseForFolderW(&info);
            if (list == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return SHGetPathFromIDListW(list, path) ? new string(path) : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(list);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct BrowseInfo
    {
        public IntPtr Owner;
        public IntPtr Root;
        public char* DisplayName;
        public char* Title;
        public uint Flags;
        public IntPtr Callback;
        public IntPtr Parameter;
        public int Image;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial IntPtr SHBrowseForFolderW(BrowseInfo* info);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial bool SHGetPathFromIDListW(IntPtr list, char* path);

    [LibraryImport("ole32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int CoInitializeEx(IntPtr reserved, uint coInit);
}
