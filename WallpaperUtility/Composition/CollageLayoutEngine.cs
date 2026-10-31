namespace WallpaperUtility.Composition;

public readonly record struct ScreenSize(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public sealed record Placement(PhotoInfo Photo, LayoutRect Bounds);

public enum PageKind
{
    SingleLandscape,
    PortraitCollage,
    SinglePortrait
}

/// <summary>One generated wallpaper: the photos it contains and where each is drawn.</summary>
public sealed record CompositionPage(PageKind Kind, IReadOnlyList<Placement> Placements, double Coverage);

public sealed record CollageOptions
{
    /// <summary>Outer margin around portrait collages, as a fraction of the screen height.</summary>
    public double MarginFraction { get; init; } = 0.03;

    /// <summary>Space between photos in a collage, as a fraction of the screen height.</summary>
    public double GapFraction { get; init; } = 0.015;

    /// <summary>Most portraits placed side by side on one wallpaper (at least 2 are always grouped).</summary>
    public int MaxGroupSize { get; init; } = 4;
}

/// <summary>
/// Pure layout logic: turns photo sizes and a screen size into wallpaper pages.
/// Landscape photos get a page each; portrait photos are grouped side by side.
/// Aspect ratios are always preserved and nothing is cropped.
/// </summary>
public static class CollageLayoutEngine
{
    public const int MinGroupSize = 2;
    private const int GroupingWindow = 60;

    public static IReadOnlyList<CompositionPage> Plan(
        IReadOnlyList<PhotoInfo> photos, ScreenSize screen, CollageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(photos);
        options ??= new CollageOptions();
        Validate(screen, options);
        foreach (PhotoInfo photo in photos)
        {
            if (photo.PixelWidth <= 0 || photo.PixelHeight <= 0)
            {
                throw new ArgumentException($"Photo '{photo.Path}' has an invalid size.", nameof(photos));
            }
        }

        var pages = new List<(int Order, CompositionPage Page)>();
        var portraitIndexes = new List<int>();
        // Side-by-side portraits only make a balanced picture on a landscape monitor.
        bool collagesAllowed = screen.Width > screen.Height;
        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i].Shape == PhotoShape.Portrait && collagesAllowed)
            {
                portraitIndexes.Add(i);
            }
            else
            {
                pages.Add((i, FitSingle(photos[i], screen)));
            }
        }

        if (portraitIndexes.Count == 1)
        {
            int index = portraitIndexes[0];
            pages.Add((index, LayoutRow([photos[index]], screen, options) with { Kind = PageKind.SinglePortrait }));
        }
        else if (portraitIndexes.Count > 1)
        {
            foreach (int[] group in GroupPortraits(portraitIndexes, photos, screen, options))
            {
                pages.Add((group[0], LayoutRow(group.Select(i => photos[i]).ToArray(), screen, options)));
            }
        }

        return pages.OrderBy(page => page.Order).Select(page => page.Page).ToArray();
    }

    /// <summary>Lays out photos in one centred row, all at the same height.</summary>
    public static CompositionPage LayoutRow(IReadOnlyList<PhotoInfo> photos, ScreenSize screen, CollageOptions options)
    {
        if (photos.Count == 0)
        {
            throw new ArgumentException("A row needs at least one photo.", nameof(photos));
        }

        (double height, double rowWidth) = RowSize(photos.Select(p => p.AspectRatio).ToArray(), screen, options);
        double gap = Gap(screen, options);
        double x = (screen.Width - rowWidth) / 2;
        double y = (screen.Height - height) / 2;
        var placements = new Placement[photos.Count];
        for (int i = 0; i < photos.Count; i++)
        {
            double width = photos[i].AspectRatio * height;
            placements[i] = new Placement(photos[i], new LayoutRect(x, y, width, height));
            x += width + gap;
        }

        return new CompositionPage(PageKind.PortraitCollage, placements, Coverage(placements, screen));
    }

    /// <summary>Fraction of the screen covered by a row of photos with these aspect ratios.</summary>
    public static double RowCoverage(IReadOnlyList<double> ratios, ScreenSize screen, CollageOptions options)
    {
        (double height, _) = RowSize(ratios, screen, options);
        return ratios.Sum() * height * height / ((double)screen.Width * screen.Height);
    }

    private static CompositionPage FitSingle(PhotoInfo photo, ScreenSize screen)
    {
        double ratio = photo.AspectRatio;
        double width = screen.Width;
        double height = width / ratio;
        if (height > screen.Height)
        {
            height = screen.Height;
            width = height * ratio;
        }

        var placement = new Placement(photo,
            new LayoutRect((screen.Width - width) / 2, (screen.Height - height) / 2, width, height));
        PageKind kind = photo.Shape == PhotoShape.Landscape ? PageKind.SingleLandscape : PageKind.SinglePortrait;
        return new CompositionPage(kind, [placement], Coverage([placement], screen));
    }

    private static (double Height, double RowWidth) RowSize(
        IReadOnlyList<double> ratios, ScreenSize screen, CollageOptions options)
    {
        double margin = screen.Height * options.MarginFraction;
        double gaps = (ratios.Count - 1) * Gap(screen, options);
        double sumRatios = ratios.Sum();
        double height = Math.Min(screen.Height - 2 * margin, (screen.Width - 2 * margin - gaps) / sumRatios);
        return (height, sumRatios * height + gaps);
    }

    private static double Gap(ScreenSize screen, CollageOptions options) => screen.Height * options.GapFraction;

    private static double Coverage(IEnumerable<Placement> placements, ScreenSize screen) =>
        placements.Sum(p => p.Bounds.Width * p.Bounds.Height) / ((double)screen.Width * screen.Height);

    /// <summary>
    /// Splits portraits (kept in order) into consecutive groups of 2..MaxGroupSize,
    /// choosing the split with the highest mean coverage per wallpaper.
    /// </summary>
    private static List<int[]> GroupPortraits(
        List<int> indexes, IReadOnlyList<PhotoInfo> photos, ScreenSize screen, CollageOptions options)
    {
        var windows = new List<List<int>>();
        for (int start = 0; start < indexes.Count; start += GroupingWindow)
        {
            windows.Add(indexes.GetRange(start, Math.Min(GroupingWindow, indexes.Count - start)));
        }

        // A window too small to form a group joins the previous one.
        if (windows.Count > 1 && windows[^1].Count < MinGroupSize)
        {
            windows[^2].AddRange(windows[^1]);
            windows.RemoveAt(windows.Count - 1);
        }

        var groups = new List<int[]>();
        foreach (List<int> window in windows)
        {
            double[] ratios = window.Select(i => photos[i].AspectRatio).ToArray();
            foreach ((int start, int length) in BestSplit(ratios, screen, options))
            {
                groups.Add(window.GetRange(start, length).ToArray());
            }
        }

        return groups;
    }

    private static List<(int Start, int Length)> BestSplit(double[] ratios, ScreenSize screen, CollageOptions options)
    {
        int n = ratios.Length;
        int max = options.MaxGroupSize;
        int maxGroups = n / MinGroupSize;
        // best[i, g]: highest total coverage for the first i photos split into g groups.
        var best = new double[n + 1, maxGroups + 1];
        var lastLength = new int[n + 1, maxGroups + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int g = 0; g <= maxGroups; g++)
            {
                best[i, g] = double.NegativeInfinity;
            }
        }

        best[0, 0] = 0;
        for (int i = MinGroupSize; i <= n; i++)
        {
            for (int length = MinGroupSize; length <= Math.Min(max, i); length++)
            {
                double coverage = RowCoverage(new ArraySegment<double>(ratios, i - length, length), screen, options);
                for (int g = 1; g <= i / MinGroupSize; g++)
                {
                    double candidate = best[i - length, g - 1] + coverage;
                    if (candidate > best[i, g] + 1e-12)
                    {
                        best[i, g] = candidate;
                        lastLength[i, g] = length;
                    }
                }
            }
        }

        int bestGroups = -1;
        double bestMean = double.NegativeInfinity;
        for (int g = 1; g <= maxGroups; g++)
        {
            // On a tie, prefer more wallpapers so each photo is larger.
            if (!double.IsNegativeInfinity(best[n, g]) && best[n, g] / g >= bestMean - 1e-12)
            {
                bestMean = best[n, g] / g;
                bestGroups = g;
            }
        }

        if (bestGroups < 0)
        {
            throw new InvalidOperationException("Portrait photos could not be grouped.");
        }

        var split = new List<(int, int)>();
        for (int i = n, g = bestGroups; i > 0; g--)
        {
            int length = lastLength[i, g];
            split.Add((i - length, length));
            i -= length;
        }

        split.Reverse();
        return split;
    }

    private static void Validate(ScreenSize screen, CollageOptions options)
    {
        if (screen.Width < 64 || screen.Height < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(screen), "The screen must be at least 64×64 pixels.");
        }

        // A maximum of at least 3 lets any count of two or more portraits be split without a leftover.
        if (options.MaxGroupSize is < 3 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxGroupSize must be between 3 and 6.");
        }

        if (options.MarginFraction is < 0 or > 0.2 || options.GapFraction is < 0 or > 0.1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Margin or gap is out of range.");
        }
    }
}
