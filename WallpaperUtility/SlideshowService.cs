using System.IO;

namespace WallpaperUtility;

internal sealed class SlideshowService(WallpaperService wallpaperService)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static string[] GetImageFiles(string folderPath) =>
        Directory.GetFiles(folderPath)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task RunAsync(string folderPath, bool composeCollages, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (composeCollages)
        {
            progress.Report("Analysing photos and planning collages...");
            CollageSet collages = await Task.Run(() => CollageSet.Create(folderPath, cancellationToken), cancellationToken);
            string unreadable = collages.Analysis.Skipped.Count == 0 ? "" :
                $" Skipped {collages.Analysis.Skipped.Count} unreadable image(s).";
            Slide[] pages = collages.Pages.Select((page, index) => new Slide(CollageSet.Describe(page),
                token => wallpaperService.Apply(
                    preparedPath => WallpaperImage.Save(collages.Render(index, token), preparedPath), token))).ToArray();
            await RotateAsync(pages, unreadable, progress, cancellationToken);
            return;
        }

        string[] images = await Task.Run(() => GetImageFiles(folderPath), cancellationToken);
        if (images.Length == 0)
        {
            throw new InvalidOperationException("The folder contains no JPEG, PNG, or BMP images.");
        }

        Slide[] slides = images.Select(path => new Slide(Path.GetFileName(path),
            token => wallpaperService.Apply(path, token))).ToArray();
        await RotateAsync(slides, "", progress, cancellationToken);
    }

    private static async Task RotateAsync(Slide[] slides, string warning, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        int skipped = 0;
        for (int index = 0; ; index = (index + 1) % slides.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Slide slide = slides[index];
            try
            {
                await Task.Run(() => slide.Apply(cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (WallpaperImage.IsInputError(ex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                skipped++;
                consecutiveFailures++;
                warning = $" Skipped {skipped} image(s). Last error: {slide.Name}: {ex.Message}";
                progress.Report(warning.TrimStart());
                if (consecutiveFailures == slides.Length)
                {
                    throw new InvalidOperationException($"No images could be applied.{warning}", ex);
                }

                continue;
            }

            consecutiveFailures = 0;
            progress.Report($"Showing {index + 1} of {slides.Length}: {slide.Name}. " +
                            $"Next image in 30 seconds.{warning}");
            await Task.Delay(Interval, cancellationToken);
        }
    }

    private sealed record Slide(string Name, Action<CancellationToken> Apply);
}
