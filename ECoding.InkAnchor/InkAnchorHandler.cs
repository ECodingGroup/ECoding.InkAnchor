using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using static ECoding.InkAnchor.InkAnchorBorder;
using System.Text;
using SixLabors.ImageSharp.Advanced;

namespace ECoding.InkAnchor;

public static class InkAnchorHandler
{
    public static async Task<Image<Rgba32>> GenerateAnchorBoxImageAsync(InkAnchorGeneratorOptions options)
    {
        var generationResult = await GenerateIngAnchorBoxAsync(options, generateSvg: false);
        return generationResult.image!;
    }

    public static async Task<string> GenerateAnchorBoxSvgAsync(InkAnchorGeneratorOptions options)
    {
        var generationResult = await GenerateIngAnchorBoxAsync(options, generateSvg: true);
        return generationResult.svg!;
    }

    public static List<(int BoxId, Image<Rgba32> Cropped)> GetAnchorBoxesContentImage(Image<Rgba32> inputImage)
    {
        using var mat = ToMat(inputImage);

        // 2) Detect & extract boxes (boxId, Mat Cropped) 
        var allBoxes = ExtractAllAnchorBoxes(mat);

        // 3) Convert each cropped Mat => Image<Rgba32>
        var results = new List<(int BoxId, Image<Rgba32>)>();

        foreach (var (boxId, croppedMat) in allBoxes)
        {
            var croppedImg = croppedMat.ToImageSharpRgba32();
            results.Add((boxId, croppedImg));
        }

        return results;
    }

    /// <summary>
    /// Evaluates the filled area of an ImageSharp image as a fraction (0.xxx).
    /// A pixel is considered "filled" if its computed brightness is below the given threshold.
    /// </summary>
    /// <param name="image">
    /// An ImageSharp Image of type Rgba32 to analyze.
    /// </param>
    /// <param name="brightnessThreshold">
    /// The brightness threshold (0-255) below which a pixel is considered "filled".
    /// Default value is 240, meaning pixels with brightness less than 240 are considered filled.
    /// </param>
    /// <returns>
    /// A double representing the fraction of the image that is filled.
    /// For example, a return value of 0.05 means 5% of the image pixels are considered filled.
    /// </returns>
    public static double GetFilledAreaPercentage(Image<Rgba32> image, byte brightnessThreshold = 240)
    {
        int width = image.Width;
        int height = image.Height;
        int totalPixels = width * height;
        int filledPixels = 0;

        // Use the root frame to access pixel data
        var frame = image.Frames.RootFrame;

        for (int y = 0; y < height; y++)
        {
            // Use DangerousGetPixelRowMemory instead of GetPixelRowSpan
            Span<Rgba32> pixelRow = frame.DangerousGetPixelRowMemory(y).Span;

            for (int x = 0; x < width; x++)
            {
                Rgba32 pixel = pixelRow[x];

                // Compute brightness (average of R, G, B)
                int brightness = (pixel.R + pixel.G + pixel.B) / 3;

                if (brightness < brightnessThreshold)
                {
                    filledPixels++;
                }
            }
        }

        double fillRatio = (double)filledPixels / totalPixels;
        return fillRatio;
    }

    #region private helpers

    #region detection section

