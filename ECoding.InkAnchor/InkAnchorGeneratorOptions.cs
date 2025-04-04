using SixLabors.ImageSharp;

namespace ECoding.InkAnchor;

public class InkAnchorGeneratorOptions
{
    internal static int MaxLabelLength = 100;
    internal static int BoxMinWidth = 30;
    internal static int BoxMinHeight = 30;

    public InkAnchorGeneratorOptions(byte boxId, int pixelWidth, int pixelHeight)
    {
        BoxId = boxId;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public byte BoxId { get; private set; }

    /// <summary>
    /// How many pixels wide/tall each ArUco marker should be. Default 60 px.
    /// This is the final rendered size in the signature box (not the raw cell count).
    /// </summary>
    public int MarkerPixelSize { get; set; } = 60;

    /// <summary>
    /// Number of border cells around the 4×4 marker. Default 1. 
    /// Increase to 2+ for easier detection if printing very small.
    /// </summary>
    public int MarkerBorderBits { get; set; } = 1;

    /// <summary>
    /// The padding between the border and the marker. 
    /// </summary>
    public int MarkerPadding { get; set; } = 5;

    /// <summary>
    /// The box label that will be embedded. 
    /// Max allowed text length is 100 characters.
    /// </summary>
    public InkAnchorLabel? BoxLabel { get; set; }

    /// <summary>
    /// The width of the generated box. The value has to be at least 30 pixels
    /// </summary>
    public int PixelWidth { get; set; }
    /// <summary>
    /// The Height of the generated box. The value has to be at least 30 pixels
    /// </summary>
    public int PixelHeight { get; set; }

    /// <summary>
    /// The fill color for the background of the box. Default is transparent
    /// </summary>
    public Color? FillColor { get; set; }

    /// <summary>
    /// The border settings for the box
    /// </summary>
    public InkAnchorBorder? Border { get; set; }
}

public class InkAnchorLabel
{
    public InkAnchorLabel(string labelText, BoxLabelPlacement labelPlacement = BoxLabelPlacement.BottomOutsideBox, int fontSize = 14, Color? color = null, string font = "Arial")
    {
        if (!color.HasValue)
        { color = Color.Black; }

        if(fontSize <= 5 && fontSize > 25)
        { throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, $"FontSize has to be between values 5 and 25."); }

        if (string.IsNullOrEmpty(font))
        { throw new ArgumentNullException(nameof(font)); }

        if(!string.IsNullOrEmpty(labelText) && labelText.Length > InkAnchorGeneratorOptions.MaxLabelLength)
        {
            throw new ArgumentOutOfRangeException(nameof(labelText), labelText.Length, $"labelText.Length has to be between max {InkAnchorGeneratorOptions.MaxLabelLength}. Characters");
        }

        Color = color.Value;
        FontSize = fontSize;
        Font = font;
        LabelPlacement = labelPlacement;
        LabelText = labelText;
    }
    public string LabelText { get; private set; }
    public Color Color { get; private set; }
    public int FontSize { get; private set; }
    public string Font { get; private set; }

    public BoxLabelPlacement LabelPlacement { get; private set; }
}

public class InkAnchorBorder
{
    public InkAnchorBorder(Color? color, int thickness = 1)
    {
        if (!color.HasValue)
        { color = Color.Black; }

        if(thickness <= 0 || thickness > 5)
        { throw new ArgumentOutOfRangeException(nameof(thickness), thickness, $"Thickness has to be between values 1 and 5. (Max 5 pixels thick)"); }

        Color = color.Value;
        Thickness = thickness;
    }

    public Color Color { get; private set; }
    public int Thickness { get; private set; }

}

public enum BoxLabelPlacement
{
    /// <summary>
    /// This will place the label to top middle of the box, inside it. 
    /// </summary>
    TopInsideBox = 1,
    /// <summary>
    /// This will place the label to bottom middle of the box, inside it. 
    /// </summary>
    BottomInsideBox = 2,
    /// <summary>
    /// This will place the label to top middle of the box, outside it. 
    /// </summary>
    TopOutsideBox = 3,
    /// <summary>
    /// This will place the label to bottom middle of the box, outside it. 
    /// </summary>
    BottomOutsideBox = 4,
}
