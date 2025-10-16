using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using static ECoding.InkAnchor.InkAnchorBorder;

namespace ECoding.InkAnchor;

public static class InkAnchorHandler
{
    public static async Task<Image<Rgba32>> GenerateAnchorBoxImageAsync(InkAnchorGeneratorOptions options)
    {
        var (img, _) = await GenerateIngAnchorBoxAsync(options, generateSvg: false);
        return img!;
    }

    public static async Task<string> GenerateAnchorBoxSvgAsync(InkAnchorGeneratorOptions options)
    {
        var (_, svg) = await GenerateIngAnchorBoxAsync(options, generateSvg: true);
        return svg!;
    }

    public static List<(int BoxId, Image<Rgba32> Cropped)> GetAnchorBoxesContentImage(
        Image<Rgba32> inputImage,
        AnchorBoxDetectionOptions? options = null)
    {
        options ??= new AnchorBoxDetectionOptions();
        return ExtractAllAnchorBoxes(inputImage, options);
    }

    public static double GetFilledAreaPercentage(Image<Rgba32> image, byte brightnessThreshold = 240)
    {
        int filled = 0;
        int total = image.Width * image.Height;
        image.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                Span<Rgba32> row = pa.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    int lum = (p.R + p.G + p.B) / 3;
                    if (lum < brightnessThreshold) filled++;
                }
            }
        });
        return (double)filled / total;
    }

    /// <summary>
    /// Crops an ImageSharp image to the tight bounding box of the signature,
    /// using simple colour heuristics.  Pixels that fall inside a small
    /// <paramref name="cornerStrip"/>×<paramref name="cornerStrip"/> square
    /// in the TL or BR corner are ignored so that residual ArUco artefacts
    /// do not inflate the bounding-box.
    /// </summary>
    public static Image<Rgba32> TrimBasedOnBinarisedImage(
        Image<Rgba32> src,
        int minBlue = 60,
        int minIntensity = 30,
        int padding = 10,
        int cornerStrip = 6       // ← NEW optional parameter (defaults to 6 px)
    )
    {
        if (src is null || src.Width == 0 || src.Height == 0)
            throw new ArgumentException("Input image is null or empty.", nameof(src));

        int w = src.Width, h = src.Height;
        int minX = w, minY = h, maxX = 0, maxY = 0;
        bool found = false;

        src.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < h; y++)
            {
                Span<Rgba32> row = pa.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    /* Skip the two marker-corner squares */
                    bool inTopLeftCorner = x < cornerStrip && y < cornerStrip;
                    bool inBottomRight = x >= w - cornerStrip && y >= h - cornerStrip;
                    if (inTopLeftCorner || inBottomRight) continue;

                    var p = row[x];
                    bool isInk =
                        (p.B > minBlue && p.B > p.R + 15 && p.B > p.G + 15) ||   // blueish
                        (p.R < minIntensity && p.G < minIntensity && p.B < minIntensity); // dark

                    if (!isInk) continue;

                    found = true;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        });

        if (!found)
            return new Image<Rgba32>(padding * 2 + 1, padding * 2 + 1);      // blank

        var crop = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        using var tight = src.Clone(ctx => ctx.Crop(crop));

        var canvas = new Image<Rgba32>(tight.Width + 2 * padding,
                                       tight.Height + 2 * padding,
                                       Color.White);
        canvas.Mutate(ctx => ctx.DrawImage(tight,
                                           new Point(padding, padding),
                                           1f));
        return canvas;
    }

    #region private helpers


    private static Task<(Image<Rgba32>? image, string? svg)> GenerateIngAnchorBoxAsync(InkAnchorGeneratorOptions options, bool generateSvg)
    {
        ValidateGeneratorOptions(options);
        int width = options.PixelWidth;
        int boxHeight = options.PixelHeight;
        byte boxId = options.BoxId;

        int labelExtraTop = 0;
        int labelExtraBottom = 0;

        if (options.BoxLabel?.LabelPlacement == BoxLabelPlacement.TopOutsideBox)
            labelExtraTop = options.BoxLabel.FontSize + 5;

        if (options.BoxLabel?.LabelPlacement == BoxLabelPlacement.BottomOutsideBox)
            labelExtraBottom = options.BoxLabel.FontSize + 5;

        // We'll store these so they're not hard-coded
        int markerSize = options.MarkerPixelSize;
        int markerBorderBits = options.MarkerBorderBits;
        int markerPadding = options.MarkerPadding;

        int totalWidth, totalHeight, boxOffsetX, boxOffsetY;
        if (options.OuterArUcoMarkers)
        {
            totalWidth = width + 2 * (markerSize + markerPadding);
            totalHeight = labelExtraTop + boxHeight + labelExtraBottom + 2 * (markerSize + markerPadding);
            boxOffsetX = markerSize + markerPadding;
            boxOffsetY = markerSize + markerPadding + labelExtraTop;
        }
        else
        {
            totalWidth = width;
            totalHeight = labelExtraTop + boxHeight + labelExtraBottom;
            boxOffsetX = 0;
            boxOffsetY = labelExtraTop;
        }

        if (!generateSvg)
        {
            // -- RASTER GENERATION (PNG, etc.) --
            var topLeftMarker = CustomArucoDrawer.DrawArucoMarkerManually(boxId * 2, markerSize, markerBorderBits);
            var bottomRightMarker = CustomArucoDrawer.DrawArucoMarkerManually(boxId * 2 + 1, markerSize, markerBorderBits);

            var image = new Image<Rgba32>(width, totalHeight);
            image.Mutate(ctx =>
            {
                if (options.FillColor.HasValue)
                    ctx.Fill(options.FillColor.Value);

                if (options.Border != null)
                {
                    var border = options.Border;
                    var color = border.Color;
                    float thickness = border.Thickness;

                    float borderX = boxOffsetX;
                    float borderY = boxOffsetY;
                    float borderW = width;
                    float borderH = boxHeight;

                    // Helper that selects the appropriate drawing method based on the style.
                    void DrawStyledLine(PointF start, PointF end)
                    {
                        if (border.Style == BorderStyle.Dashed)
                        {
                            // For dashed lines, e.g., 10px dash, 10px gap:
                            DrawDashedLine(ctx, color, thickness, start, end, dashLength: 10, gap: 10);
                        }
                        else if (border.Style == BorderStyle.Dotted)
                        {
                            // For dotted lines, e.g., 2px dot, 6px gap:
                            DrawDashedLine(ctx, color, thickness, start, end, dashLength: 2, gap: 6);
                        }
                        else
                        {
                            // Solid line:
                            ctx.DrawLine(color, thickness, start, end);
                        }
                    }

                    // Draw each side if its flag is set.
                    if (border.Sides.HasFlag(BorderSides.Top))
                        DrawStyledLine(new PointF(borderX, borderY), new PointF(borderX + borderW, borderY));

                    if (border.Sides.HasFlag(BorderSides.Right))
                        DrawStyledLine(new PointF(borderX + borderW - 1f, borderY), new PointF(borderX + borderW - 1f, borderY + borderH));

                    if (border.Sides.HasFlag(BorderSides.Bottom))
                        DrawStyledLine(new PointF(borderX, borderY + borderH), new PointF(borderX + borderW, borderY + borderH));

                    if (border.Sides.HasFlag(BorderSides.Left))
                        DrawStyledLine(new PointF(borderX, borderY), new PointF(borderX, borderY + borderH));
                }

                if (options.BoxLabel != null)
                {
                    var label = options.BoxLabel;
                    var font = SystemFonts.CreateFont(label.Font, label.FontSize);
                    float labelY;
                    if (options.OuterArUcoMarkers)
                    {
                        labelY = CalculateOuterLabelY(label, markerSize, markerPadding, boxOffsetY, boxHeight);
                    } 
                    else
                    {
                        labelY = CalculateLabelY(label, labelExtraTop, boxHeight);
                    }

                    // Ensure full opacity
                    var color = label.Color.WithAlpha(1f);

                    var textOptions = new RichTextOptions(font)
                    {
                        Origin = new PointF((float)Math.Round(totalWidth / 2f), (float)Math.Round(labelY)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    ctx.DrawText(textOptions, label.LabelText, color);
                }

                // Draw top-left marker
                if (options.OuterArUcoMarkers)
                {
                    ctx.DrawImage(topLeftMarker, new Point(markerPadding, markerPadding + labelExtraTop), 1f);
                    ctx.DrawImage(bottomRightMarker, new Point(totalWidth - markerSize - markerPadding, totalHeight - markerSize - markerPadding - labelExtraBottom), 1f);
                } else
                {
                    ctx.DrawImage(
                        topLeftMarker,
                        new SixLabors.ImageSharp.Point(
                            markerPadding,
                            labelExtraTop + markerPadding),
                        1f);

                    // Draw bottom-right marker
                    ctx.DrawImage(
                        bottomRightMarker,
                        new SixLabors.ImageSharp.Point(
                            width - markerSize - markerPadding,
                            labelExtraTop + boxHeight - markerSize - markerPadding),
                        1f);
                }
            });

            return Task.FromResult<(Image<Rgba32>?, string?)>((image, null));
        }
        else
        {
            var svg = GenerateSvg(options, boxId, markerSize, markerBorderBits, labelExtraTop, labelExtraBottom, boxHeight, width, totalWidth, totalHeight, markerPadding, boxOffsetX, boxOffsetY);

            return Task.FromResult<(Image<Rgba32>?, string?)>((null, svg));
        }
    }

    private sealed record MarkerHit(int Id, Rectangle Rect);

    /// <remarks>
    /// Finds 4 × 4 ArUco markers without OpenCV.  
    /// Works for every rotation and a range of cell-sizes.
    /// </remarks>
    private static IEnumerable<MarkerHit> DetectMarkers(
    Image<Rgba32> img,
    int borderBits = 1,
    int minCellPx = 4,
    int maxCellPx = 14)
    {
        int w = img.Width, h = img.Height;

        // ---- 1) Luminance (8-bit) ----
        var lum = new byte[w * h];
        img.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = pa.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    lum[y * w + x] = (byte)((row[x].R + row[x].G + row[x].B) / 3);
            }
        });

        // ---- 2) Integral image (exclusive coords) ----
        int istride = w + 1;
        var ii = new long[(h + 1) * istride];
        for (int y = 1; y <= h; y++)
        {
            long rowsum = 0;
            int lumRow = (y - 1) * w;
            int iiRow = y * istride;
            int iiPrevRow = (y - 1) * istride;
            for (int x = 1; x <= w; x++)
            {
                rowsum += lum[lumRow + (x - 1)];
                ii[iiRow + x] = ii[iiPrevRow + x] + rowsum;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static long SumRect(long[] sat, int stride, int x0, int y0, int x1, int y1)
            => sat[y1 * stride + x1] - sat[y1 * stride + x0]
             - sat[y0 * stride + x1] + sat[y0 * stride + x0];

        // ---- 3) Pattern LUT (exact) + arrays for Hamming fallback ----
        static int BitIndex(int r, int c) => 15 - (r * 4 + c);
        static ushort RotateFlat(ushort v, int rot)
        {
            ushort outV = 0;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    int bit = (v >> BitIndex(r, c)) & 1;
                    if (bit == 0) continue;
                    int tr, tc;
                    switch (rot)
                    {
                        case 0: tr = r; tc = c; break; // 0°
                        case 1: tr = c; tc = 3 - r; break; // 90°
                        case 2: tr = 3 - r; tc = 3 - c; break; // 180°
                        default: tr = 3 - c; tc = r; break; // 270°
                    }
                    outV |= (ushort)(1 << BitIndex(tr, tc));
                }
            return outV;
        }

        var idByPattern = new int[1 << 16];               // exact -> id, -1 = none
        Array.Fill(idByPattern, -1);
        var rotPatterns = new ushort[ArucoDict4x4_50.Markers.Length * 4];
        var rotIds = new int[rotPatterns.Length];

        int rp = 0;
        for (int id = 0; id < ArucoDict4x4_50.Markers.Length; id++)
        {
            ushort baseV = ArucoDict4x4_50.Markers[id];   // 16-bit row-major (MSB first)
            for (int r90 = 0; r90 < 4; r90++)
            {
                ushort pat = RotateFlat(baseV, r90);
                if (idByPattern[pat] < 0) idByPattern[pat] = id;   // exact
                rotPatterns[rp] = pat;                             // for Hamming
                rotIds[rp] = id;
                rp++;
            }
        }

        // ---- 4) Scan scales (bottom-first), early-exit only on PLAUSIBLE pair ----
        var results = new List<MarkerHit>(16);
        var rectById = new Rectangle?[ArucoDict4x4_50.Markers.Length];
        int havePair = 0;                            // 0 = no, 1 = yes (volatile)
        object gate = new object();

        // Geometry check: BR must be to the bottom-right of TL and same scale (±15%)
        static bool IsPlausiblePair(Rectangle tl, Rectangle br)
        {
            if (br.Left <= tl.Right || br.Top <= tl.Bottom) return false;
            // similar window sizes
            float wRatio = (float)tl.Width / Math.Max(1, br.Width);
            float hRatio = (float)tl.Height / Math.Max(1, br.Height);
            return wRatio > 0.85f && wRatio < 1.15f && hRatio > 0.85f && hRatio < 1.15f;
        }

        // k-means (k=2) threshold for 16 values (3 iterations, no alloc)
        static long TwoMeansThreshold(ReadOnlySpan<long> s16)
        {
            long min = long.MaxValue, max = long.MinValue;
            for (int i = 0; i < 16; i++) { if (s16[i] < min) min = s16[i]; if (s16[i] > max) max = s16[i]; }
            double m0 = min, m1 = max;
            for (int it = 0; it < 3; it++)
            {
                double sum0 = 0, sum1 = 0; int c0 = 0, c1 = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (Math.Abs(s16[i] - m0) <= Math.Abs(s16[i] - m1)) { sum0 += s16[i]; c0++; }
                    else { sum1 += s16[i]; c1++; }
                }
                if (c0 > 0) m0 = sum0 / c0;
                if (c1 > 0) m1 = sum1 / c1;
            }
            return (long)((m0 + m1) * 0.5);
        }

        for (int cell = minCellPx; cell <= maxCellPx && Volatile.Read(ref havePair) == 0; cell++)
        {
            int t = borderBits * cell;
            int win = (4 + 2 * borderBits) * cell;
            if (win > w || win > h) break;

            // Good recall; try "cell" if you need more speed
            int step = Math.Max(1, cell / 2);

            // precompute centers and patch size
            int[] cxCenter = new int[4], cyCenter = new int[4];
            for (int i = 0; i < 4; i++)
            {
                cxCenter[i] = t + i * cell + cell / 2;
                cyCenter[i] = t + i * cell + cell / 2;
            }
            int r = Math.Max(1, cell / 5);
            int innerSide = win - 2 * t;
            if (innerSide <= 0) continue;
            int ringArea = (win * t * 2) + ((win - 2 * t) * t * 2);
            int innerArea = innerSide * innerSide;

            int nTop = (h - win) / step + 1;

            Parallel.For(
                0, nTop,
                new ParallelOptions { MaxDegreeOfParallelism = -1 },
                () => new List<MarkerHit>(2),
                (idx, state, localHits) =>
                {
                    if (Volatile.Read(ref havePair) != 0) { state.Stop(); return localHits; }
                    int ti = nTop - 1 - idx;           // bottom-first
                    int top = ti * step;

                    int y0Win = top, y1Win = top + win;
                    int y0Inner = y0Win + t, y1Inner = y1Win - t;

                    for (int left = 0; left <= w - win && Volatile.Read(ref havePair) == 0; left += step)
                    {
                        int x0Win = left, x1Win = left + win;
                        int x0Inner = x0Win + t, x1Inner = x1Win - t;

                        // very lenient ring test: skip only if ring brighter than inside
                        long sumTop = SumRect(ii, istride, x0Win, y0Win, x1Win, y0Win + t);
                        long sumBottom = SumRect(ii, istride, x0Win, y1Win - t, x1Win, y1Win);
                        long sumLeft = SumRect(ii, istride, x0Win, y0Win + t, x0Win + t, y1Win - t);
                        long sumRight = SumRect(ii, istride, x1Win - t, y0Win + t, x1Win, y1Win - t);
                        long ringSum = sumTop + sumBottom + sumLeft + sumRight;
                        long innerSum = SumRect(ii, istride, x0Inner, y0Inner, x1Inner, y1Inner);
                        if (ringSum > innerSum) continue;

                        // collect the 16 center-patch sums
                        Span<long> sums = stackalloc long[16];
                        int k = 0;
                        for (int cy = 0; cy < 4; cy++)
                        {
                            int cyc = y0Win + cyCenter[cy];
                            int y0p = cyc - r, y1p = cyc + r + 1;
                            for (int cx = 0; cx < 4; cx++)
                            {
                                int cxc = x0Win + cxCenter[cx];
                                int x0p = cxc - r, x1p = cxc + r + 1;
                                sums[k++] = SumRect(ii, istride, x0p, y0p, x1p, y1p);
                            }
                        }

                        long thr = TwoMeansThreshold(sums);

                        // build pattern
                        ushort observed = 0;
                        for (int i = 0; i < 16; i++)
                            if (sums[i] >= thr) observed |= (ushort)(1 << (15 - i));

                        // exact LUT match first
                        int id = idByPattern[observed];

                        // fallback: smallest Hamming distance (<= 2) across rotations
                        if (id < 0)
                        {
                            int bestId = -1, bestD = 3; // accept 0,1,2
                            for (int i = 0; i < rotPatterns.Length; i++)
                            {
                                int d = BitOperations.PopCount((uint)(observed ^ rotPatterns[i]));
                                if (d < bestD) { bestD = d; bestId = rotIds[i]; if (bestD == 0) break; }
                            }
                            if (bestD <= 2) id = bestId;
                        }

                        if (id >= 0)
                        {
                            var rect = new Rectangle(left, top, win, win);
                            localHits.Add(new MarkerHit(id, rect));

                            // ---- EARLY-EXIT ONLY IF A *PLAUSIBLE* TL ↔ BR PAIR EXISTS ----
                            lock (gate)
                            {
                                rectById[id] = rect;
                                int tlId = (id % 2 == 0) ? id : id - 1;
                                int brId = tlId + 1;

                                if (rectById[tlId].HasValue && rectById[brId].HasValue)
                                {
                                    var tl = rectById[tlId]!.Value;
                                    var br = rectById[brId]!.Value;
                                    if (IsPlausiblePair(tl, br))
                                    {
                                        Volatile.Write(ref havePair, 1);
                                        state.Stop();
                                    }
                                }
                            }

                            // Skip horizontally overlapping windows in this row
                            left += win - step;
                        }
                    }
                    return localHits;
                },
                localHits => { lock (results) results.AddRange(localHits); });

            if (Volatile.Read(ref havePair) != 0)
                break;
        }

        return results;
    }

    // -----------------------------------------------------------------------------
    // 2.  Helper lives *outside* DetectMarkers – no closure -> no compiler error
    // -----------------------------------------------------------------------------
    private static bool MatchAnyRotation(ReadOnlySpan<byte> bits, byte[,] refBits)
    {
        for (int rot = 0; rot < 4; rot++)          // 0°,90°,180°,270°
        {
            bool ok = true;

            for (int cy = 0; cy < 4 && ok; cy++)
                for (int cx = 0; cx < 4 && ok; cx++)
                {
                    int tx, ty;                        // target in reference
                    switch (rot)
                    {
                        case 0: tx = cx; ty = cy; break;
                        case 1: tx = 3 - cy; ty = cx; break;
                        case 2: tx = 3 - cx; ty = 3 - cy; break;
                        default: tx = cy; ty = 3 - cx; break;
                    }
                    if (bits[cy * 4 + cx] != refBits[ty, tx])
                        ok = false;
                }
            if (ok) return true;
        }
        return false;
    }

    /* ExtractAllAnchorBoxes keeps its public signature – only the internals
       are switched to the new DetectMarkers that yields good hits again. */

    /// <summary>
    /// Detects all anchor box positions in the image by finding pairs of ArUco markers.
    /// Each anchor box is defined by a top-left (even ID) and bottom-right (odd ID) marker pair.
    /// Returns the content area between these markers, excluding the markers themselves.
    /// </summary>
    /// <param name="img">The image to search for anchor box markers</param>
    /// <param name="options">Detection options for marker scanning parameters</param>
    /// <returns>List of anchor box IDs and their rectangular positions in the image</returns>
    private static List<(int BoxId, Rectangle Position)> ExtractAllAnchorBoxesPositions(Image<Rgba32> img, AnchorBoxDetectionOptions options)
    {
        options ??= new AnchorBoxDetectionOptions();

        var hits = DetectMarkers(img, options.BorderBits, options.MinCellPx, options.MaxCellPx).ToList();
        if (hits.Count == 0) return new();

        /* build lookup table */
        var map = new Dictionary<int, Rectangle>();
        foreach (var h in hits)
        {
            map.TryAdd(h.Id, h.Rect);
        }

        var result = new List<(int, Rectangle)>();
        foreach (int id in map.Keys.OrderBy(k => k))
        {
            if (id % 2 != 0) continue;       // we want the TL marker (even)
            int brId = id + 1;
            if (!map.TryGetValue(brId, out var brRect)) continue;

            var tlRect = map[id];
            int left = tlRect.Right + 2;
            int top = tlRect.Bottom + 2;
            int right = brRect.Left - 2;
            int bottom = brRect.Top - 2;

            if (right <= left || bottom <= top) continue;

            var box = new Rectangle(left, top, right - left, bottom - top);
            result.Add((id / 2, box));
        }

        return result;
    }

    /// <summary>
    /// Crops regions from an image based on provided rectangular positions.
    /// Creates cloned image segments for each position, useful for extracting
    /// anchor box content areas as separate images.
    /// </summary>
    /// <param name="image">The source image to crop from</param>
    /// <param name="positions">List of box IDs and their rectangular positions to crop</param>
    /// <returns>List of box IDs with their corresponding cropped image segments</returns>
    private static List<(int BoxId, Image<Rgba32> Cropped)> CropImages(Image<Rgba32> image, List<(int BoxId, Rectangle Position)> positions)
    {
        var result = new List<(int, Image<Rgba32>)>();
        foreach (var (boxId, position) in positions)
        {
            var cropped = image.Clone(ctx => ctx.Crop(position));
            result.Add((boxId, cropped));
        }
        return result;
    }

    /// <summary>
    /// Attempts to detect anchor boxes by applying rotations to the detection image.
    /// When markers are found, applies the same rotation to the original image before cropping.
    /// This is particularly useful when documents are scanned at an angle via physical scanner.
    /// </summary>
    /// <param name="imageFrom">The image to use for detection (may have enhancements like binary threshold applied)</param>
    /// <param name="originalImage">The original unmodified image to crop from</param>
    /// <param name="options">Detection options including rotation angles to try</param>
    /// <returns>List of cropped anchor box images from the original image</returns>
    private static List<(int BoxId, Image<Rgba32> Cropped)> ApplyRotationsAndGetAllAnchorBoxes(
        Image<Rgba32> imageFrom,
        Image<Rgba32> originalImage,
        AnchorBoxDetectionOptions options)
    {
        foreach (var rotation in options.Rotations)
        {
            using var rotatedDetection = imageFrom.Clone(ctx => ctx.Rotate(rotation));

            var positions = ExtractAllAnchorBoxesPositions(rotatedDetection, options);
            if (positions.Count > 0)
            {
                // Found markers in rotated image - apply same rotation to original and crop
                using var rotatedOriginal = originalImage.Clone(ctx => ctx.Rotate(rotation));
                return CropImages(rotatedOriginal, positions);
            }
        }

        return [];
    }

    /// <summary>
    /// Extracts all anchor boxes from an image using a multi-stage detection strategy.
    /// Tries detection with progressively more enhancements: first on the original image,
    /// then with binary threshold, and finally with rotations. This approach balances
    /// detection success rate with performance.
    /// </summary>
    /// <param name="originalImage">The original image to extract anchor boxes from</param>
    /// <param name="options">Detection options including optional enhancements (binary threshold, rotations)</param>
    /// <returns>List of cropped anchor box images with their box IDs</returns>
    private static List<(int BoxId, Image<Rgba32> Cropped)> ExtractAllAnchorBoxes(
        Image<Rgba32> originalImage,
        AnchorBoxDetectionOptions options)
    {
        // Stage 1: Attempt detection on the original image without any modifications
        var positionsFromOriginal = ExtractAllAnchorBoxesPositions(originalImage, options);
        if (positionsFromOriginal.Count > 0)
        {
            return CropImages(originalImage, positionsFromOriginal);
        }

        // Stage 2: Apply binary threshold if specified
        // Converting to black/white often improves detection on low-contrast or noisy images
        if (options.BinaryThreshold != null)
        {
            using var binaryVersion = originalImage.Clone(ctx => {
                ctx.Grayscale()
                    .BinaryThreshold(options.BinaryThreshold.Value);
            });

            var positionsFromBinary = ExtractAllAnchorBoxesPositions(binaryVersion, options);
            if (positionsFromBinary.Count > 0)
            {
                return CropImages(originalImage, positionsFromBinary);
            }

            // Stage 2b: Try rotations on the binary image
            // Combines both enhancements for maximum detection success
            if (options.Rotations != null && options.Rotations.Length > 0)
            {
                var resultFromBinaryRotated = ApplyRotationsAndGetAllAnchorBoxes(binaryVersion, originalImage, options);
                if (resultFromBinaryRotated.Count > 0)
                {
                    return resultFromBinaryRotated;
                }
            }
        }

        // Stage 3: Apply rotations if specified
        // Useful for documents scanned at an angle via physical scanner
        if (options.Rotations != null && options.Rotations.Length > 0)
        {
            var resultFromRotated = ApplyRotationsAndGetAllAnchorBoxes(originalImage, originalImage, options);
            if (resultFromRotated.Count > 0)
            {
                return resultFromRotated;
            }
        }

        return [];
    }

    private static void ValidateGeneratorOptions(InkAnchorGeneratorOptions options)
    {
        if (options.PixelWidth < InkAnchorGeneratorOptions.BoxMinWidth)
            throw new ArgumentOutOfRangeException(nameof(options.PixelWidth));
        if (options.PixelHeight < InkAnchorGeneratorOptions.BoxMinHeight)
            throw new ArgumentOutOfRangeException(nameof(options.PixelHeight));
    }

    private static string EscapeXml(string s) => System.Security.SecurityElement.Escape(s) ?? string.Empty;

    private static string ToSvgHex(this Color c)
    {
        var p = c.ToPixel<Rgba32>();
        return $"#{p.R:X2}{p.G:X2}{p.B:X2}";
    }

    /// <summary>
    /// Draws a dashed (or dotted) line by repeatedly drawing short solid line segments.
    /// </summary>
    /// <param name="ctx">The image processing context.</param>
    /// <param name="color">Line color.</param>
    /// <param name="thickness">Line thickness.</param>
    /// <param name="start">Start point.</param>
    /// <param name="end">End point.</param>
    /// <param name="dashLength">Length of the drawn segment.</param>
    /// <param name="gap">Length of the gap between segments.</param>
    private static void DrawDashedLine(IImageProcessingContext ctx, Color color, float thickness, PointF start, PointF end, float dashLength, float gap)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float dashPlusGap = dashLength + gap;
        var direction = new PointF(dx / distance, dy / distance);
        float current = 0f;

        while (current < distance)
        {
            // Compute the end point of this dash segment:
            float segmentLength = MathF.Min(dashLength, distance - current);
            var p1 = new PointF(start.X + direction.X * current, start.Y + direction.Y * current);
            var p2 = new PointF(start.X + direction.X * (current + segmentLength), start.Y + direction.Y * (current + segmentLength));

            // Draw the solid segment:
            ctx.DrawLine(color, thickness, p1, p2);
            current += dashPlusGap;
        }
    }

    private static float CalculateLabelY(InkAnchorLabel label, int labelOffsetTop, int boxHeight)
    {
        const float padding = 5;
        return label.LabelPlacement switch
        {
            //BoxLabelPlacement.TopInsideBox => labelOffsetTop + padding,
            //BoxLabelPlacement.BottomInsideBox => labelOffsetTop + boxHeight - label.FontSize - padding,
            BoxLabelPlacement.TopOutsideBox => padding,
            BoxLabelPlacement.BottomOutsideBox => labelOffsetTop + boxHeight + padding,
            _ => labelOffsetTop + boxHeight - label.FontSize - padding
        };
    }

    private static float CalculateOuterLabelY(InkAnchorLabel label, int markerSize, int markerPadding, int boxOffsetY, int boxHeight)
    {
        const float padding = 5;
        return label.LabelPlacement switch
        {
            BoxLabelPlacement.TopOutsideBox => markerSize + markerPadding + padding,
            BoxLabelPlacement.BottomOutsideBox => boxOffsetY + boxHeight + padding,
            _ => boxOffsetY + boxHeight - label.FontSize - padding
        };
    }

    private static string GenerateSvg(InkAnchorGeneratorOptions options, byte boxId, int markerSize, int markerBorderBits, int labelExtraTop, int labelExtraBottom, int boxHeight, int width, int totalWidth, int totalHeight, int markerPadding, int boxOffsetX, int boxOffsetY)
    {
        var svgTopLeft = CustomArucoDrawer.GenerateArucoMarkerSvgManually(boxId * 2, markerSize, markerBorderBits);
        var svgBottomRight = CustomArucoDrawer.GenerateArucoMarkerSvgManually(boxId * 2 + 1, markerSize, markerBorderBits);

        string fillColor = options.FillColor?.ToSvgHex() ?? "none";

        string labelSvg = string.Empty;
        if (options.BoxLabel is { } labelOpt)
        {
            float labelY;
            if (options.OuterArUcoMarkers)
            {
                labelY = CalculateOuterLabelY(options.BoxLabel, markerSize, markerPadding, boxOffsetY, boxHeight);
            }
            else
            {
                labelY = CalculateLabelY(options.BoxLabel, labelExtraTop, boxHeight);
            }

            int baselineAdjust = 12;
            if (options.BoxLabel.LabelPlacement == BoxLabelPlacement.TopOutsideBox)
            {
                baselineAdjust *= -1;
            }
            labelY += baselineAdjust;
            labelSvg = $"""
                    <text x="{totalWidth / 2}" 
                          y="{labelY}" 
                          text-anchor="middle" 
                          font-size="{labelOpt.FontSize}" 
                          fill="{labelOpt.Color.ToSvgHex()}" 
                          font-family="{labelOpt.Font}">
                      {EscapeXml(labelOpt.LabelText)}
                    </text>
                    """;
        }

        // Build individual border lines based on the BorderSides flags
        var border = options.Border;
        string borderLinesSvg = string.Empty;

        if (border != null && border.Sides != BorderSides.None)
        {
            string dashArray = border.Style switch
            {
                BorderStyle.Dashed => "10,10",
                BorderStyle.Dotted => "2,6",
                _ => "none"
            };

            string stroke = border.Color.ToSvgHex();
            int thickness = border.Thickness;

            int borderX = boxOffsetX;
            int borderRightX = borderX + width;
            int borderY = boxOffsetY;
            int borderBottomY = boxOffsetY + boxHeight;


            var linesBuilder = new StringBuilder();

            if (border.Sides.HasFlag(BorderSides.Top))
                linesBuilder.AppendLine($"<line x1=\"{borderX}\" y1=\"{borderY}\" x2=\"{borderRightX}\" y2=\"{borderY}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Right))
                linesBuilder.AppendLine($"<line x1=\"{borderRightX}\" y1=\"{borderY}\" x2=\"{borderRightX}\" y2=\"{borderBottomY}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Bottom))
                linesBuilder.AppendLine($"<line x1=\"{borderX}\" y1=\"{borderBottomY}\" x2=\"{borderRightX}\" y2=\"{borderBottomY}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Left))
                linesBuilder.AppendLine($"<line x1=\"{borderX}\" y1=\"{borderY}\" x2=\"{borderX}\" y2=\"{borderBottomY}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            borderLinesSvg = linesBuilder.ToString();
        }

        string topLeftMarkerPos, bottomRightMarkerPos;
        if (options.OuterArUcoMarkers)
        {
            topLeftMarkerPos = $"translate({markerPadding},{markerPadding + labelExtraTop})";
            bottomRightMarkerPos = $"translate({totalWidth - markerSize - markerPadding},{totalHeight - markerSize - markerPadding - labelExtraBottom})";
        }
        else
        {
            topLeftMarkerPos = $"translate({markerPadding},{labelExtraTop + markerPadding})";
            bottomRightMarkerPos = $"translate({width - markerSize - markerPadding},{labelExtraTop + boxHeight - markerSize - markerPadding})";
        }

        string svg = $"""
            <svg width="{totalWidth}" height="{totalHeight}" xmlns="http://www.w3.org/2000/svg">
              <rect x="{boxOffsetX}" y="{boxOffsetY}" width="{width}" height="{boxHeight}" fill="{fillColor}" stroke="none"/>
              {borderLinesSvg}
              {labelSvg}
              <g transform="{topLeftMarkerPos}">
                {svgTopLeft}
              </g>
              <g transform="{bottomRightMarkerPos}">
                {svgBottomRight}
              </g>
            </svg>
            """;

        return svg;
    }

    #endregion
}
