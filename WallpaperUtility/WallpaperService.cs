using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WallpaperUtility;

public sealed class WallpaperService
{
    private const uint SetDesktopWallpaper = 0x0014;
    private const uint GetDesktopWallpaper = 0x0073;
    private const uint UpdateProfile = 0x0001;
    private const uint NotifyChange = 0x0002;

    public void Apply(string imagePath, CancellationToken cancellationToken = default) =>
        Apply(preparedPath => WallpaperImage.Prepare(imagePath, preparedPath), cancellationToken);

    /// <summary>Applies a BMP produced by <paramref name="writePreparedBitmap"/> into the inactive cache slot.</summary>
    public void Apply(Action<string> writePreparedBitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writePreparedBitmap);
        cancellationToken.ThrowIfCancellationRequested();
        string cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperUtility", "Wallpapers");
        Directory.CreateDirectory(cacheDirectory);
        // Protect the two slots from simultaneous writes by another app instance.
        using var cacheLock = new FileStream(Path.Combine(cacheDirectory, "cache.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = new StringBuilder(32768);
        if (!ReadWallpaper(GetDesktopWallpaper, (uint)current.Capacity, current, 0))
        {
            throw WindowsError("read the current wallpaper");
        }

        string firstSlot = Path.Combine(cacheDirectory, "current-1.bmp");
        string preparedPath = string.Equals(current.ToString(), firstSlot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(cacheDirectory, "current-2.bmp")
            : firstSlot;
        writePreparedBitmap(preparedPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
            ?? throw new IOException("Windows desktop placement settings could not be opened.");
        object? previousStyle = desktop.GetValue("WallpaperStyle", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        object? previousTiling = desktop.GetValue("TileWallpaper", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        RegistryValueKind styleKind = previousStyle is null ? RegistryValueKind.Unknown : desktop.GetValueKind("WallpaperStyle");
        RegistryValueKind tilingKind = previousTiling is null ? RegistryValueKind.Unknown : desktop.GetValueKind("TileWallpaper");

        try
        {
            // SPI_SETDESKWALLPAPER uses these placement settings when refreshing the desktop.
            desktop.SetValue("WallpaperStyle", "6", RegistryValueKind.String);
            desktop.SetValue("TileWallpaper", "0", RegistryValueKind.String);
            if (!SystemParametersInfoW(SetDesktopWallpaper, 0, preparedPath, UpdateProfile | NotifyChange))
            {
                throw WindowsError("change the wallpaper");
            }
        }
        catch (Exception error) when (WallpaperImage.IsInputError(error) || error is Win32Exception)
        {
            try
            {
                RestorePlacementValue(desktop, "WallpaperStyle", previousStyle, styleKind);
                RestorePlacementValue(desktop, "TileWallpaper", previousTiling, tilingKind);
            }
            catch (Exception restoreError) when (WallpaperImage.IsInputError(restoreError))
            {
                throw new IOException(
                    $"Could not apply the wallpaper: {error.Message} " +
                    $"The previous placement settings could not be restored: {restoreError.Message}",
                    new AggregateException(error, restoreError));
            }

            throw;
        }
    }

    private static void RestorePlacementValue(RegistryKey desktop, string name, object? value, RegistryValueKind kind)
    {
        if (value is null)
        {
            desktop.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            desktop.SetValue(name, value, kind);
        }
    }

    private static Win32Exception WindowsError(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, error == 0
            ? $"Windows could not {operation} and did not provide an error code."
            : $"Windows could not {operation}: {new Win32Exception(error).Message} (code {error}).");
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode,
        ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadWallpaper(uint action, uint parameter, StringBuilder value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, string value, uint flags);
}