    /// <summary>
    /// Converts an ImageSharp Image&lt;Rgba32&gt; to an OpenCvSharp Mat (BGRA).
    /// </summary>
    private static Mat ToMat(Image<Rgba32> source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        // We'll store 8-bit BGRA in the Mat
        var mat = new Mat(source.Height, source.Width, MatType.CV_8UC4);

        // For older ImageSharp versions, use source[x, y] directly
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Rgba32 pixel = source[x, y];
                // Note OpenCV default is typically BGR(A),
                // so we place data in BGRA order
                mat.Set(y, x, new Vec4b(pixel.B, pixel.G, pixel.R, pixel.A));
            }
        }

        return mat;
    }
    public static Image<Rgba32> ToImageSharpRgba32(this Mat mat)
    {
        if (mat.Empty())
            throw new ArgumentException("Input Mat is empty.", nameof(mat));

        int width = mat.Width;
        int height = mat.Height;
        int channels = mat.Channels();

        // Create the ImageSharp image
        var image = new Image<Rgba32>(width, height);

        switch (channels)
        {
            // --- 1 channel: Grayscale => replicate same value into R, G, B
            case 1:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte gray = mat.At<byte>(y, x);
                        image[x, y] = new Rgba32(gray, gray, gray, 255);
                    }
                }
                break;

            // --- 3 channels: BGR => convert to R,G,B + alpha=255
            case 3:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Vec3b has B,G,R in .Item0, .Item1, .Item2
                        var color = mat.Get<Vec3b>(y, x);
                        image[x, y] = new Rgba32(
                            r: color.Item2,
                            g: color.Item1,
                            b: color.Item0,
                            a: 255);
                    }
                }
                break;

            // --- 4 channels: BGRA => convert to R,G,B,A
            case 4:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Vec4b has B,G,R,A in .Item0, .Item1, .Item2, .Item3
                        var color = mat.Get<Vec4b>(y, x);
                        image[x, y] = new Rgba32(
                            r: color.Item2,
                            g: color.Item1,
                            b: color.Item0,
                            a: color.Item3);
                    }
                }
                break;

            default:
                throw new NotSupportedException(
                    $"Mat with {channels} channels is not supported for direct conversion.");
        }

        return image;
    }

    /// <summary>
    /// Detects all ArUco markers in the given image, looks for pairs of IDs (2*n, 2*n + 1),
    /// and extracts the rectangular region between them (excluding the markers themselves).
    /// Returns a list of (boxId, croppedMat).
    /// </summary>
    /// <param name="inputImage">Input BGR image.</param>
    /// <returns>A list of (boxId, extracted Mat) for each found box.</returns>
    private static List<(int BoxId, Mat Cropped)> ExtractAllAnchorBoxes(Mat inputImage)
    {
        if (inputImage == null || inputImage.Empty())
            throw new ArgumentException("Input image is null or empty.", nameof(inputImage));

        using var gray = new Mat();
        Cv2.CvtColor(inputImage, gray, ColorConversionCodes.BGR2GRAY);

        var parameters = new DetectorParameters()
        {
            CornerRefinementMethod = CornerRefineMethod.Subpix
        };

        var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);

        CvAruco.DetectMarkers(
            gray,
            dict,
            out Point2f[][] corners,
            out int[] ids,
            parameters,
            out _
        );

        if (ids.Length == 0)
            return new List<(int, Mat)>();

        var markerCornersDict = new Dictionary<int, Point2f[]>();
        for (int i = 0; i < ids.Length; i++)
            markerCornersDict[ids[i]] = corners[i];

        var uniqueIds = new List<int>(markerCornersDict.Keys);
        uniqueIds.Sort();

        var results = new List<(int BoxId, Mat Cropped)>();

        foreach (int markerId in uniqueIds)
        {
            if (markerId % 2 != 0)
                continue;

            int nextId = markerId + 1;
            if (!markerCornersDict.ContainsKey(nextId))
                continue;

            int boxId = markerId / 2;

            var cornersTopLeft = markerCornersDict[markerId];
            var cornersBottomRight = markerCornersDict[nextId];

            var cropped = CropByTwoMarkers(inputImage, cornersTopLeft, cornersBottomRight);

            results.Add((boxId, cropped));
        }

        return results;
    }

    /// <summary>
    ///  Crops the rectangle that lies BETWEEN the given TL and BR ArUco markers,
    ///  optionally skips a safety margin (<paramref name="innerPadding"/>), paints
    ///  the markers out (transparent or black), and auto-orients the result so
    ///  that TL really ends up at the top-left corner of the returned Mat.
    /// </summary>
    private static Mat CropByTwoMarkers(
        Mat inputImage,
        Point2f[] markerTL,              // must be the SMALLER-ID marker
        Point2f[] markerBR,              // must be the LARGER-ID marker
        int innerPadding = 2,
        bool transparent = true,
        bool autoOrient = true)
    {
        Rect tlRect = Cv2.BoundingRect(markerTL);
        Rect brRect = Cv2.BoundingRect(markerBR);

        int left = tlRect.Right + innerPadding;
        int top = tlRect.Bottom + innerPadding;
        int right = brRect.Left - innerPadding;
        int bottom = brRect.Top - innerPadding;

        // clamp & ensure non-empty
        left = Math.Clamp(left, 0, inputImage.Width - 2);
        top = Math.Clamp(top, 0, inputImage.Height - 2);
        right = Math.Clamp(right, left + 1, inputImage.Width - 1);
        bottom = Math.Clamp(bottom, top + 1, inputImage.Height - 1);

        var inner = new Rect(left, top, right - left, bottom - top);
        var roi = new Mat(inputImage, inner).Clone();

        Scalar hole = transparent ? new Scalar(0, 0, 0, 0) : Scalar.Black;

        void ZapMarker(Point2f[] marker)
        {
            Rect r = Cv2.BoundingRect(marker);
            r.X = r.X - inner.X;           // ROI-local coordinates
            r.Y = r.Y - inner.Y;
            r.Width = Math.Min(r.Width, roi.Width - r.X);
            r.Height = Math.Min(r.Height, roi.Height - r.Y);
            Cv2.Rectangle(roi, r, hole, thickness: -1);
        }

        ZapMarker(markerTL);
        ZapMarker(markerBR);

        if (autoOrient)
        {
            static Point2f C(Point2f[] pts) =>
                new((float)pts.Average(p => p.X), (float)pts.Average(p => p.Y));

            float dx = C(markerBR).X - C(markerTL).X;
            float dy = C(markerBR).Y - C(markerTL).Y;

            if (dx < 0 && dy < 0) Cv2.Rotate(roi, roi, RotateFlags.Rotate180);
        }

        return roi;
    }


    public static Image<Rgba32> TrimAndBinarise(
        Image<Rgba32> src,
        int blurSize = 3,
        double blurSigma = 1.0,
        AdaptiveThresholdTypes adaptiveMethod = AdaptiveThresholdTypes.GaussianC,
        ThresholdTypes thresholdType = ThresholdTypes.Binary,
        int blockSize = 25,
        double C = 10,
        int targetWidth = 300,
        int targetHeight = 150
    )
    {
        if (src is null || src.Width == 0 || src.Height == 0)
            throw new ArgumentException("Input image is null or empty.", nameof(src));

        // Step 1: Convert ImageSharp image to OpenCv Mat (grayscale)
        using Mat matSrc = new(src.Height, src.Width, MatType.CV_8UC4);
        src.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < src.Height; y++)
            {
                Span<Rgba32> row = pa.GetRowSpan(y);
                for (int x = 0; x < src.Width; x++)
                {
                    var px = row[x];
                    matSrc.Set(y, x, new Vec4b(px.B, px.G, px.R, px.A));
                }
            }
        });

        Mat gray = new();
        Cv2.CvtColor(matSrc, gray, ColorConversionCodes.BGRA2GRAY);

        // Step 2: Gaussian blur (optional but recommended)
        if (blurSize > 0)
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(blurSize, blurSize), blurSigma);

        // Step 3: Adaptive Thresholding
        Mat thresh = new();
        Cv2.AdaptiveThreshold(gray, thresh, 255, adaptiveMethod, thresholdType, blockSize, C);

        // Step 4: Crop whitespace
        var boundingRect = GetTightBoundingBox(thresh);
        //var contours = Cv2.FindContoursAsArray(thresh, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        //var boundingRect = contours.Select(Cv2.BoundingRect).OrderByDescending(r => r.Width * r.Height).FirstOrDefault();

        if (boundingRect.Width == 0 || boundingRect.Height == 0)
            return new Image<Rgba32>(targetWidth, targetHeight); // empty fallback

        Mat cropped = new(thresh, boundingRect);

        // Step 5: Resize and center signature
        Mat resized = new(targetHeight, targetWidth, MatType.CV_8UC1, Scalar.White);
        float scale = Math.Min((float)targetWidth / boundingRect.Width, (float)targetHeight / boundingRect.Height);

        Mat resizedSig = new();
        Cv2.Resize(cropped, resizedSig, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Area);

        int offsetX = (targetWidth - resizedSig.Width) / 2;
        int offsetY = (targetHeight - resizedSig.Height) / 2;
        resizedSig.CopyTo(new Mat(resized, new Rect(offsetX, offsetY, resizedSig.Width, resizedSig.Height)));

        // Convert back to ImageSharp
        Image<Rgba32> result = new(targetWidth, targetHeight);
        result.ProcessPixelRows(pa =>
        {
            for (int y = 0; y < targetHeight; y++)
            {
                Span<Rgba32> row = pa.GetRowSpan(y);
                for (int x = 0; x < targetWidth; x++)
                {
                    byte val = resized.At<byte>(y, x);
                    row[x] = new Rgba32(val, val, val, 255);
                }
            }
        });

        // Dispose temporary mats
        gray.Dispose();
        thresh.Dispose();
        cropped.Dispose();
        resized.Dispose();
        resizedSig.Dispose();

        return result;
    }

    private static Rect GetTightBoundingBox(Mat binaryMat)
    {
        int minX = binaryMat.Width, maxX = 0;
        int minY = binaryMat.Height, maxY = 0;

        for (int y = 0; y < binaryMat.Height; y++)
        {
            for (int x = 0; x < binaryMat.Width; x++)
            {
                if (binaryMat.At<byte>(y, x) == 0) // black pixel
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        if (minX >= maxX || minY >= maxY)
            return new Rect(0, 0, binaryMat.Width, binaryMat.Height);

        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }


    #endregion

    private static void ValidateGeneratorOptions(InkAnchorGeneratorOptions options)
    {
        if (options.PixelWidth < InkAnchorGeneratorOptions.BoxMinWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PixelWidth),
                options.PixelWidth,
                $"minimum allowed length for box width is {InkAnchorGeneratorOptions.BoxMinWidth} pixels");
        }

        if (options.PixelHeight < InkAnchorGeneratorOptions.BoxMinHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PixelHeight),
                options.PixelHeight,
                $"minimum allowed length for box height is {InkAnchorGeneratorOptions.BoxMinHeight} pixels");
        }
    }

    private static Task<(Image<Rgba32>? image, string? svg)>
        GenerateIngAnchorBoxAsync(InkAnchorGeneratorOptions options, bool generateSvg)
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
            var topLeftMarker = GenerateArucoMarkerImage(boxId * 2, markerSize, markerBorderBits);
            var bottomRightMarker = GenerateArucoMarkerImage(boxId * 2 + 1, markerSize, markerBorderBits);

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

    public static string ToSvgHex(this Color color)
    {
        var rgba = color.ToPixel<Rgba32>();
        return $"#{rgba.R:X2}{rgba.G:X2}{rgba.B:X2}";
    }

    private static string GenerateSvg(InkAnchorGeneratorOptions options, byte boxId, int markerSize, int markerBorderBits, int labelExtraTop, int boxHeight, int width, int totalHeight, int markerPadding)
    {
        var svgTopLeft = GenerateArucoMarkerSvg(boxId * 2, markerSize, markerBorderBits);
        var svgBottomRight = GenerateArucoMarkerSvg(boxId * 2 + 1, markerSize, markerBorderBits);

        string fillColor = options.FillColor?.ToSvgHex() ?? "none";

        string labelSvg = string.Empty;
        if (options.BoxLabel is { } labelOpt)
        {
            var extraY = 12;
            if(labelOpt.LabelPlacement == BoxLabelPlacement.TopOutsideBox)
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

    private static Image<Rgba32> GenerateArucoMarkerImage(int markerId, int pixelSize, int borderBits)
    {
        var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);

        using var markerMat = CustomArucoDrawer.DrawArucoMarkerManually(
            dict, markerId, pixelSize, borderBits);

        var image = new Image<Rgba32>(markerMat.Width, markerMat.Height);
        for (int y = 0; y < markerMat.Height; y++)
        {
            for (int x = 0; x < markerMat.Width; x++)
            {
                byte value = markerMat.At<byte>(y, x);
                image[x, y] = value == 1
                    ? new Rgba32(255, 255, 255)
                    : new Rgba32(0, 0, 0);
            }
        }
        return image;
    }

    private static string GenerateArucoMarkerSvg(int markerId, int pixelSize, int borderBits)
    {
        var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
        return CustomArucoDrawer.GenerateArucoMarkerSvgManually(
            dict, markerId, pixelSize, borderBits);
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

    private static string EscapeXml(string input) =>
        System.Security.SecurityElement.Escape(input) ?? "";


    #endregion
}
