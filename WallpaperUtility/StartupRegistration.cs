using Microsoft.Win32;

namespace WallpaperUtility;

/// <summary>Per-user "start with Windows" entry under HKCU\...\Run (no admin rights needed).</summary>
internal static class StartupRegistration
{
    public const string BackgroundArgument = "--background";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WallpaperUtility";

    public static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArgument}";

    public static string? RegisteredCommand()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static void Enable()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, Command, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
