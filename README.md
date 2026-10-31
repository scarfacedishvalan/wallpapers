# Wallpaper Utility

A minimal Windows desktop app for setting a single wallpaper or cycling through
a folder of pictures every 30 seconds. Supports JPEG, PNG, and BMP. No services,
databases, third-party packages, or administrator privileges are required to run it.

All images use Windows **Fit** placement: the entire photo is scaled proportionally
and centered without cropping, stretching, or tiling. Borders use the existing
Windows background color when the photo and screen have different aspect ratios.

An optional **collage mode** composes wallpapers at the primary monitor's native
resolution: landscape photos are shown one at a time, and portrait photos are
placed side by side (2–4 per wallpaper) on a neutral background. See
[Collage mode](#collage-mode) and the design notes in [COLLAGE_PLAN.md](COLLAGE_PLAN.md).

## Requirements

- Windows 10 or Windows 11 with an interactive desktop.
- .NET 10 SDK (Windows x64 for this machine).
- Windows wallpaper changes must not be blocked by organizational policy.

Install the SDK if needed:

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget --architecture x64
```

Restart your terminal after installation, then verify:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
dotnet new list wpf
```

Expect a 10.0 SDK, `Microsoft.WindowsDesktop.App 10.0.x`, and the WPF template.
Full Visual Studio and extra .NET workloads are not required.

## Build and launch

From this directory:

```powershell
dotnet build .\WallpaperUtility\WallpaperUtility.csproj
dotnet run --project .\WallpaperUtility\WallpaperUtility.csproj --no-build
```

Alternatively, after building, launch:

```powershell
.\WallpaperUtility\bin\Debug\net10.0-windows\WallpaperUtility.exe
```

Close the app before rebuilding it.

## Configure the slideshow folder

Edit [WallpaperUtility/appsettings.json](WallpaperUtility/appsettings.json):

```json
{
  "FolderPath": "C:\\Users\\abhir\\OneDrive\\Pictures\\pics",
  "ComposeCollages": true
}
```

Use an absolute folder path. Backslashes must be doubled inside JSON strings.
`FolderPath` is required; malformed JSON, unknown properties, and empty or relative
paths produce a visible settings error rather than silently using another folder.
You can still use the manual image controls or choose a folder in the UI after a
settings error.

`ComposeCollages` is optional (default `false`). When `true`, the **Compose collages**
option starts ticked so the slideshow rotates collages; you can still untick it
for a single run.

After editing this source file, close the app and run:

```powershell
dotnet run --project .\WallpaperUtility\WallpaperUtility.csproj
```

This builds as needed and copies the JSON next to the executable. Settings are
loaded from that copy at startup, not from the terminal's working directory.
Do not use `--no-build` immediately after editing the source JSON, because it
would continue using the previously copied settings.

If running a copied or published executable, edit the `appsettings.json` beside
that executable and restart it. No C# changes are needed.

## Run a slideshow

1. Launch the app. **Slideshow folder** is populated from the JSON.
2. Optionally type a different folder or use **Choose folder...**. Changes made in
   the UI apply only to this run; they do not overwrite the JSON.
3. Click **Start slideshow**. The first image is applied immediately.
4. Leave the app open or minimized. Each successfully applied image stays up for
   30 seconds before the next image is prepared and applied. Loading a large or
   online-only OneDrive image may add a little time to the transition.
5. Click **Stop** to end rotation, or close the app. The last wallpaper stays
   applied; the app does not restore your previous wallpaper automatically.

Images are processed in case-insensitive filename order, then the list repeats.
Only JPEG (`.jpg`/`.jpeg`), PNG, and BMP files directly inside the folder are included;
subfolders and other formats are ignored. The list is captured when Start is
clicked. Stop and start again to include newly added images. A restart of the
slideshow begins at the first image, not the last position.

The status shows the filename and position, such as **Showing 4 of 25**.
Unreadable or corrupt images are skipped with a visible warning. The slideshow
stops with an error if no images can be applied, the folder is unavailable/empty,
or Windows rejects a wallpaper operation.

Manual Apply and folder editing are disabled during rotation to prevent
overlapping changes. Stop and close cancel the next transition and wait for any
in-flight image preparation/native call to finish; no rotation process remains
after the app exits. An already committed native change cannot be undone by Stop.
The app does not auto-start a slideshow on a normal launch. For background running
and starting with Windows, see the next section.

## Run in the background and start with Windows

- **Start with Windows**: when ticked, the app adds a per-user entry
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WallpaperUtility` containing
  `"<path to this exe>" --background`. No administrator rights are needed. Untick it
  to remove the entry. It also appears under **Settings > Apps > Startup**, where
  Windows can turn it off as well.
- **`--background`**: starts with no window. The slideshow starts right away using
  `appsettings.json`, including `ComposeCollages`. A notification-area (tray) icon
  shows the current status as a tooltip. Double-click the icon or choose **Open** to
  show the window, or choose **Exit** to stop. On Windows 11 the icon may be under
  the **^** hidden-icons arrow. You can drag it onto the taskbar.
- If a background start fails, for example because OneDrive is not ready yet at
  sign-in, the app shows a notification and retries every minute, up to 5 attempts.
  Retries stop if you start or stop the slideshow yourself.
- **Run in background** hides a window you opened yourself. The slideshow keeps
  running, and the tray icon appears.
- While the tray icon exists, the window's close button only hides the window. Use
  **Exit** in the tray menu to quit. If the settings file is invalid, a background
  launch shows the window with the error instead of hiding.
- Launching the exe again while it is running opens the existing window instead of
  starting a second slideshow.

The startup entry stores the full path of the exe that registered it. If you move
or rebuild to a different folder, open that copy, then untick and tick **Start with
Windows**. The app warns at launch when the entry points to a different copy.
Close the app (tray **Exit**) before rebuilding, because a running copy locks the exe.

For OneDrive folders, **Always keep on this device** is recommended if you want
predictable transitions without waiting for downloads.

## Collage mode

1. **Preview collages...** analyses the slideshow folder and opens a preview window.
   The summary shows the monitor resolution, how many photos are landscape or
   portrait, how many wallpapers will be generated, and any unreadable files.
2. Use **Previous**/**Next** to inspect each wallpaper, rendered at full native
   resolution. **Apply this wallpaper** applies the one shown. The window is modal,
   so it never overlaps a slideshow or manual apply.
3. To rotate collages, tick **Compose collages** and click **Start slideshow**
   (set `"ComposeCollages": true` in the JSON to have it ticked at startup).
   Each generated wallpaper stays up for 30 seconds, like the normal slideshow.
   With the box unticked the slideshow behaves exactly as before.

How photos are bucketed and laid out:

- The size used is the **displayed** size, after the EXIF orientation is applied.
  Phone photos stored as 4000×3000 with a "rotate 90°" tag are portrait.
- Width ≥ height → **landscape** (one per wallpaper, fit to the screen).
  Width < height → **portrait** (grouped). Every readable photo is in exactly one bucket.
- Portraits are kept in filename order and split into groups of 2–4. All photos in a
  group share the same height, are evenly spaced, and are centred with a margin.
  The split chosen gives the highest average screen coverage, so regular 3:4 photos
  usually pair up and tall, narrow photos go three or four to a wallpaper. There is
  never a leftover single, unless the folder has only one portrait.
- Photos are never cropped or stretched. Remaining space is dark grey (`#1E1E1E`).
- On a portrait-oriented monitor every photo is shown on its own.

The generated bitmap matches the primary monitor's resolution exactly and uses the
same two-slot cache and Fit placement as the direct mode. With several monitors,
the others show the same image scaled. The plan is created when Start or Preview is
clicked; restart after changing resolution or the folder contents.

The collage component lives in [WallpaperUtility/Composition](WallpaperUtility/Composition)
and contains no Windows wallpaper code:

- [PhotoInfo](WallpaperUtility/Composition/PhotoInfo.cs) handles displayed size, aspect ratio, and bucket.
- [PhotoAnalyzer](WallpaperUtility/Composition/PhotoAnalyzer.cs) reads the size and EXIF
  orientation from file headers, without decoding pixels.
- [CollageLayoutEngine](WallpaperUtility/Composition/CollageLayoutEngine.cs) is pure
  layout. It takes any photo list and a screen size and returns pages of rectangles.
- [CompositionRenderer](WallpaperUtility/Composition/CompositionRenderer.cs) draws a page
  into a native-resolution bitmap. Each photo is decoded at its target size, and EXIF
  rotation and transparency are handled.

[CollageSet](WallpaperUtility/CollageSet.cs) connects these to the folder and the
[PrimaryDisplay](WallpaperUtility/PrimaryDisplay.cs) resolution.
[WallpaperService](WallpaperUtility/WallpaperService.cs) applies the result.

### Composition tests

[WallpaperUtility.CompositionTests](WallpaperUtility.CompositionTests) is a console
harness with no packages. It compiles the composition sources directly and runs
these checks:

- Synthetic layouts: all landscape, even/odd/single portraits, narrow, square, panorama,
  mixed sequences, 16:9/16:10/4:3/21:9/32:9 and portrait monitors, and 1000 photos.
  The checks confirm that every photo appears once, aspect ratios are preserved,
  there are no overlaps, gaps are equal, and layouts are centred.
- EXIF-rotation and transparency rendering on generated images, plus corrupt-file
  reporting. Direct-mode `Prepare` output is checked to be upright.
- With a folder argument, the harness analyses that folder and saves downscaled PNG
  previews of each wallpaper and of several hand-picked combinations. It also runs
  every EXIF-rotated photo through the direct-mode path.

```powershell
dotnet run --project .\WallpaperUtility.CompositionTests -c Release -- "C:\path\to\photos" "C:\path\to\output"
```

The exit code is 0 when all checks pass.

## How to test a single image

Applying an image changes your real Windows wallpaper and persists after the app
closes. Note your original wallpaper first; restore it through Windows
**Settings > Personalization > Background** or apply the original image again.

1. Launch the app. **Apply wallpaper** should initially be disabled.
2. Paste the full path of a recognizable JPEG, PNG, or BMP. Paths with spaces,
   Unicode characters, and surrounding double quotes are supported.
3. Click **Apply wallpaper**. Expect **Wallpaper applied successfully.**
4. Minimize other windows or press **Win+D** and visually confirm that the desktop
   actually shows the selected image. The success message alone is not a visual
   verification.
5. Apply a different image using **Browse...**. Confirm that the desktop changes
   again. Repeat with each supported format.
6. Open **Browse...**, then cancel. The existing path and wallpaper should remain
   unchanged.
7. Enter a nonexistent path and click Apply. Expect an explanatory error, no
   wallpaper change, and controls that remain usable.
8. Create a text file with a `.png` extension and try applying it. Expect an error,
   not a success message. Repeat with an image deleted after it was selected.
9. After an error, apply a valid image to verify recovery.
10. Close the app. Confirm that the new wallpaper remains. The source image can
    be moved or removed after a successful apply because the app keeps its own copy.

## How to test the slideshow

Use a small test folder with two or three distinct images for quick loop testing.
Note your previous wallpaper first, as the tests change your real desktop.

1. Set `FolderPath` in the JSON, run the app with the command above, and confirm
   the folder appears in the UI.
2. Start the slideshow. Confirm the first image appears immediately and the status
   identifies it. Manual Apply and another Start should be disabled.
3. Minimize the app and observe the next image after approximately 30 seconds.
   Wait for the list to wrap around; two valid images wrap in about one minute.
4. Click Stop, wait more than 30 seconds, and confirm the wallpaper does not change.
   Manual Apply should work again.
5. Start again, close the app, and wait more than 30 seconds. Confirm the last
   wallpaper remains and no further rotation occurs.
6. Try an empty folder and a nonexistent folder. Both should show an error and
   leave the wallpaper unchanged.
7. Add a text file renamed to `.jpg` alongside valid pictures. Restart the
   slideshow. It should skip the corrupt file, display a warning, and continue
   with valid images. A folder containing only corrupt images should stop with
   an error instead of retrying forever.
8. Add or remove images while stopped, then restart. Confirm the list is refreshed.
9. Run several cycles and restart the app. In
   `%LOCALAPPDATA%\WallpaperUtility\Wallpapers`, confirm that only
   `current-1.bmp` and `current-2.bmp` are created/reused for new wallpapers,
   rather than a new BMP every 30 seconds.
10. Temporarily break the JSON syntax and restart. Expect a visible configuration
    error. Correct the JSON, rebuild/run, and confirm recovery.

## Implementation and scope

- [MainWindow](WallpaperUtility/MainWindow.xaml.cs) handles the picker, typed path,
  Start/Stop, busy state, shutdown, and visible success/error feedback.
- [SlideshowSettings](WallpaperUtility/SlideshowSettings.cs) reads and validates
  the JSON folder setting.
- [SlideshowService](WallpaperUtility/SlideshowService.cs) owns the folder snapshot,
  sequential 30-second loop, cancellation, and skipped-image reporting.
- [WallpaperImage](WallpaperUtility/WallpaperImage.cs) decodes and validates the
  image using WPF and creates a BMP copy for predictable native API compatibility.
- [WallpaperService](WallpaperUtility/WallpaperService.cs) calls Unicode
  `SystemParametersInfoW` with `SPI_SETDESKWALLPAPER`, `SPIF_UPDATEINIFILE`, and
  `SPIF_SENDCHANGE`. Before refreshing the wallpaper, it sets the current user's
  Windows placement settings to Fit (`WallpaperStyle=6`, `TileWallpaper=0`).
  It checks the native result, reports Windows errors, and attempts to restore
  the previous placement settings if applying the wallpaper fails.

Processing runs off the UI thread. A successful message means Windows accepted
the API request; verify the visible result using the steps above, especially on
managed machines or desktops using slideshows, themes, or other wallpaper apps.

Prepared BMPs are stored in `%LOCALAPPDATA%\WallpaperUtility\Wallpapers`.
The app queries the active wallpaper and writes to the other of two reusable
files (`current-1.bmp` and `current-2.bmp`), so it does not overwrite the active
image while preparing the next one. This bounds new cache usage across manual
changes, slideshow cycles, and app restarts. A small `cache.lock` file protects
against concurrent cache writes. Only one copy of the app runs at a time:
launching it again opens the window of the copy that is already running.

Older `wallpaper-<guid>.bmp` files created by Phase 1 are not deleted automatically.
To reclaim that old space, first apply an image using this version, then remove
those old GUID-named BMPs. Do not delete the active cached file. To clear the
entire cache, close the app and switch to a wallpaper outside this cache using
Windows Settings before deleting the cached files.

The app explicitly selects **Fit** on every manual apply and slideshow transition,
even if Windows was previously set to Tile or Fill. This Windows setting persists
after the app closes. Portrait photos normally have borders on the left and right
on a landscape monitor; this is intentional so no part of the photo is cut off.
Transparent images may display differently from their appearance over a
checkerboard in an editor. Separate per-monitor wallpapers and shuffle are not
implemented. Both modes honour the EXIF
orientation, so phone photos stored sideways with a rotation tag display upright.

## Verification performed

Built and tested on Windows 11 build 26200 with .NET SDK 10.0.403:

- Debug build completed with zero warnings and zero errors.
- Single-image UI automation verified startup, initially disabled Apply,
  missing/corrupt input errors, recovery, and applying JPEG, PNG, and BMP.
- The standard file picker opened; dismissing it without a selection preserved
  the typed path and wallpaper.
- Quoted paths containing spaces and Unicode characters worked.
- Windows' wallpaper API reported the prepared BMP after each successful apply.
- Source images could be deleted after applying; the wallpaper setting persisted
  after closing the application.
- A desktop screenshot was visually inspected and confirmed that the generated
  test image was actually displayed, not just accepted by the API.
- The previous wallpaper was restored and generated test images were removed.

Slideshow verification:

- The configured 25-photo folder loaded from JSON and started successfully.
- Invalid JSON, missing properties, blank paths, and relative paths showed errors.
- Missing, empty, and all-corrupt folders stopped with visible errors.
- Real transitions measured **30.01 seconds** and **30.06 seconds**, including a
  wrap to the first image while the app was minimized.
- Corrupt files were skipped with a visible warning; unsupported extensions were
  excluded from the image count.
- Stop and closing the app prevented any subsequent change during a further
  **32-second** observation period for each.
- Manual application worked after stopping/restarting, and the cache stayed
  bounded at two reusable BMPs across cycles and app restarts.
- The previous wallpaper and pre-existing cache files were restored after testing.
