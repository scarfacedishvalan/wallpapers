using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperUtility.Composition;

// Usage: WallpaperUtility.CompositionTests [photoFolder] [outputFolder]
// Without a photo folder only the synthetic tests run.
int failures = 0;
int checks = 0;
var options = new CollageOptions();
var uhd = new ScreenSize(3840, 2160);

void Check(bool condition, string message)
{
    checks++;
    if (!condition)
    {
        failures++;
        Console.WriteLine($"  FAIL: {message}");
    }
}

PhotoInfo P(double ratio, string name = "p") => ratio >= 1
    ? new PhotoInfo(name, (int)Math.Round(1000 * ratio), 1000)
    : new PhotoInfo(name, 1000, (int)Math.Round(1000 / ratio));

string Describe(IReadOnlyList<CompositionPage> pages) =>
    string.Join(" | ", pages.Select(p => $"{p.Kind switch
    {
        PageKind.SingleLandscape => "L",
        PageKind.SinglePortrait => "P",
        _ => "C" + p.Placements.Count
    }}:{p.Coverage:P0}"));

IReadOnlyList<CompositionPage> RunCase(string name, IReadOnlyList<PhotoInfo> photos, ScreenSize screen)
{
    var pages = CollageLayoutEngine.Plan(photos, screen, options);
    Console.WriteLine($"[{name}] {screen}: {Describe(pages)}");
    Validate(photos, pages, screen);
    return pages;
}

void Validate(IReadOnlyList<PhotoInfo> photos, IReadOnlyList<CompositionPage> pages, ScreenSize screen)
{
    var placed = pages.SelectMany(p => p.Placements).Select(p => p.Photo).ToList();
    Check(placed.Count == photos.Count, $"placed {placed.Count} of {photos.Count} photos");
    foreach (PhotoInfo photo in photos)
    {
        Check(placed.Count(p => ReferenceEquals(p, photo)) == 1, $"{photo.Path} must appear exactly once");
    }

    // Photo order must be preserved across the sequence of pages.
    var order = photos.Select((p, i) => (p, i)).ToDictionary(x => (object)x.p, x => x.i, ReferenceEqualityComparer.Instance);
    var sequence = placed.Select(p => order[p]).ToList();
    if (screen.Width > screen.Height)
    {
        var portraitSequence = sequence.Where(i => photos[i].Shape == PhotoShape.Portrait).ToList();
        Check(portraitSequence.SequenceEqual(portraitSequence.Order()), "portrait order preserved");
    }

    const double eps = 1e-6;
    double gap = screen.Height * options.GapFraction;
    int portraitTotal = screen.Width > screen.Height ? photos.Count(p => p.Shape == PhotoShape.Portrait) : 0;
    foreach (CompositionPage page in pages)
    {
        foreach (Placement placement in page.Placements)
        {
            LayoutRect r = placement.Bounds;
            Check(Math.Abs(r.Width / r.Height - placement.Photo.AspectRatio) < 1e-9, "aspect ratio preserved");
            Check(r.X >= -eps && r.Y >= -eps && r.Right <= screen.Width + eps && r.Bottom <= screen.Height + eps,
                $"rect {r} inside screen");
            Check(r.Width > 0 && r.Height > 0, "non-empty rect");
        }

        for (int i = 0; i < page.Placements.Count; i++)
        {
            for (int j = i + 1; j < page.Placements.Count; j++)
            {
                LayoutRect a = page.Placements[i].Bounds, b = page.Placements[j].Bounds;
                bool overlap = a.X < b.Right - eps && b.X < a.Right - eps && a.Y < b.Bottom - eps && b.Y < a.Bottom - eps;
                Check(!overlap, "no overlapping photos");
            }
        }

        switch (page.Kind)
        {
            case PageKind.SingleLandscape:
                Check(page.Placements.Count == 1, "landscape pages hold one photo");
                Check(page.Placements[0].Photo.Shape == PhotoShape.Landscape, "landscape page holds a landscape photo");
                LayoutRect l = page.Placements[0].Bounds;
                Check(Math.Abs(l.Width - screen.Width) < eps || Math.Abs(l.Height - screen.Height) < eps,
                    "landscape photo fills the screen in one dimension");
                break;
            case PageKind.SinglePortrait:
                Check(page.Placements.Count == 1, "single portrait page holds one photo");
                Check(portraitTotal <= 1, "a lone portrait only happens when there is nothing to pair it with");
                break;
            case PageKind.PortraitCollage:
                Check(page.Placements.Count is >= 2 && page.Placements.Count <= options.MaxGroupSize,
                    $"collage size {page.Placements.Count} within 2..{options.MaxGroupSize}");
                Check(page.Placements.All(p => p.Photo.Shape == PhotoShape.Portrait), "collages contain only portraits");
                LayoutRect first = page.Placements[0].Bounds;
                Check(page.Placements.All(p => Math.Abs(p.Bounds.Y - first.Y) < eps && Math.Abs(p.Bounds.Height - first.Height) < eps),
                    "collage photos share top edge and height");
                for (int i = 1; i < page.Placements.Count; i++)
                {
                    Check(Math.Abs(page.Placements[i].Bounds.X - page.Placements[i - 1].Bounds.Right - gap) < eps, "equal gaps");
                }

                double left = first.X, right = screen.Width - page.Placements[^1].Bounds.Right;
                double top = first.Y, bottom = screen.Height - first.Bottom;
                Check(Math.Abs(left - right) < eps && Math.Abs(top - bottom) < eps, "collage centred");
                double margin = screen.Height * options.MarginFraction;
                Check(Math.Min(left, top) >= margin - eps, "collage keeps the outer margin");
                Check(Math.Abs(Math.Min(left, top) - margin) < eps, "collage is as large as the margin allows");
                break;
        }
    }
}

int[] GroupSizes(IReadOnlyList<CompositionPage> pages) =>
    pages.Where(p => p.Kind == PageKind.PortraitCollage).Select(p => p.Placements.Count).ToArray();

Console.WriteLine("== Layout engine: synthetic combinations ==");
var empty = RunCase("empty", [], uhd);
Check(empty.Count == 0, "no photos -> no pages");

var allLandscape = RunCase("all landscape", [P(1.5), P(16 / 9.0), P(2.0), P(4 / 3.0)], uhd);
Check(allLandscape.All(p => p.Kind == PageKind.SingleLandscape) && allLandscape.Count == 4, "4 landscape pages");
Check(allLandscape[1].Coverage > 0.999, "16:9 photo fills a 16:9 screen");

var pair = RunCase("2 portraits 3:4", [P(0.75), P(0.75)], uhd);
Check(GroupSizes(pair).SequenceEqual([2]), "two portraits -> one pair");

var four = RunCase("4 portraits 3:4", Enumerable.Range(0, 4).Select(_ => P(0.75)).ToArray(), uhd);
Check(GroupSizes(four).SequenceEqual([2, 2]), "four 3:4 portraits -> two pairs");

var three = RunCase("3 portraits 3:4", [P(0.75), P(0.75), P(0.75)], uhd);
Check(GroupSizes(three).SequenceEqual([3]), "three portraits -> one triple, no leftover");

var five = RunCase("5 portraits 3:4", Enumerable.Range(0, 5).Select(_ => P(0.75)).ToArray(), uhd);
Check(GroupSizes(five).Sum() == 5 && GroupSizes(five).Length == 2, "five portraits -> 2 + 3");

var seven = RunCase("7 portraits", Enumerable.Range(0, 7).Select(_ => P(0.75)).ToArray(), uhd);
Check(GroupSizes(seven).Sum() == 7 && GroupSizes(seven).All(s => s >= 2), "seven portraits all grouped");

var narrow = RunCase("4 narrow 0.46", Enumerable.Range(0, 4).Select(_ => P(0.462)).ToArray(), uhd);
Check(GroupSizes(narrow).SequenceEqual([4]), "narrow portraits -> four per wallpaper");

var mixedNarrow = RunCase("0.75 + 0.46 + 0.75", [P(0.75), P(0.462), P(0.75)], uhd);
Check(mixedNarrow[0].Coverage > 0.8, "mixed triple fills over 80%");

var single = RunCase("single portrait among landscapes", [P(1.5), P(0.75), P(1.5)], uhd);
Check(single.Count == 3 && single[1].Kind == PageKind.SinglePortrait, "lone portrait shown alone, in order");

RunCase("near-square 0.83 + 0.99", [P(0.83), P(0.99)], uhd);
var square = RunCase("exact square", [new PhotoInfo("sq", 1000, 1000)], uhd);
Check(square[0].Kind == PageKind.SingleLandscape, "square goes to the landscape bucket");
RunCase("panorama 4:1", [P(4.0)], uhd);
RunCase("extreme tall 0.25", [P(0.25), P(0.25), P(0.75)], uhd);

var mixed = RunCase("mixed sequence", [P(1.5, "L1"), P(0.75, "P1"), P(0.75, "P2"), P(1.9, "L2"), P(0.66, "P3"),
    P(0.46, "P4"), P(0.75, "P5"), P(1.5, "L3"), P(0.6, "P6")], uhd);
Check(mixed[0].Kind == PageKind.SingleLandscape && mixed[1].Kind == PageKind.PortraitCollage, "pages interleaved by first photo");

var exif = new PhotoInfo("rotated", 4000, 3000, 6);
Check(exif.Shape == PhotoShape.Portrait && Math.Abs(exif.AspectRatio - 0.75) < 1e-9, "EXIF 6 4000x3000 is a 3:4 portrait");
Check(new PhotoInfo("m", 4000, 3000, 2).Shape == PhotoShape.Landscape, "EXIF 2 (mirror) does not rotate");

PhotoInfo[] sample = [P(1.5), P(0.75), P(0.75), P(0.46), P(0.75), P(1.78), P(0.66)];
foreach (ScreenSize screen in new[] { new ScreenSize(1920, 1080), new ScreenSize(1920, 1200),
             new ScreenSize(1024, 768), new ScreenSize(3440, 1440), new ScreenSize(5120, 1440) })
{
    RunCase("sample", sample, screen);
}

var portraitMonitor = RunCase("portrait monitor", sample, new ScreenSize(1080, 1920));
Check(portraitMonitor.Count == sample.Length, "portrait monitor -> one photo per page");

var random = new Random(42);
var large = Enumerable.Range(0, 1000).Select(i => P(random.NextDouble() < 0.6 ? 0.4 + random.NextDouble() * 0.59 : 1 + random.NextDouble() * 1.5, $"r{i}")).ToArray();
var stopwatch = Stopwatch.StartNew();
var largePlan = CollageLayoutEngine.Plan(large, uhd, options);
stopwatch.Stop();
Console.WriteLine($"[1000 random photos] {largePlan.Count} pages in {stopwatch.ElapsedMilliseconds} ms");
Validate(large, largePlan, uhd);
Check(stopwatch.ElapsedMilliseconds < 2000, "planning 1000 photos takes under 2 s");

foreach (int count in Enumerable.Range(2, 40))
{
    var portraits = Enumerable.Range(0, count).Select(i => P(0.4 + random.NextDouble() * 0.59, $"q{i}")).ToArray();
    Validate(portraits, CollageLayoutEngine.Plan(portraits, uhd, options), uhd);
}

Console.WriteLine($"Checked portrait counts 2..41 with random ratios.");

string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "collage-tests");
Directory.CreateDirectory(output);

