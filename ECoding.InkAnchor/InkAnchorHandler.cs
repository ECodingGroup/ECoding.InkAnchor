using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using OpenCvSharp.Internal;

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
    /// and extracts the rectangular region between them. Returns a list of (boxId, croppedMat).
    /// </summary>
    /// <param name="inputImage">Input BGR image.</param>
    /// <returns>A list of (boxId, extracted Mat) for each found box.</returns>
    private static List<(int BoxId, Mat Cropped)> ExtractAllAnchorBoxes(Mat inputImage)
    {
        if (inputImage == null || inputImage.Empty())
            throw new ArgumentException("Input image is null or empty.", nameof(inputImage));

        // 1) Convert to grayscale (helps detection reliability)
        using var gray = new Mat();
        Cv2.CvtColor(inputImage, gray, ColorConversionCodes.BGR2GRAY);

        // 2) Detect markers
        Point2f[][] rejectedCorners;
        var parameters = new DetectorParameters()
        {
            AdaptiveThreshWinSizeMin = 3,
            AdaptiveThreshWinSizeMax = 23,
            AdaptiveThreshWinSizeStep = 10,
            AdaptiveThreshConstant = 7,         
            MinMarkerPerimeterRate = 0.02,    
            MaxMarkerPerimeterRate = 4.0f,
            CornerRefinementMethod = CornerRefineMethod.Subpix,
            DetectInvertedMarker = true
            // etc.
        };
        var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);

        CvAruco.DetectMarkers(
            image: gray,
            dictionary: dict,
            corners: out Point2f[][] corners,
            ids: out int[] ids,
            parameters: parameters,
            rejectedImgPoints: out rejectedCorners
        );

        if (ids.Length == 0)
            return new List<(int, Mat)>(); // no markers => no boxes

        // 3) Store corners in a dictionary so we can quickly find them by marker ID
        //    (Because corners[i] corresponds to ids[i])
        var markerCornersDict = new Dictionary<int, Point2f[]>();
        for (int i = 0; i < ids.Length; i++)
        {
            markerCornersDict[ids[i]] = corners[i];
        }

        // 4) For each "even" ID, check if (ID+1) is also present. 
        //    If so, compute bounding rectangle between them => "boxId = ID/2".
        var results = new List<(int BoxId, Mat Cropped)>();

        // Gather all unique IDs, then sort them so we check pairs in ascending order
        var uniqueIds = new List<int>(markerCornersDict.Keys);
        uniqueIds.Sort();

        foreach (int markerId in uniqueIds)
        {
            // We only form pairs from even => even+1
            // So if markerId is even, check markerId+1
            if (markerId % 2 == 0)
            {
                int nextId = markerId + 1;
                if (markerCornersDict.ContainsKey(nextId))
                {
                    // We found a pair (markerId, markerId+1)
                    // => top-left marker corners, bottom-right marker corners
                    int boxId = markerId / 2; // e.g. if markerId=2 => boxId=1

                    var cornersTopLeft = markerCornersDict[markerId];
                    var cornersBottomRight = markerCornersDict[nextId];

                    // Convert these two marker corners to a bounding rectangle
                    var cropped = CropByTwoMarkers(inputImage, cornersTopLeft, cornersBottomRight);

                    results.Add((boxId, cropped));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a bounding rectangle from the top-left marker corners and the bottom-right marker corners,
    /// then crops that region from the input image. 
    /// </summary>
    private static Mat CropByTwoMarkers(Mat inputImage, Point2f[] cornersTopLeft, Point2f[] cornersBottomRight)
    {
        // 1) Identify the actual top-left corner from cornersTopLeft 
        //    (lowest X+Y => "naive top-left" in image space)
        var markerTLCorner = GetTopLeftCorner(cornersTopLeft);

        // 2) Identify the actual bottom-right corner from cornersBottomRight 
        //    (highest X+Y => "naive bottom-right" in image space)
        var markerBRCorner = GetBottomRightCorner(cornersBottomRight);

        // 3) bounding rectangle
        float minX = Math.Min(markerTLCorner.X, markerBRCorner.X);
        float maxX = Math.Max(markerTLCorner.X, markerBRCorner.X);
        float minY = Math.Min(markerTLCorner.Y, markerBRCorner.Y);
        float maxY = Math.Max(markerTLCorner.Y, markerBRCorner.Y);

        // Clip to image boundaries
        minX = Math.Max(minX, 0);
        minY = Math.Max(minY, 0);
        maxX = Math.Min(maxX, inputImage.Width - 1);
        maxY = Math.Min(maxY, inputImage.Height - 1);

        if (maxX - minX < 5 || maxY - minY < 5)
        {
            // if the rectangle is too small, we can skip it or throw an exception
            throw new Exception("Markers are too close or invalid bounding box.");
        }

        var roiRect = new Rect(
            (int)minX,
            (int)minY,
            (int)(maxX - minX),
            (int)(maxY - minY)
        );

        return new Mat(inputImage, roiRect);
    }

    /// <summary>
    /// For a set of marker corners, find the corner with the smallest X+Y => naive "top-left."
    /// </summary>
    private static Point2f GetTopLeftCorner(Point2f[] corners)
    {
        Point2f best = corners[0];
        float minSum = best.X + best.Y;

        for (int i = 1; i < corners.Length; i++)
        {
            float s = corners[i].X + corners[i].Y;
            if (s < minSum)
            {
                best = corners[i];
                minSum = s;
            }
        }
        return best;
    }

    /// <summary>
    /// For a set of marker corners, find the corner with the largest X+Y => naive "bottom-right."
    /// </summary>
    private static Point2f GetBottomRightCorner(Point2f[] corners)
    {
        Point2f best = corners[0];
        float maxSum = best.X + best.Y;

        for (int i = 1; i < corners.Length; i++)
        {
            float s = corners[i].X + corners[i].Y;
            if (s > maxSum)
            {
                best = corners[i];
                maxSum = s;
            }
        }
        return best;
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
                    ctx.Draw(
                        options.Border.Color,
                        options.Border.Thickness,
                        new RectangleF(0, labelExtraTop, width - 1, boxHeight - 1));
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
            // -- SVG GENERATION --
            var svgTopLeft = GenerateArucoMarkerSvg(boxId * 2, markerSize, markerBorderBits);
            var svgBottomRight = GenerateArucoMarkerSvg(boxId * 2 + 1, markerSize, markerBorderBits);

            string fillColor = options.FillColor?.ToHex() ?? "none";
            string borderColor = options.Border?.Color.ToHex() ?? "#000000";
            int borderWidth = options.Border?.Thickness ?? 1;

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

            // We inline the two marker SVGs in <g> elements
            string svg = $"""
            <svg width="{width}" height="{totalHeight}" xmlns="http://www.w3.org/2000/svg">
              <rect x="0" y="{labelExtraTop}" width="{width}" height="{boxHeight}" 
                    fill="{fillColor}" stroke="{borderColor}" stroke-width="{borderWidth}" />
              {labelSvg}
              
              <g transform="translate({markerPadding},{labelExtraTop + markerPadding})">
                {svgTopLeft}
              </g>
              
              <g transform="translate({width - markerSize - markerPadding},{labelExtraTop + boxHeight - markerSize - markerPadding})">
                {svgBottomRight}
              </g>
            </svg>
            """;

            return Task.FromResult<(Image<Rgba32>?, string?)>((null, svg));
        }
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
                    ? new Rgba32(0, 0, 0)
                    : new Rgba32(255, 255, 255);
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
            BoxLabelPlacement.TopInsideBox => labelOffsetTop + padding,
            BoxLabelPlacement.BottomInsideBox => labelOffsetTop + boxHeight - label.FontSize - padding,
            BoxLabelPlacement.TopOutsideBox => padding,
            BoxLabelPlacement.BottomOutsideBox => labelOffsetTop + boxHeight + padding,
            _ => labelOffsetTop + boxHeight - label.FontSize - padding
        };
    }

    private static string EscapeXml(string input) =>
        System.Security.SecurityElement.Escape(input) ?? "";


    #endregion
}
