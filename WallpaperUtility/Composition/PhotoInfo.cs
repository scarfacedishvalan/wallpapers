namespace WallpaperUtility.Composition;

public enum PhotoShape
{
    Landscape,
    Portrait
}

/// <summary>Stored pixel size plus EXIF orientation (1–8) of a photo.</summary>
public sealed record PhotoInfo(string Path, int PixelWidth, int PixelHeight, int ExifOrientation = 1)
{
    // Orientations 5–8 rotate the photo by 90° or 270°, swapping width and height.
    public bool IsRotated => ExifOrientation is >= 5 and <= 8;

    public int DisplayWidth => IsRotated ? PixelHeight : PixelWidth;

    public int DisplayHeight => IsRotated ? PixelWidth : PixelHeight;

    public double AspectRatio => (double)DisplayWidth / DisplayHeight;

    // The only filter: every photo is either landscape (incl. square) or portrait.
    public PhotoShape Shape => AspectRatio >= 1.0 ? PhotoShape.Landscape : PhotoShape.Portrait;
}
