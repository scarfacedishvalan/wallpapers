using System.IO;
using System.Windows.Media.Imaging;

namespace WallpaperUtility.Composition;

public sealed record AnalysisResult(IReadOnlyList<PhotoInfo> Photos, IReadOnlyList<(string Path, string Error)> Skipped);

/// <summary>Reads photo dimensions and EXIF orientation from file headers without decoding pixels.</summary>
public static class PhotoAnalyzer
{
    private const string OrientationQuery = "/app1/ifd/{ushort=274}";

    public static PhotoInfo Analyze(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder is not (JpegBitmapDecoder or PngBitmapDecoder or BmpBitmapDecoder))
        {
            throw new NotSupportedException("Choose a JPEG, PNG, or BMP image.");
        }

        BitmapFrame frame = decoder.Frames[0];
        return new PhotoInfo(fullPath, frame.PixelWidth, frame.PixelHeight, ReadOrientation(frame));
    }

    public static AnalysisResult AnalyzeAll(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var photos = new List<PhotoInfo>();
        var skipped = new List<(string, string)>();
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                photos.Add(Analyze(path));
            }
            catch (Exception error) when (WallpaperImage.IsInputError(error))
            {
                skipped.Add((path, error.Message));
            }
        }

        return new AnalysisResult(photos, skipped);
    }

    /// <summary>EXIF orientation (1–8) of a frame, or 1 when absent or unreadable.</summary>
    public static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(OrientationQuery))
            {
                int value = Convert.ToInt32(metadata.GetQuery(OrientationQuery));
                return value is >= 1 and <= 8 ? value : 1;
            }
        }
        catch (Exception error) when (WallpaperImage.IsInputError(error) || error is InvalidCastException)
        {
            // Missing or unreadable metadata: treat the photo as stored upright.
        }

        return 1;
    }
}