Console.WriteLine();
Console.WriteLine("== Analyzer + renderer: generated images ==");
var red = Color.FromRgb(255, 0, 0);
var blue = Color.FromRgb(0, 0, 255);
string rotatedJpeg = Path.Combine(output, "exif6.jpg");
WriteTestJpeg(rotatedJpeg, 400, 200, orientation: 6);
PhotoInfo rotatedInfo = PhotoAnalyzer.Analyze(rotatedJpeg);
Check(rotatedInfo.ExifOrientation == 6 && rotatedInfo.Shape == PhotoShape.Portrait, $"analyser reads EXIF orientation (got {rotatedInfo.ExifOrientation})");
var smallScreen = new ScreenSize(400, 400);
BitmapSource rotatedRender = CompositionRenderer.Render(CollageLayoutEngine.Plan([rotatedInfo], smallScreen)[0], smallScreen);
// Stored left half red / right half blue; rotated 90° clockwise, red must end up on top.
Check(IsClose(PixelAt(rotatedRender, 200, 120), red), "EXIF 6 rotated: top is red");
Check(IsClose(PixelAt(rotatedRender, 200, 280), blue), "EXIF 6 rotated: bottom is blue");
Check(PixelAt(rotatedRender, 20, 200) == CompositionRenderer.DefaultBackground, "side bars use the background");

