using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WallpaperUtility;

/// <summary>Shows each generated collage wallpaper at native resolution before it is applied.</summary>
public partial class PreviewWindow : Window
{
    private readonly WallpaperService wallpaperService;
    private readonly string folderPath;
    private readonly CancellationTokenSource cancellation = new();
    private CollageSet? collages;
    private BitmapSource? rendered;
    private int pageIndex;
    private int renderVersion;
    private bool busy = true;

    public PreviewWindow(WallpaperService wallpaperService, string folderPath)
    {
        InitializeComponent();
        this.wallpaperService = wallpaperService;
        this.folderPath = folderPath;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            collages = await Task.Run(() => CollageSet.Create(folderPath, cancellation.Token));
            SummaryText.Text = collages.Summary;
            busy = false;
            await ShowPageAsync(0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex) || ex is Win32Exception)
        {
            SummaryText.Text = $"Could not plan collages: {ex.Message}";
            busy = false;
        }
    }

    private async Task ShowPageAsync(int index)
    {
        if (collages is null)
        {
            return;
        }

        pageIndex = index;
        int version = ++renderVersion;
        rendered = null;
        PageText.Text = $"Wallpaper {index + 1} of {collages.Pages.Count} — " +
                        $"{CollageSet.Describe(collages.Pages[index])} (rendering...)";
        UpdateControls();
        try
        {
            BitmapSource bitmap = await Task.Run(() => collages.Render(index, cancellation.Token));
            if (version != renderVersion)
            {
                return;
            }

            rendered = bitmap;
            PreviewImage.Source = bitmap;
            PageText.Text = $"Wallpaper {index + 1} of {collages.Pages.Count} — " +
                            $"{CollageSet.Describe(collages.Pages[index])}. " +
                            $"{collages.Pages[index].Coverage:P0} of the screen covered.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex))
        {
            if (version == renderVersion)
            {
                PreviewImage.Source = null;
                PageText.Text = $"Wallpaper {index + 1} could not be rendered: {ex.Message}";
            }
        }

        UpdateControls();
    }

    private async void PreviousClicked(object sender, RoutedEventArgs e) =>
        await ShowPageAsync((pageIndex - 1 + collages!.Pages.Count) % collages.Pages.Count);

    private async void NextClicked(object sender, RoutedEventArgs e) =>
        await ShowPageAsync((pageIndex + 1) % collages!.Pages.Count);

    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        BitmapSource? bitmap = rendered;
        if (bitmap is null)
        {
            return;
        }

        busy = true;
        UpdateControls();
        string description = PageText.Text;
        PageText.Text = "Applying wallpaper...";
        try
        {
            await Task.Run(() => wallpaperService.Apply(path => WallpaperImage.Save(bitmap, path)));
            PageText.Text = $"Applied. {description}";
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex) || ex is Win32Exception)
        {
            PageText.Text = $"Could not apply wallpaper: {ex.Message}";
        }
        finally
        {
            busy = false;
            UpdateControls();
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy && collages is not null)
        {
            // Never interrupt an in-flight wallpaper change.
            e.Cancel = true;
            PageText.Text = "Finishing the current wallpaper change before closing is allowed...";
            return;
        }

        cancellation.Cancel();
    }

    private void UpdateControls()
    {
        bool navigable = !busy && collages is { Pages.Count: > 1 };
        PreviousButton.IsEnabled = navigable;
        NextButton.IsEnabled = navigable;
        ApplyButton.IsEnabled = !busy && rendered is not null;
    }
}
