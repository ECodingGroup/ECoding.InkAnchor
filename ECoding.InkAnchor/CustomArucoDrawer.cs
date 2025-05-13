using OpenCvSharp;
using OpenCvSharp.Aruco;
using System.Text;

namespace ECoding.InkAnchor;

public static class CustomArucoDrawer
{
    public static Mat DrawArucoMarkerManually(Dictionary dictionary, int markerId, int sidePixels, int borderBits = 1)
    {
        int baseSize = dictionary.MarkerSize;
        if (baseSize <= 0)
            throw new Exception("Dictionary.MarkerSize is zero or invalid.");

        // Get the entire table of marker codes
        using Mat bytesList = dictionary.BytesList;

        if (markerId < 0 || markerId >= bytesList.Rows)
            throw new ArgumentOutOfRangeException(nameof(markerId),
                $"markerId must be between 0 and {bytesList.Rows - 1} for this dictionary.");

        // -- 1) Extract the row for the requested markerId, then reshape to 1 channel --
        byte[] rowDataLocal;
        using (Mat row = bytesList.Row(markerId))
        using (Mat rowSingleChannel = row.Reshape(1, 1))
        {
            // Now it's 1×(row.Cols * row.Channels), 1 channel
            bool success = rowSingleChannel.GetArray(out byte[] rowBytes);
            if (!success)
                throw new Exception("Failed to get row data from matrix.");

            rowDataLocal = rowBytes;
        }

        // -- 2) Decode marker bits (4×4, 5×5, etc.) from rowDataLocal --
        int totalBits = baseSize * baseSize;
        var markerCells = new Mat(baseSize, baseSize, MatType.CV_8UC1, Scalar.All(0));

        for (int i = 0; i < totalBits; i++)
        {
            int r = i / baseSize;
            int c = i % baseSize;

            // Checking the bit in rowDataLocal[i >> 3]
            bool bitIsSet = (rowDataLocal[i >> 3] & (128 >> (i & 7))) != 0;
            if (bitIsSet)
                markerCells.Set(r, c, 1); // 1 => black
        }

        // -- 3) Add the border (borderBits) around the marker pattern --
        int extendedSize = baseSize + (2 * borderBits);
        var markerWithBorder = new Mat(extendedSize, extendedSize, MatType.CV_8UC1, Scalar.All(0));

        for (int r = 0; r < baseSize; r++)
        {
            for (int c = 0; c < baseSize; c++)
            {
                byte cell = markerCells.Get<byte>(r, c);
                markerWithBorder.Set(r + borderBits, c + borderBits, cell);
            }
        }

        // -- 4) Scale up to sidePixels×sidePixels using nearest neighbor --
        var finalMarker = new Mat();
        Cv2.Resize(markerWithBorder, finalMarker,
            new Size(sidePixels, sidePixels), 0, 0, InterpolationFlags.Nearest);

        return finalMarker;
    }


    /// <summary>
    /// Produce an SVG string by manually decoding the dictionary bits and drawing them.
    /// </summary>
    public static string GenerateArucoMarkerSvgManually(Dictionary dictionary, int markerId, int sidePixels, int borderBits = 1)
    {
        int baseSize = dictionary.MarkerSize;
        if (baseSize <= 0)
            throw new Exception("Dictionary.MarkerSize is zero or invalid.");

        byte[] rowDataLocal;
        using Mat bytesList = dictionary.BytesList;

        if (markerId < 0 || markerId >= bytesList.Rows)
            throw new ArgumentOutOfRangeException(nameof(markerId));

        using (Mat row = bytesList.Row(markerId))
        using (Mat rowSingleChannel = row.Reshape(1, 1))
        {
            if (!rowSingleChannel.GetArray(out byte[] rowBytes))
                throw new Exception("Failed to read dictionary row data.");

            rowDataLocal = rowBytes;
        }

        int totalBits = baseSize * baseSize;
        var markerCells = new byte[baseSize, baseSize];
        for (int i = 0; i < totalBits; i++)
        {
            int r = i / baseSize;
            int c = i % baseSize;
            bool bitIsSet = (rowDataLocal[i >> 3] & (128 >> (i & 7))) != 0;
            if (bitIsSet)
                markerCells[r, c] = 1;
        }

        int extendedSize = baseSize + (2 * borderBits);
        var markerWithBorder = new byte[extendedSize, extendedSize];
        for (int r = 0; r < baseSize; r++)
            for (int c = 0; c < baseSize; c++)
                markerWithBorder[r + borderBits, c + borderBits] = markerCells[r, c];

        var sb = new StringBuilder();
        // Group wrapper
        sb.AppendLine($"<g transform=\"scale({(float)sidePixels / extendedSize})\">");

        // White background rect
        sb.AppendLine($"  <rect x=\"0\" y=\"0\" width=\"{extendedSize}\" height=\"{extendedSize}\" fill=\"black\"/>");

        // Draw black pixels
        for (int y = 0; y < extendedSize; y++)
        {
            for (int x = 0; x < extendedSize; x++)
            {
                if (markerWithBorder[y, x] == 1)
                    sb.AppendLine($"  <rect x=\"{x}\" y=\"{y}\" width=\"1\" height=\"1\" fill=\"white\"/>");
            }
        }

        sb.AppendLine("</g>");
        return sb.ToString();
    }
}