// Direct mode: the prepared BMP must also be upright.
string preparedBmp = Path.Combine(output, "exif6-prepared.bmp");
WallpaperUtility.WallpaperImage.Prepare(rotatedJpeg, preparedBmp);
BitmapSource prepared = BitmapFrame.Create(new Uri(preparedBmp), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
Check(prepared.PixelWidth == 200 && prepared.PixelHeight == 400, $"direct mode rotates EXIF 6 (got {prepared.PixelWidth}x{prepared.PixelHeight})");
Check(IsClose(PixelAt(prepared, 100, 50), red) && IsClose(PixelAt(prepared, 100, 350), blue), "direct mode: red on top, blue at bottom");
string uprightJpeg = Path.Combine(output, "exif1.jpg");
WriteTestJpeg(uprightJpeg, 400, 200, orientation: 1);
string preparedUpright = Path.Combine(output, "exif1-prepared.bmp");
WallpaperUtility.WallpaperImage.Prepare(uprightJpeg, preparedUpright);
prepared = BitmapFrame.Create(new Uri(preparedUpright), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
Check(prepared.PixelWidth == 400 && prepared.PixelHeight == 200 && IsClose(PixelAt(prepared, 50, 100), red),
    "direct mode leaves upright photos unchanged");

string transparentPng = Path.Combine(output, "transparent.png");
var transparent = BitmapSource.Create(100, 200, 96, 96, PixelFormats.Bgra32, null, new byte[100 * 200 * 4], 400);
Save(transparent, transparentPng, new PngBitmapEncoder());
BitmapSource transparentRender = CompositionRenderer.Render(
    CollageLayoutEngine.Plan([PhotoAnalyzer.Analyze(transparentPng)], smallScreen)[0], smallScreen);
Check(PixelAt(transparentRender, 200, 200) == CompositionRenderer.DefaultBackground, "transparent pixels show the background");

var bad = Path.Combine(output, "corrupt.jpg");
File.WriteAllText(bad, "not an image");
var analysis = PhotoAnalyzer.AnalyzeAll([rotatedJpeg, bad, transparentPng]);
Check(analysis.Photos.Count == 2 && analysis.Skipped.Count == 1, "corrupt files are reported as skipped");

if (args.Length > 0 && Directory.Exists(args[0]))
{
    Console.WriteLine();
    Console.WriteLine($"== Real folder: {args[0]} ==");
    string[] files = Directory.GetFiles(args[0])
        .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp")
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    var real = PhotoAnalyzer.AnalyzeAll(files);
    foreach (PhotoInfo photo in real.Photos)
    {
        Console.WriteLine($"  {Path.GetFileName(photo.Path),-22} {photo.PixelWidth,5}x{photo.PixelHeight,-5} exif {photo.ExifOrientation} -> {photo.DisplayWidth,5}x{photo.DisplayHeight,-5} {photo.AspectRatio:0.000} {photo.Shape}");
    }

    Console.WriteLine($"  {real.Photos.Count(p => p.Shape == PhotoShape.Landscape)} landscape, " +
                      $"{real.Photos.Count(p => p.Shape == PhotoShape.Portrait)} portrait, {real.Skipped.Count} skipped");
    var realPages = RunCase("real folder", real.Photos, uhd);
    RenderAll("folder", realPages, uhd);

    // Direct mode must produce upright BMPs for every EXIF-rotated photo.
    foreach (PhotoInfo photo in real.Photos.Where(p => p.ExifOrientation != 1))
    {
        string bmp = Path.Combine(output, "direct-" + Path.GetFileNameWithoutExtension(photo.Path) + ".bmp");
        WallpaperUtility.WallpaperImage.Prepare(photo.Path, bmp);
        var frame = BitmapDecoder.Create(new Uri(bmp), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad).Frames[0];
        Check(frame.PixelWidth == photo.DisplayWidth && frame.PixelHeight == photo.DisplayHeight,
            $"direct mode upright: {Path.GetFileName(photo.Path)} {frame.PixelWidth}x{frame.PixelHeight}");
        if (photo.ExifOrientation == 8)
        {
            Save(Downscale(frame, 300), Path.ChangeExtension(bmp, ".png"), new PngBitmapEncoder());
        }

        File.Delete(bmp);
    }

    Console.WriteLine($"  direct mode checked {real.Photos.Count(p => p.ExifOrientation != 1)} EXIF-rotated photo(s)");

    PhotoInfo[] portraits = real.Photos.Where(p => p.Shape == PhotoShape.Portrait).ToArray();
    PhotoInfo[] landscapes = real.Photos.Where(p => p.Shape == PhotoShape.Landscape).ToArray();
    PhotoInfo Find(string name) => real.Photos.First(p => Path.GetFileName(p.Path).StartsWith(name, StringComparison.OrdinalIgnoreCase));
    var combos = new Dictionary<string, PhotoInfo[]>
    {
        ["combo-1-landscape-wide"] = [Find("20260407")],
        ["combo-2-portraits-exif"] = [Find("20240510"), Find("20241212")],
        ["combo-3-portraits"] = portraits.Take(3).ToArray(),
        ["combo-narrow-mix"] = [Find("20250315"), Find("20250317"), Find("20250622")],
        ["combo-1-portrait-1-landscape"] = [Find("9N8A3236"), Find("9N8A3413")],
        ["combo-1920x1080-mixed"] = [.. landscapes.Take(1), .. portraits.Take(5)],
    };
    foreach ((string name, PhotoInfo[] photos) in combos)
    {
        ScreenSize screen = name.Contains("1920") ? new ScreenSize(1920, 1080) : uhd;
        RenderAll(name, RunCase(name, photos, screen), screen);
    }
}

Console.WriteLine();
Console.WriteLine($"{checks} checks, {failures} failures. Output: {output}");
return failures == 0 ? 0 : 1;

void RenderAll(string prefix, IReadOnlyList<CompositionPage> pages, ScreenSize screen)
{
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < pages.Count; i++)
    {
        BitmapSource bitmap = CompositionRenderer.Render(pages[i], screen);
        Check(bitmap.PixelWidth == screen.Width && bitmap.PixelHeight == screen.Height, "render matches screen size");
        Check(PixelAt(bitmap, 0, 0) == CompositionRenderer.DefaultBackground || pages[i].Coverage > 0.999,
            "corner shows background");
        foreach (Placement placement in pages[i].Placements)
        {
            LayoutRect r = placement.Bounds;
            Check(PixelAt(bitmap, (int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)) != CompositionRenderer.DefaultBackground,
                $"{Path.GetFileName(placement.Photo.Path)} drawn");
        }

        string names = string.Join(", ", pages[i].Placements.Select(p => Path.GetFileName(p.Photo.Path)));
        string file = Path.Combine(output, $"{prefix}-{i + 1:00}.png");
        Save(Downscale(bitmap, 960), file, new PngBitmapEncoder());
        Console.WriteLine($"    {Path.GetFileName(file)}: {pages[i].Kind} {pages[i].Coverage:P0} [{names}]");
    }

    Console.WriteLine($"  rendered {pages.Count} page(s) in {stopwatch.ElapsedMilliseconds} ms");
}

static BitmapSource Downscale(BitmapSource source, int width) =>
    new TransformedBitmap(source, new ScaleTransform((double)width / source.PixelWidth, (double)width / source.PixelWidth));

static Color PixelAt(BitmapSource bitmap, int x, int y)
{
    var pixel = new byte[4];
    new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
    return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
}

static bool IsClose(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 60;

static void Save(BitmapSource bitmap, string path, BitmapEncoder encoder)
{
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static void WriteTestJpeg(string path, int width, int height, ushort orientation)
{
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bool left = x < width / 2;
            pixels[i] = (byte)(left ? 0 : 255);
            pixels[i + 2] = (byte)(left ? 255 : 0);
            pixels[i + 3] = 255;
        }
    }

    var metadata = new BitmapMetadata("jpg");
    metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
    encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4),
        null, metadata, null));
    using var stream = File.Create(path);
    encoder.Save(stream);
}
