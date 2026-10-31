# Collage (composition) mode – implementation plan

Goal: instead of handing Windows one photo at a time, generate a wallpaper bitmap at the
primary monitor's native resolution. Landscape photos are shown one per wallpaper; portrait
photos are grouped side by side into a balanced landscape collage. Nothing is stretched,
nothing is cropped, the leftover space is a neutral background.

The existing direct mode (single image + slideshow of individual files) stays unchanged.

## 1. What is in the configured folder

`FolderPath` = `C:\Users\abhir\OneDrive\Pictures\pics`, primary monitor = **3840 × 2160 (16:9, 1.778)**.

| File | Stored W×H | EXIF orientation | Displayed W×H | Ratio | Bucket |
|---|---|---|---|---|---|
| 20240510_165501.jpg | 4000×3000 | 6 (rotate 90°) | 3000×4000 | 0.750 | Portrait |
| 20240626_091118.jpg | 8160×6120 | 6 | 6120×8160 | 0.750 | Portrait |
| 20240816_171400.jpg | 3000×4000 | – | 3000×4000 | 0.750 | Portrait |
| 20240816_201135.jpg | 3000×4000 | – | 3000×4000 | 0.750 | Portrait |
| 20240826_183920.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20240922_163935.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20240929_185919.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20241021_174412.jpg | 3000×4000 | 1 | 3000×4000 | 0.750 | Portrait |
| 20241211_075710.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20241212_083238.jpg | 4000×3000 | 8 (rotate 270°) | 3000×4000 | 0.750 | Portrait |
| 20241213_131907.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20241214_124129.jpg | 4000×3000 | 6 | 3000×4000 | 0.750 | Portrait |
| 20250315_160604.jpg | 3340×4529 | – | 3340×4529 | 0.737 | Portrait |
| 20250317_074351.jpg | 1848×4000 | – | 1848×4000 | 0.462 | Portrait (tall/narrow) |
| 20250622_180430.jpg | 7449×8974 | – | 7449×8974 | 0.830 | Portrait (near-square) |
| 20250628_154529.jpg | 4000×1848 | 6 | 1848×4000 | 0.462 | Portrait (tall/narrow) |
| 20251201_173228.jpg | 1019×1687 | – | 1019×1687 | 0.604 | Portrait |
| 20251205_104840.jpg | 1848×2493 | – | 1848×2493 | 0.741 | Portrait |
| 20251221_101434.jpg | 3500×1848 | 1 | 3500×1848 | 1.894 | Landscape (wide) |
| 20260407_192606.jpg | 3690×1848 | – | 3690×1848 | 1.997 | Landscape (wide) |
| 9N8A2938 copy.jpg | 4897×3266 | 1 | 4897×3266 | 1.499 | Landscape |
| 9N8A3140 copy.jpg | 4897×3266 | 1 | 4897×3266 | 1.499 | Landscape |
| 9N8A3236 copy.jpg | 3266×4897 | 1 | 3266×4897 | 0.667 | Portrait |
| 9N8A3413 copy.jpg | 4897×3266 | 1 | 4897×3266 | 1.499 | Landscape |
| 9N8A3450 copy.jpg | 4897×3266 | 1 | 4897×3266 | 1.499 | Landscape |

Observations:

* Sizes range from 1019×1687 to 7449×8974 (67 MP). Ratios span 0.462 – 1.997.
* **Raw pixel width/height is misleading.** Phones store many portrait shots as 4000×3000 with
  an EXIF "rotate" flag. Using raw sizes, 10 portrait photos would be classified as landscape
  and rendered sideways. The analyser must use the *displayed* (EXIF-corrected) size.
  (The existing direct mode also ignores EXIF orientation – see §8.)
* Effective split: **6 landscape, 19 portrait** (a mixed folder).

## 2. ⭐ Filtering / bucketing logic

Exactly two buckets. Every decodable photo lands in exactly one of them.

```
displayedW, displayedH = rawW, rawH
if EXIF orientation ∈ {5, 6, 7, 8}:   // any 90°/270° rotation (with or without mirror)
    swap(displayedW, displayedH)

ratio = displayedW / displayedH

bucket = ratio >= 1.0 ? LANDSCAPE : PORTRAIT     // exactly square counts as landscape
```

