using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ECoding.InkAnchor;

public class AnchorBoxDetectionOptions
{
    /// <summary>
    /// Number of border bits around the ArUco marker. Default 1.
    /// This is the quiet zone (white border) required around the marker for proper detection.
    /// </summary>
    public int BorderBits { get; set; } = 1;

    /// <summary>
    /// Minimum cell size in pixels when searching for markers. Default 4 px.
    /// Markers with cells smaller than this will not be detected.
    /// </summary>
    public int MinCellPx { get; set; } = 4;

    /// <summary>
    /// Maximum cell size in pixels when searching for markers. Default 14 px.
    /// Markers with cells larger than this will not be detected.
    /// </summary>
    public int MaxCellPx { get; set; } = 14;

    /// <summary>
    /// Array of rotation angles (in degrees) to try when detecting markers.
    /// If null or empty, no rotation-based detection will be performed.
    /// </summary>
    public float[] Rotations { get; set; } = [];

    /// <summary>
    /// Threshold value for converting the image to binary (black and white).
    /// If null, no binarization will be applied and the image will be processed as-is.
    /// Valid range is 0.0 to 1.0 when specified.
    /// </summary>
    public float? BinaryThreshold { get; set; }
}