# ECoding.InkAnchor

ECoding.InkAnchor is a versatile .NET 9 library designed to easily generate and analyze customizable anchor boxes using ArUco markers. It enables structured image generation, automated extraction, and evaluation of image content.

---

## Key Features

- **Anchor Box Generation**: Create raster or SVG images containing labeled and customizable anchor boxes defined by ArUco markers.
- **Marker Detection and Extraction**: Efficiently detect anchor boxes from scanned images and extract content between ArUco markers.
- **Content Evaluation**: Measure the filled area percentage within extracted image sections, ideal for automated processing of handwritten or marked content.

---

## Installation

Install the library easily via NuGet:

```bash
dotnet add package ECoding.InkAnchor
```

---

## Usage

### Generating Anchor Box Images

```csharp
var options = new InkAnchorGeneratorOptions(boxId: 1, pixelWidth: 400, pixelHeight: 200)
{
    FillColor = Color.White,
    Border = new InkAnchorBorder(Color.Black, thickness: 2, borderStyle: InkAnchorBorder.BorderStyle.Dashed),
    BoxLabel = new InkAnchorLabel("Signature Box", BoxLabelPlacement.TopOutsideBox, fontSize: 14, color: Color.Black)
};

var image = await InkAnchorHandler.GenerateAnchorBoxImageAsync(options);
await image.SaveAsync("anchor_box.png");
```

### Generating SVG Anchor Boxes

```csharp
var svgContent = await InkAnchorHandler.GenerateAnchorBoxSvgAsync(options);
await File.WriteAllTextAsync("anchor_box.svg", svgContent);
```

### Detecting and Extracting Boxes from Images

```csharp
using var inputImage = Image.Load<Rgba32>("scanned_page.png");
var extractedBoxes = InkAnchorHandler.GetAnchorBoxesContentImage(inputImage);

foreach (var (boxId, croppedImage) in extractedBoxes)
{
    await croppedImage.SaveAsync($"extracted_box_{boxId}.png");
}
```

### Evaluating Filled Area Percentage

```csharp
using var croppedImage = Image.Load<Rgba32>("extracted_box.png");
double filledPercentage = InkAnchorHandler.GetFilledAreaPercentage(croppedImage, brightnessThreshold: 225);
Console.WriteLine($"Filled Area: {filledPercentage * 100:0.##}%");
```

---

## Configuration Options

### `InkAnchorGeneratorOptions`

- **BoxId** *(byte)*: Unique identifier for the anchor box.
- **PixelWidth/PixelHeight** *(int)*: Dimensions of the box, minimum 30 pixels.
- **MarkerPixelSize** *(int)*: Pixel size of ArUco markers, default is 60 px.
- **MarkerBorderBits** *(int)*: Border bits around markers, default 1.
- **MarkerPadding** *(int)*: Padding between marker and box border, default 1.
- **FillColor** *(Color?)*: Background color, default transparent.
- **Border** *(InkAnchorBorder?)*: Border styling and settings.
- **BoxLabel** *(InkAnchorLabel?)*: Label text, placement, and styling.
- **OuterArUcoMarkers** *(boolean)*: Determines whether ArUco markers are rendered outside the border area.

### `InkAnchorBorder`

Customize borders using:
- **Color**: Border color.
- **Thickness**: 1-5 pixels.
- **Style**: Solid, Dashed, Dotted.
- **Sides**: Top, Right, Bottom, Left, All.

### `InkAnchorLabel`

Configure labels with:
- **LabelText**: Text for the label (max 100 characters).
- **Color**: Text color.
- **FontSize**: Between 5 and 25 pixels.
- **Font**: Font family.
- **LabelPlacement**: Top or Bottom outside the box.

---

## Contributing

Contributions, feature requests, and bug reports are welcome! Please create an issue or submit a pull request on GitHub.

---

## License

Licensed under the MIT License.

---

## Dependencies

- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)
- [OpenCvSharp](https://github.com/shimat/opencvsharp)

---

## Acknowledgments

Thanks to the authors and maintainers of SixLabors.ImageSharp and OpenCvSharp for enabling powerful imaging and vision processing capabilities in .NET.