* No third "square" or "panorama" bucket, no thresholds that could leave a photo unassigned.
* Files that cannot be decoded (corrupt, unsupported, offline) are **excluded and reported**
  as skipped, they are not silently dropped.
* Only `.jpg/.jpeg/.png/.bmp` directly in the folder (same rule as the existing slideshow).
* Order is case-insensitive filename order (for these files ≈ capture date), so collages group
  photos taken around the same time.

What each bucket does:

| Bucket | Wallpaper produced |
|---|---|
| Landscape | One photo per wallpaper, scaled to fit the whole screen (letterbox/pillarbox with background). Photos close to 16:9 fill almost the entire screen; 3:2 photos get slim side bars. |
| Portrait | Grouped 2–4 side by side in one row, all scaled to the same height, centred, equal gaps. |

Guard: on a **portrait-oriented monitor** (height > width) collages make no sense, so every
photo becomes a single fit-to-screen page.

## 3. Portrait collage algorithm

Row layout for a group with ratios r₁…r_k (screen W×H, margin m, gap g):

```
availW = W − 2m,  availH = H − 2m
h      = min(availH, (availW − (k−1)·g) / Σr)   // as tall as possible without overflowing
w_i    = r_i · h                                   // exact aspect ratio, never stretched
rowW   = Σw_i + (k−1)·g
x0     = (W − rowW)/2,  y0 = (H − h)/2            // centred both ways
coverage = Σ(w_i · h) / (W · H)
```

Defaults: margin = 3 % of screen height (65 px at 2160), gap = 1.5 % (32 px).

Every photo is fully visible (no crop) and all photos in a collage share the same height, so
it looks like an intentional collage rather than two images pasted together.

**Choosing the groups.** Portraits are kept in folder order and split into consecutive groups
of 2–4. Among all valid splits, choose the one with the **highest mean coverage per wallpaper**
(dynamic programming over "first i portraits in g groups", then pick the best g). Effect on
16:9:

| Photos | Best grouping | Why |
|---|---|---|
| 3:4 (0.75) photos | pairs (≈75 % coverage) | a triple shrinks every photo to 1620 px tall |
| narrow 0.46 photos | 3–4 per wallpaper | a pair would leave half the screen empty |
| mix 0.75 + 0.46 + 0.75 | triple (≈82 %) | widths add up to ≈16:9 |
| odd count (e.g. 5) | 2 + 3 | there is never a leftover single |

Edge cases:

* 0 portraits → only landscape pages.
* **Exactly 1 portrait in the whole folder** → it is shown alone, centred with background on
  both sides (nothing to pair it with; this is the only lone-portrait case).
* Large folders: the DP runs over windows of 60 portraits so planning stays fast for
  thousands of photos.

**Final sequence**: landscape pages and portrait collages are interleaved by the position of
their first photo in the folder order, so the slideshow stays roughly chronological.

## 4. Architecture (layout separated from Windows)

```
WallpaperUtility/
  Composition/                    (no Windows wallpaper / registry / P/Invoke code)
    PhotoInfo.cs                  raw size + EXIF orientation → displayed size, ratio, bucket
    PhotoAnalyzer.cs              header-only read of size + orientation (no full decode)
    CollageLayoutEngine.cs        pure math: photos + screen size → pages of rectangles
    CompositionRenderer.cs        draws a page into a BitmapSource at native resolution
  PrimaryDisplay.cs               native resolution of the primary monitor (EnumDisplaySettings)
  PreviewWindow.xaml(.cs)         browse generated wallpapers, apply the one shown
  WallpaperService.cs             + Apply(Action<string> writeBitmap) overload
  WallpaperImage.cs               + Save(BitmapSource, path) for rendered composites
  SlideshowService.cs             + collage mode (rotates through pages instead of files)
  MainWindow.xaml(.cs)            + "Compose collages" checkbox and "Preview collages..." button
WallpaperUtility.CompositionTests/  console test harness (no NuGet packages)
```

* `CollageLayoutEngine` is reusable: input = any photo list + `ScreenSize` (+ options),
  output = `CompositionPage` list with exact `LayoutRect`s. It has no I/O and no WPF types,
  so it is unit-testable with synthetic sizes.
