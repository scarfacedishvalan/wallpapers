using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WallpaperUtility;

public partial class MainWindow : Window
{
    private const int BackgroundStartAttempts = 5;
    private static readonly TimeSpan BackgroundRetryDelay = TimeSpan.FromMinutes(1);
    private readonly WallpaperService wallpaperService = new();
    private bool isApplying;
    private bool closeRequested;
    private bool exitRequested;
    private bool isClosed;
    private bool updatingStartupBox;
    private int slideshowRuns;
    private CancellationTokenSource? slideshowCancellation;
    private Forms.NotifyIcon? trayIcon;

    public MainWindow(bool background)
    {
        InitializeComponent();
        bool settingsLoaded = false;
        try
        {
            SlideshowSettings settings = SlideshowSettings.Load();
            FolderPathBox.Text = settings.FolderPath;
            CollageModeBox.IsChecked = settings.ComposeCollages;
            settingsLoaded = true;
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex) || ex is JsonException)
        {
            StatusText.Text = $"Could not load appsettings.json: {ex.Message} " +
                              "Correct the file and restart, or choose a folder here.";
        }

        RefreshStartupBox(reportMismatch: settingsLoaded);
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(StatusText, (_, _) => UpdateTrayText());
        UpdateControls();

        if (background)
        {
            if (settingsLoaded)
            {
                EnsureTrayIcon();
                Dispatcher.BeginInvoke(RunBackgroundSlideshow);
            }
            else
            {
                // Nothing can run without valid settings, so show the error instead of hiding.
                Dispatcher.BeginInvoke(ShowFromBackground);
            }
        }
    }

    public void ShowFromBackground()
    {
        if (isClosed)
        {
            return;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ImagePathChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateControls();
            StatusText.Text = "Choose an image and click Apply wallpaper.";
        }
    }

    private void FolderPathChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateControls();
        }
    }

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose a wallpaper",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(this) == true)
        {
            ImagePathBox.Text = picker.FileName;
        }
    }

    private void FolderBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose a slideshow folder", Multiselect = false };
        if (picker.ShowDialog(this) == true)
        {
            FolderPathBox.Text = picker.FolderName;
        }
    }

    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        string path = ImagePathBox.Text.Trim().Trim('"');
        isApplying = true;
        UpdateControls();
        StatusText.Text = "Applying wallpaper...";

        try
        {
            await Task.Run(() => wallpaperService.Apply(path));
            StatusText.Text = "Wallpaper applied successfully.";
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex) || ex is Win32Exception)
        {
            StatusText.Text = $"Could not apply wallpaper: {ex.Message}";
        }
        finally
        {
            isApplying = false;
            FinishOperation();
        }
    }

    private async void StartClicked(object sender, RoutedEventArgs e) => await StartSlideshowAsync();

    /// <summary>Runs the slideshow until stopped. Returns true if it ended because of an error.</summary>
    private async Task<bool> StartSlideshowAsync()
    {
        string folderPath = FolderPathBox.Text.Trim().Trim('"');
        bool composeCollages = CollageModeBox.IsChecked == true;
        using var cancellation = new CancellationTokenSource();
        slideshowCancellation = cancellation;
        slideshowRuns++;
        UpdateControls();
        StatusText.Text = "Starting slideshow...";
        var progress = new Progress<string>(message =>
        {
            if (slideshowCancellation == cancellation && !cancellation.IsCancellationRequested)
            {
                StatusText.Text = message;
            }
        });

        try
        {
            await new SlideshowService(wallpaperService).RunAsync(folderPath, composeCollages, progress, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText.Text = "Slideshow stopped. The last wallpaper remains applied.";
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex) || ex is Win32Exception)
        {
            StatusText.Text = $"Slideshow stopped: {ex.Message}";
            if (trayIcon is not null && !IsVisible)
            {
                trayIcon.ShowBalloonTip(10000, "Wallpaper Utility", StatusText.Text, Forms.ToolTipIcon.Warning);
            }

            return true;
        }
        finally
        {
            slideshowCancellation = null;
            FinishOperation();
        }

        return false;
    }

    /// <summary>
    /// Started with --background (e.g. at sign-in). Photos may not be readable yet while OneDrive
    /// starts, so a failed start is retried a few times unless the user takes over.
    /// </summary>
    private async void RunBackgroundSlideshow()
    {
        for (int attempt = 1; ; attempt++)
        {
            bool failed = await StartSlideshowAsync();
            if (!failed || attempt == BackgroundStartAttempts || closeRequested || isClosed)
            {
                return;
            }

            int run = slideshowRuns;
            StatusText.Text += $" Retrying in 1 minute (attempt {attempt + 1} of {BackgroundStartAttempts}).";
            await Task.Delay(BackgroundRetryDelay);
            if (run != slideshowRuns || isApplying || closeRequested || isClosed)
            {
                return;
            }
        }
    }

    private void PreviewClicked(object sender, RoutedEventArgs e)
    {
        string folderPath = FolderPathBox.Text.Trim().Trim('"');
        // Modal, so a preview apply can never overlap a slideshow or manual apply.
        new PreviewWindow(wallpaperService, folderPath) { Owner = this }.ShowDialog();
    }

    private void BackgroundClicked(object sender, RoutedEventArgs e)
    {
        EnsureTrayIcon();
        Hide();
        trayIcon!.ShowBalloonTip(5000, "Wallpaper Utility",
            "Still running in the background. Use this icon to open or exit.", Forms.ToolTipIcon.Info);
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (updatingStartupBox)
        {
            return;
        }

        try
        {
            if (StartupBox.IsChecked == true)
            {
                StartupRegistration.Enable();
                StatusText.Text = "Wallpaper Utility will start in the background when you sign in to Windows.";
            }
            else
            {
                StartupRegistration.Disable();
                StatusText.Text = "Wallpaper Utility will no longer start with Windows.";
            }
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex))
        {
            StatusText.Text = $"Could not change the startup setting: {ex.Message}";
            RefreshStartupBox(reportMismatch: false);
        }
    }

    private void RefreshStartupBox(bool reportMismatch)
    {
        updatingStartupBox = true;
        try
        {
            string? registered = StartupRegistration.RegisteredCommand();
            StartupBox.IsChecked = registered is not null;
            if (reportMismatch && registered is not null
                && !string.Equals(registered, StartupRegistration.Command, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = $"Start with Windows currently launches {registered}. " +
                                  "Untick and tick the option to use this copy instead.";
            }
        }
        catch (Exception ex) when (WallpaperImage.IsInputError(ex))
        {
            StartupBox.IsEnabled = false;
            StartupBox.ToolTip = $"The startup setting could not be read: {ex.Message}";
        }
        finally
        {
            updatingStartupBox = false;
        }
    }

    private void EnsureTrayIcon()
    {
        if (trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Wallpaper Utility", null, (_, _) => ShowFromBackground());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());
        trayIcon = new Forms.NotifyIcon
        {
            Icon = (Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null)
                   ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowFromBackground();
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (trayIcon is not null)
        {
            string text = $"Wallpaper Utility: {StatusText.Text}";
            trayIcon.Text = text.Length > 127 ? text[..124] + "..." : text;
        }
    }

    private void ExitFromTray()
    {
        exitRequested = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        isClosed = true;
        trayIcon?.Dispose();
        trayIcon = null;
        base.OnClosed(e);
    }

    private void StopClicked(object sender, RoutedEventArgs e)
    {
        slideshowCancellation?.Cancel();
        StatusText.Text = "Stopping slideshow...";
        UpdateControls();
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (trayIcon is not null && !exitRequested)
        {
            // While in the tray, the close button only hides the window; Exit is in the tray menu.
            e.Cancel = true;
            Hide();
            return;
        }

        if (isApplying || slideshowCancellation is not null)
        {
            e.Cancel = true;
            closeRequested = true;
            slideshowCancellation?.Cancel();
            StatusText.Text = "Finishing the current operation before closing...";
            UpdateControls();
        }
    }

    private void FinishOperation()
    {
        UpdateControls();
        if (closeRequested)
        {
            Close();
        }
    }

    private void UpdateControls()
    {
        bool idle = !isApplying && slideshowCancellation is null && !closeRequested;
        ImagePathBox.IsEnabled = idle;
        BrowseButton.IsEnabled = idle;
        FolderPathBox.IsEnabled = idle;
        FolderBrowseButton.IsEnabled = idle;
        ApplyButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(ImagePathBox.Text);
        StartButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(FolderPathBox.Text);
        CollageModeBox.IsEnabled = idle;
        PreviewButton.IsEnabled = idle && !string.IsNullOrWhiteSpace(FolderPathBox.Text);
        StopButton.IsEnabled = slideshowCancellation is { IsCancellationRequested: false } && !closeRequested;
    }
}