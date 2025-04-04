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
    public static async Task<Image<Rgba32>> GenerateIngAnchorBoxImageAsync(InkAnchorGeneratorOptions options)
    {
        var generationResult = await GenerateIngAnchorBoxAsync(options, generateSvg: false);
        return generationResult.image!;
    }

    public static async Task<string> GenerateIngAnchorBoxSvgAsync(InkAnchorGeneratorOptions options)
    {
        var generationResult = await GenerateIngAnchorBoxAsync(options, generateSvg: true);
        return generationResult.svg!;
    }

    #region private helpers

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
