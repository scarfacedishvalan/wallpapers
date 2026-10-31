using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Media.Imaging;
using WallpaperUtility.Composition;

namespace WallpaperUtility;

internal static class WallpaperImage
{
    public static bool IsInputError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or FormatException or InvalidOperationException
            or COMException or SecurityException;

    public static void Prepare(string imagePath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        string sourcePath = Path.GetFullPath(imagePath);
        using var encoded = new MemoryStream();
        using (var source = File.OpenRead(sourcePath))
        {
            // Decode lazily while the stream is open: eager OnLoad decoding rejects some
            // camera JPEGs with minor defects that otherwise decode fine.
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);

            if (decoder is not (JpegBitmapDecoder or PngBitmapDecoder or BmpBitmapDecoder))
            {
                throw new NotSupportedException("Choose a JPEG, PNG, or BMP image.");
            }

            BitmapFrame frame = decoder.Frames[0];
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(
                CompositionRenderer.ApplyOrientation(frame, PhotoAnalyzer.ReadOrientation(frame))));
            // Finish decoding before touching the inactive cache file.
            encoder.Save(encoded);
        }

        WriteFile(encoded, outputPath);
    }

    /// <summary>Saves an already rendered bitmap (such as a collage) as the prepared BMP.</summary>
    public static void Save(BitmapSource bitmap, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var encoded = new MemoryStream();
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(encoded);
        WriteFile(encoded, outputPath);
    }

    private static void WriteFile(MemoryStream encoded, string outputPath)
    {
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.Position = 0;
        encoded.CopyTo(output);
    }
}
