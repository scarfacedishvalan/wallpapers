using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallpaperUtility.Composition;

/// <summary>Draws a <see cref="CompositionPage"/> into a bitmap of exactly the screen size.</summary>
public static class CompositionRenderer
{
    public static readonly Color DefaultBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);

    /// <remarks>Uses only WIC imaging, so it can run on any thread. The result is frozen.</remarks>
    public static BitmapSource Render(CompositionPage page, ScreenSize screen, Color? background = null,
        CancellationToken cancellationToken = default)
    {
        Color fill = background ?? DefaultBackground;
        int stride = screen.Width * 4;
        var canvas = new byte[stride * screen.Height];
        for (int i = 0; i < canvas.Length; i += 4)
        {
            canvas[i] = fill.B;
            canvas[i + 1] = fill.G;
            canvas[i + 2] = fill.R;
            canvas[i + 3] = 0xFF;
        }

        foreach (Placement placement in page.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int32Rect target = Snap(placement.Bounds, screen);
            if (target.Width > 0 && target.Height > 0)
            {
                DrawPhoto(placement.Photo, target, canvas, stride, fill);
            }
        }

        var result = BitmapSource.Create(screen.Width, screen.Height, 96, 96, PixelFormats.Bgr32, null, canvas, stride);
        result.Freeze();
        return result;
    }

    private static Int32Rect Snap(LayoutRect bounds, ScreenSize screen)
    {
        int left = Math.Clamp((int)Math.Round(bounds.X), 0, screen.Width);
        int top = Math.Clamp((int)Math.Round(bounds.Y), 0, screen.Height);
        int right = Math.Clamp((int)Math.Round(bounds.Right), left, screen.Width);
        int bottom = Math.Clamp((int)Math.Round(bounds.Bottom), top, screen.Height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static void DrawPhoto(PhotoInfo photo, Int32Rect target, byte[] canvas, int canvasStride, Color background)
    {
        BitmapSource scaled = LoadScaled(photo, target.Width, target.Height);
        int stride = target.Width * 4;
        var pixels = new byte[stride * target.Height];
        scaled.CopyPixels(pixels, stride, 0);

        // Pixels are premultiplied BGRA; blend over the background so transparency stays neutral.
        for (int y = 0; y < target.Height; y++)
        {
            int source = y * stride;
            int destination = (target.Y + y) * canvasStride + target.X * 4;
            for (int x = 0; x < target.Width; x++, source += 4, destination += 4)
            {
                int inverseAlpha = 255 - pixels[source + 3];
                canvas[destination] = (byte)(pixels[source] + background.B * inverseAlpha / 255);
                canvas[destination + 1] = (byte)(pixels[source + 1] + background.G * inverseAlpha / 255);
                canvas[destination + 2] = (byte)(pixels[source + 2] + background.R * inverseAlpha / 255);
            }
        }
    }

    private static BitmapSource LoadScaled(PhotoInfo photo, int width, int height)
    {
        var decoded = new BitmapImage();
        using (var stream = File.OpenRead(photo.Path))
        {
            decoded.BeginInit();
            decoded.StreamSource = stream;
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            // Decode straight to the target size; the decoder works in stored (unrotated) axes.
            decoded.DecodePixelWidth = photo.IsRotated ? height : width;
            decoded.DecodePixelHeight = photo.IsRotated ? width : height;
            decoded.EndInit();
        }

        BitmapSource oriented = ApplyOrientation(decoded, photo.ExifOrientation);
        if (oriented.PixelWidth != width || oriented.PixelHeight != height)
        {
            oriented = new TransformedBitmap(oriented,
                new ScaleTransform((double)width / oriented.PixelWidth, (double)height / oriented.PixelHeight));
        }

        var converted = new FormatConvertedBitmap(oriented, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>Rotates/mirrors stored pixels so the photo appears as the EXIF orientation intends.</summary>
    public static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null
        };
        return transform is null ? source : new TransformedBitmap(source, transform);
    }
}