* `CompositionRenderer` uses only WPF imaging (WIC), which the app already uses. Each photo is
  decoded directly at its target size (no 67 MP full decodes), EXIF rotation/mirroring is
  applied, transparency is blended onto the background, and pixels are copied into a single
  Bgr32 buffer at the monitor's size. No `RenderTargetBitmap`, so it works on background
  threads.
* No new dependencies.

## 5. Integration with the existing workflow

* `WallpaperService.Apply(string)` is unchanged in behaviour; it now delegates to a new
  `Apply(Action<string> writePreparedBitmap)` that keeps the same two-slot cache, lock, Fit
  placement and error rollback. Composites are exactly screen-sized, so Fit shows them 1:1.
* Slideshow: a **Compose collages** checkbox switches the 30-second rotation from individual
  files to generated pages. Unchecked = exactly the old behaviour.
* **Preview collages...** opens a window that analyses the folder, shows a summary
  (landscape/portrait counts, skipped files, number of wallpapers), and lets you step through
  each page rendered at full native resolution; **Apply this wallpaper** applies it.

## 6. Test plan (before integration)

1. Layout engine with synthetic sizes, asserting for every case: each photo appears exactly
   once, aspect ratio preserved, rectangles inside the screen, no overlaps, equal gaps,
   vertical alignment, group sizes 2–4.
   Combinations: all landscape, all portrait (even/odd), single portrait, two portraits, narrow
   portraits, near-square, exact square, panorama, mixed sequences, empty input, ultrawide
   21:9, 16:10, 4:3, portrait monitor, 1000-photo performance.
2. Analyzer/renderer on the real folder: buckets match §1, output is exactly 3840×2160, EXIF
   rotated photos appear upright, margins are background-coloured.
3. Render PNG previews of the real-folder plan and of hand-picked combinations
   (2 portraits, 3 portraits, narrow + regular, landscape) and inspect them visually.
4. Then integrate and build with zero warnings.

## 7. Complexity assessment

A dynamic, reliable collage maker **is feasible** with the single-row justified layout above:
it is deterministic, guarantees no cropping/stretching, and every photo is assigned.

Flagged as deliberately **out of scope** (would add significant complexity for small gain):

* Multi-row / mosaic layouts (e.g. 1 big portrait + 2 stacked landscapes). Those need either
  cropping or a much larger search space to stay gap-free.
* Mixing landscape photos into collages, and non-consecutive "best fit" pairing across the
  whole folder (would break chronological grouping).
* Face/saliency-aware layout, blurred-photo backgrounds.
* Per-monitor compositions on multi-monitor setups: the composite is sized for the primary
  monitor; with Fit, other monitors show it scaled.
* Resolution changes while a collage slideshow runs are picked up on the next Start.

## 8. Pre-existing direct-mode issues (fixed)

* The direct mode copied pixels without honouring EXIF orientation, so phone photos stored as
  4000×3000 + "rotate 90°" appeared sideways. `WallpaperImage.Prepare` now applies the same
  orientation logic as the collage renderer (`PhotoAnalyzer.ReadOrientation` +
  `CompositionRenderer.ApplyOrientation`).
* `20240626_091118.jpg` (8160×6120, a minor JPEG defect) could not be decoded by the direct mode
  at all, because eager `BitmapCacheOption.OnLoad` decoding rejected it, so the slideshow skipped
  it. `Prepare` now decodes lazily while the file is open, which handles it.
* The test harness checks all 10 EXIF-rotated photos in the folder through the direct path.

## 9. Results

* Test harness: 15,418 checks, 0 failures. Planning 1000 photos takes about 10 ms.
* Real folder at 3840×2160 → **14 wallpapers**:
  * 5 pairs of 3:4 portraits (75 % coverage)
  * 3 triples: `0.75+0.75+0.737` (72 %), `0.462+0.83+0.462` (87 %), and `0.604+0.741+0.667` (80 %)
  * 6 single landscapes (84–94 %)
  * No lone portraits
* EXIF-6 and EXIF-8 photos render upright. Rendering 14 pages takes about 4 s (≈0.3 s per page).
* UI automation tested preview, navigation, applying from preview (3840×2160 BMP),
  the collage slideshow with a 30 s transition, and stopping it. Direct mode still works.
  The original wallpaper was restored afterwards.
