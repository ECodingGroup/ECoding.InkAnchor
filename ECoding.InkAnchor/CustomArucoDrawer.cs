using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Text;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace ECoding.InkAnchor;

public static class CustomArucoDrawer
{
    public static Image<Rgba32> DrawArucoMarkerManually(int markerId, int sidePixels, int borderBits = 1)
    {
        var markerBits = ArucoDict4x4_50.GetMarkerBits(markerId);
        int baseSize = markerBits.GetLength(0);
        int extendedSize = baseSize + 2 * borderBits;
        int cellSize = sidePixels / extendedSize;

        var img = new Image<Rgba32>(extendedSize * cellSize, extendedSize * cellSize);

        img.Mutate(ctx =>
        {
            ctx.Fill(Color.Black); // Border

            for (int y = 0; y < baseSize; y++)
            {
                for (int x = 0; x < baseSize; x++)
                {
                    if (markerBits[y, x] != 0)
                    {
                        var cellRect = new Rectangle(
                            (x + borderBits) * cellSize,
                            (y + borderBits) * cellSize,
                            cellSize,
                            cellSize);
                        ctx.Fill(Color.White, cellRect);
                    }
                }
            }
        });

        return img;
    }


    /// <summary>
    /// Produce an SVG string by manually decoding the dictionary bits and drawing them.
    /// </summary>
    public static string GenerateArucoMarkerSvgManually(int markerId, int sidePixels, int borderBits = 1)
    {
        var markerBits = ArucoDict4x4_50.GetMarkerBits(markerId);
        int baseSize = markerBits.GetLength(0);
        int extendedSize = baseSize + 2 * borderBits;
        float cellSize = (float)sidePixels / extendedSize;

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{sidePixels}\" height=\"{sidePixels}\">");
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{sidePixels}\" height=\"{sidePixels}\" fill=\"black\"/>");

        for (int y = 0; y < baseSize; y++)
        {
            for (int x = 0; x < baseSize; x++)
            {
                if (markerBits[y, x] != 0)
                {
                    float sx = (x + borderBits) * cellSize;
                    float sy = (y + borderBits) * cellSize;
                    sb.AppendLine($"<rect x=\"{sx}\" y=\"{sy}\" width=\"{cellSize}\" height=\"{cellSize}\" fill=\"white\"/>");
                }
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
