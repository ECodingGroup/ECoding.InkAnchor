using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using static ECoding.InkAnchor.InkAnchorBorder;
using System.Text;
using SixLabors.ImageSharp.Advanced;

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

    public static List<(int BoxId, Image<Rgba32> Cropped)> GetAnchorBoxesContentImage(Image<Rgba32> inputImage)
    => ExtractAllAnchorBoxes(inputImage);

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


    private static Task<(Image<Rgba32>? image, string? svg)> GenerateIngAnchorBoxAsync(
    InkAnchorGeneratorOptions options, bool generateSvg)
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

        int totalHeight = labelExtraTop + boxHeight + labelExtraBottom;

        // We'll store these so they're not hard-coded
        int markerSize = options.MarkerPixelSize;
        int markerBorderBits = options.MarkerBorderBits;
        int markerPadding = options.MarkerPadding;

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
                    float x = 0, y = labelExtraTop, w = width, h = boxHeight;

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
                        DrawStyledLine(new PointF(x, y), new PointF(x + w, y));

                    if (border.Sides.HasFlag(BorderSides.Right))
                        DrawStyledLine(new PointF(x + w - 1, y), new PointF(x + w - 1, y + h));

                    if (border.Sides.HasFlag(BorderSides.Bottom))
                        DrawStyledLine(new PointF(x, y + h), new PointF(x + w, y + h));

                    if (border.Sides.HasFlag(BorderSides.Left))
                        DrawStyledLine(new PointF(x, y), new PointF(x, y + h));
                }

                if (options.BoxLabel != null)
                {
                    var label = options.BoxLabel;
                    var font = SystemFonts.CreateFont(label.Font, label.FontSize);
                    float labelY = CalculateLabelY(label, labelExtraTop, boxHeight);

                    // Ensure full opacity
                    var color = label.Color.WithAlpha(1f);

                    var textOptions = new RichTextOptions(font)
                    {
                        Origin = new PointF((float)Math.Round(width / 2f), (float)Math.Round(labelY)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    ctx.DrawText(textOptions, label.LabelText, color);
                }

                // Draw top-left marker
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
            });

            return Task.FromResult<(Image<Rgba32>?, string?)>((image, null));
        }
        else
        {
            var svg = GenerateSvg(options, boxId, markerSize, markerBorderBits, labelExtraTop, boxHeight, width, totalHeight, markerPadding);

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

        // --- luminance buffer ----------------------------------------------------
        var lum = new byte[w * h];
        img.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = pa.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    lum[y * w + x] =
                        (byte)((row[x].R + row[x].G + row[x].B) / 3);
            }
        });

        // --- scan every plausible cell size -------------------------------------
        for (int cell = minCellPx; cell <= maxCellPx; cell++)
        {
            int win = (4 + 2 * borderBits) * cell;
            int step = Math.Max(1, cell / 2);

            for (int top = 0; top <= h - win; top += step)
            {
                for (int left = 0; left <= w - win; left += step)
                {
                    // cheap four-corner reject
                    if (lum[top * w + left] > 50 ||
                        lum[top * w + left + win - 1] > 50 ||
                        lum[(top + win - 1) * w + left] > 50 ||
                        lum[(top + win - 1) * w + left + win - 1] > 50)
                        continue;

                    // --- sample 4×4 payload -----------------------------------------
                    Span<byte> bits = stackalloc byte[16];
                    for (int cy = 0; cy < 4; cy++)
                        for (int cx = 0; cx < 4; cx++)
                        {
                            int sx = left + (borderBits + cx) * cell + cell / 2;
                            int sy = top + (borderBits + cy) * cell + cell / 2;
                            bits[cy * 4 + cx] = (byte)(lum[sy * w + sx] < 128 ? 0 : 1);
                        }

                    // --- compare against dictionary ----------------------------------
                    for (int id = 0; id < ArucoDict4x4_50.Markers.Length; id++)
                    {
                        var refBits = ArucoDict4x4_50.GetMarkerBits(id); // byte[4,4]

                        if (MatchAnyRotation(bits, refBits))
                        {
                            yield return new MarkerHit(id,
                                         new Rectangle(left, top, win, win));
                            left += win - step;          // skip duplicates
                            break;                        // next window
                        }
                    }
                }
            }
        }
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
    private static List<(int BoxId, Image<Rgba32> Cropped)>
        ExtractAllAnchorBoxes(Image<Rgba32> img)
    {
        var hits = DetectMarkers(img).ToList();
        if (hits.Count == 0) return new();

        /* build lookup table (same as before) */
        var map = new Dictionary<int, Rectangle>();
        foreach (var h in hits)
        { map.TryAdd(h.Id, h.Rect); }
        var result = new List<(int, Image<Rgba32>)>();

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
            var cropped = img.Clone(ctx => ctx.Crop(box));
            result.Add((id / 2, cropped));
        }
        return result;
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

    private static string GenerateSvg(InkAnchorGeneratorOptions options, byte boxId, int markerSize, int markerBorderBits, int labelExtraTop, int boxHeight, int width, int totalHeight, int markerPadding)
    {
        var svgTopLeft = CustomArucoDrawer.GenerateArucoMarkerSvgManually(boxId * 2, markerSize, markerBorderBits);
        var svgBottomRight = CustomArucoDrawer.GenerateArucoMarkerSvgManually(boxId * 2 + 1, markerSize, markerBorderBits);

        string fillColor = options.FillColor?.ToSvgHex() ?? "none";

        string labelSvg = string.Empty;
        if (options.BoxLabel is { } labelOpt)
        {
            var extraY = 12;
            if (labelOpt.LabelPlacement == BoxLabelPlacement.TopOutsideBox)
            {
                extraY *= -1;
            }

            float labelY = CalculateLabelY(labelOpt, labelExtraTop, boxHeight) + extraY;
            labelSvg = $"""
                    <text x="{width / 2}" 
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

            int x1 = 0, y1 = labelExtraTop;
            int x2 = width, y2 = labelExtraTop + boxHeight;

            var linesBuilder = new StringBuilder();

            if (border.Sides.HasFlag(BorderSides.Top))
                linesBuilder.AppendLine($"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y1}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Right))
                linesBuilder.AppendLine($"<line x1=\"{x2}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Bottom))
                linesBuilder.AppendLine($"<line x1=\"{x1}\" y1=\"{y2}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            if (border.Sides.HasFlag(BorderSides.Left))
                linesBuilder.AppendLine($"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x1}\" y2=\"{y2}\" stroke=\"{stroke}\" stroke-width=\"{thickness}\" stroke-dasharray=\"{dashArray}\" />");

            borderLinesSvg = linesBuilder.ToString();
        }

        string svg = $"""
            <svg width="{width}" height="{totalHeight}" xmlns="http://www.w3.org/2000/svg">
              <rect x="0" y="{labelExtraTop}" width="{width}" height="{boxHeight}" fill="{fillColor}" stroke="none"/>
              {borderLinesSvg}
              {labelSvg}
              <g transform="translate({markerPadding},{labelExtraTop + markerPadding})">
                {svgTopLeft}
              </g>
              <g transform="translate({width - markerSize - markerPadding},{labelExtraTop + boxHeight - markerSize - markerPadding})">
                {svgBottomRight}
              </g>
            </svg>
            """;

        return svg;
    }

    #endregion
}
