using System.ComponentModel;
using System.Runtime.InteropServices;
using WallpaperUtility.Composition;

namespace WallpaperUtility;

/// <summary>Native resolution of the primary monitor, independent of DPI scaling.</summary>
internal static class PrimaryDisplay
{
    private const int CurrentSettings = -1;

    public static ScreenSize GetResolution()
    {
        var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        // A null device name selects the primary display.
        if (!EnumDisplaySettingsW(null, CurrentSettings, ref mode) || mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0)
        {
            throw new Win32Exception("Windows did not report the primary monitor's resolution.");
        }

        return new ScreenSize(mode.dmPelsWidth, mode.dmPelsHeight);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNumber, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
