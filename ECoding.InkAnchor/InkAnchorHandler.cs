using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using System.Text.Json;
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
    /// Crops the region between two markers (A at top-left, B at bottom-right),
    /// excluding the bounding rectangles of the markers themselves.
    /// </summary>
    private static Mat CropByTwoMarkers(Mat inputImage, Point2f[] markerA, Point2f[] markerB)
    {
        // Collect all points from both markers
        var allPoints = markerA.Concat(markerB).ToList();

        // Find the minimum and maximum X and Y coordinates
        float minX = allPoints.Min(p => p.X);
        float minY = allPoints.Min(p => p.Y);
        float maxX = allPoints.Max(p => p.X);
        float maxY = allPoints.Max(p => p.Y);

        // Identify marker bounding rectangles
        var rectA = Cv2.BoundingRect(markerA);
        var rectB = Cv2.BoundingRect(markerB);

        // Dynamically determine inner cropping region excluding markers
        int cropX = (int)Math.Min(rectA.Right, rectB.Right);
        int cropY = (int)Math.Min(rectA.Bottom, rectB.Bottom);
        int cropRight = (int)Math.Max(rectA.Left, rectB.Left);
        int cropBottom = (int)Math.Max(rectA.Top, rectB.Top);

        // Correct for inverted orientations
        int innerX = Math.Min(cropX, cropRight);
        int innerY = Math.Min(cropY, cropBottom);
        int innerWidth = Math.Abs(cropRight - cropX);
        int innerHeight = Math.Abs(cropBottom - cropY);

        // Ensure cropping area is within image boundaries
        innerX = Math.Clamp(innerX, 0, inputImage.Width - 1);
        innerY = Math.Clamp(innerY, 0, inputImage.Height - 1);
        innerWidth = Math.Clamp(innerWidth, 1, inputImage.Width - innerX);
        innerHeight = Math.Clamp(innerHeight, 1, inputImage.Height - innerY);

        return new Mat(inputImage, new Rect(innerX, innerY, innerWidth, innerHeight)).Clone();
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

                    var textOptions = new RichTextOptions(font)
                    {
                        Origin = new PointF(width / 2f, labelY),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    ctx.DrawText(textOptions, label.LabelText, label.Color);
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

    private static string GenerateSvg(InkAnchorGeneratorOptions options, byte boxId, int markerSize, int markerBorderBits, int labelExtraTop, int boxHeight, int width, int totalHeight, int markerPadding)
    {
        var svgTopLeft = GenerateArucoMarkerSvg(boxId * 2, markerSize, markerBorderBits);
        var svgBottomRight = GenerateArucoMarkerSvg(boxId * 2 + 1, markerSize, markerBorderBits);

        string fillColor = options.FillColor?.ToHex() ?? "none";

        string labelSvg = string.Empty;
        if (options.BoxLabel is { } labelOpt)
        {
            float labelY = CalculateLabelY(labelOpt, labelExtraTop, boxHeight);
            labelSvg = $"""
                    <text x="{width / 2}" 
                          y="{labelY}" 
                          text-anchor="middle" 
                          font-size="{labelOpt.FontSize}" 
                          fill="{labelOpt.Color.ToHex()}" 
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

            string stroke = border.Color.ToHex();
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
