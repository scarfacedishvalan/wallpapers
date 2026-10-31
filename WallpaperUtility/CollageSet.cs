using System.IO;
using System.Windows.Media.Imaging;
using WallpaperUtility.Composition;

namespace WallpaperUtility;

/// <summary>Analyses a folder and plans collage wallpapers for the primary monitor.</summary>
internal sealed record CollageSet(ScreenSize Screen, AnalysisResult Analysis, IReadOnlyList<CompositionPage> Pages)
{
    public static CollageSet Create(string folderPath, CancellationToken cancellationToken = default)
    {
        string[] files = SlideshowService.GetImageFiles(folderPath);
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The folder contains no JPEG, PNG, or BMP images.");
        }

        AnalysisResult analysis = PhotoAnalyzer.AnalyzeAll(files, cancellationToken);
        if (analysis.Photos.Count == 0)
        {
            throw new InvalidOperationException(
                $"None of the {files.Length} image(s) could be read. First error: {analysis.Skipped[0].Error}");
        }

        ScreenSize screen = PrimaryDisplay.GetResolution();
        return new CollageSet(screen, analysis, CollageLayoutEngine.Plan(analysis.Photos, screen));
    }

    public string Summary
    {
        get
        {
            int landscape = Analysis.Photos.Count(p => p.Shape == PhotoShape.Landscape);
            int collages = Pages.Count(p => p.Kind == PageKind.PortraitCollage);
            string skipped = Analysis.Skipped.Count == 0 ? "" :
                $" Skipped {Analysis.Skipped.Count} unreadable image(s), e.g. " +
                $"{Path.GetFileName(Analysis.Skipped[0].Path)}: {Analysis.Skipped[0].Error}";
            return $"Screen {Screen}. {Analysis.Photos.Count} photo(s): {landscape} landscape, " +
                   $"{Analysis.Photos.Count - landscape} portrait → {Pages.Count} wallpaper(s), " +
                   $"{collages} of them collages.{skipped}";
        }
    }

    public static string Describe(CompositionPage page)
    {
        string kind = page.Kind switch
        {
            PageKind.PortraitCollage => $"Collage of {page.Placements.Count} portraits",
            PageKind.SinglePortrait => "Single portrait",
            _ => "Single landscape"
        };
        return $"{kind}: {string.Join(", ", page.Placements.Select(p => Path.GetFileName(p.Photo.Path)))}";
    }

    public BitmapSource Render(int index, CancellationToken cancellationToken = default) =>
        CompositionRenderer.Render(Pages[index], Screen, cancellationToken: cancellationToken);
}
