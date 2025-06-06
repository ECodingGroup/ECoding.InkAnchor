﻿using ECoding.InkAnchor;
using iText.Svg.Converter;
using iTextSharp.text.pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using SkiaSharp.Extended.Svg;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Image = SixLabors.ImageSharp.Image;

/*Generate PDF With raster image*/

//Console.WriteLine("Generating PDF with signature box...");

//QuestPDF.Settings.License = LicenseType.Community;

//var options = new InkAnchorGeneratorOptions(boxId: 2, pixelWidth: 200, pixelHeight: 100)
//{
//    FillColor = null, // transparent
//    Border = new InkAnchorBorder(SixLabors.ImageSharp.Color.Black, 1, InkAnchorBorder.BorderStyle.Solid, InkAnchorBorder.BorderSides.All),
//    BoxLabel = new InkAnchorLabel("Podpis poistníka", BoxLabelPlacement.BottomOutsideBox, fontSize: 14),
//    MarkerPixelSize = 20,
//    MarkerBorderBits = 1
//};

//// Generate the signature box image
//var boxImage = await InkAnchorHandler.GenerateAnchorBoxImageAsync(options);

//// Convert ImageSharp image to byte array (PNG)
//using var ms = new MemoryStream();
//await boxImage.SaveAsPngAsync(ms);
//var imageBytes = ms.ToArray();

//// Define PDF document
//var pdf = Document.Create(container =>
//{
//    container.Page(page =>
//    {
//        page.Size(PageSizes.A4);
//        page.Margin(20);
//        page.DefaultTextStyle(x => x.FontSize(20));

//        page.Content().PaddingVertical(20).Column(column =>
//        {
//            column.Item().Text("This is a test document.");
//            column.Item().Element(e => e.Extend()); // flexible space
//        });

//        page.Footer().Row(row =>
//        {
//            row.RelativeItem(); // Spacer on the left side
//            row.ConstantItem(boxImage.Width)
//               .PaddingRight(20)
//               .PaddingBottom(20)
//               .Image(imageBytes);
//        });
//    });
//});
//// Save PDF to file
//pdf.GeneratePdf(@"InkAnchor.pdf");


/*Generate SVG PDF example*/
//Console.WriteLine("Generating PDF with signature box...");

//QuestPDF.Settings.License = LicenseType.Community;

//var options = new InkAnchorGeneratorOptions(
//                  boxId: 2, pixelWidth: 200, pixelHeight: 100)
//{
//    FillColor = null,
//    Border = new InkAnchorBorder(
//                   SixLabors.ImageSharp.Color.Black, 1,
//                   InkAnchorBorder.BorderStyle.Solid,
//                   InkAnchorBorder.BorderSides.All),
//    BoxLabel = new InkAnchorLabel(
//                          "Podpis poistníka",
//                          BoxLabelPlacement.BottomOutsideBox,
//                          fontSize: 14),
//    MarkerPixelSize = 20,
//    MarkerBorderBits = 1
//};

//// 1) ─── generate SVG instead of PNG ──────────────────────────────
//string svgMarkup = await InkAnchorHandler.GenerateAnchorBoxSvgAsync(options);


//// 2) ─── build PDF ────────────────────────────────────────────────
//var pdf = Document.Create(container =>
//{
//    container.Page(page =>
//    {
//        page.Size(PageSizes.A4);
//        page.Margin(20);
//        page.DefaultTextStyle(x => x.FontSize(20));

//        page.Content().PaddingVertical(20).Column(col =>
//        {
//            col.Item().Text("This is a test document.");
//            col.Item().Element(e => e.Extend());      // flexible spacer
//        });

//        page.Footer().Row(row =>
//        {
//            row.RelativeItem();                       // left spacer
//            row.ConstantItem(options.PixelWidth)      // same width as box
//               .PaddingRight(20)
//               .PaddingBottom(20)
//               // 3) ── display the SVG ────────────────────────────
//               .Svg(svgMarkup);        // <- extension from QuestPDF.Svg
//        });
//    });
//});

//// 4) ─── save ─────────────────────────────────────────────────────
//pdf.GeneratePdf("InkAnchor.pdf");

/*
async Task<byte[]> PrintSignatureFieldAsync(byte[] pdf, bool manualSignatureField)
{
    using var inputMs = new MemoryStream(pdf);
    using var outputMs = new MemoryStream();
    using var reader = new iText.Kernel.Pdf.PdfReader(inputMs);
    using var writer = new iText.Kernel.Pdf.PdfWriter(outputMs);
    //using var stamper = new PdfStamper(reader, outputMs);

    //int page = reader.NumberOfPages;

    float x = 410;
    float y = 70;
    float width = 200;
    float height = 100;

    var options = new InkAnchorGeneratorOptions(boxId: 1, pixelWidth: (int)width, pixelHeight: (int)height)
    {
        FillColor = SixLabors.ImageSharp.Color.White,
        Border = new InkAnchorBorder(SixLabors.ImageSharp.Color.Black, thickness: 1, borderStyle: InkAnchorBorder.BorderStyle.Solid),
        BoxLabel = new InkAnchorLabel("Podpis poistníka", BoxLabelPlacement.BottomOutsideBox, fontSize: 14, font: "Helvetica", color: SixLabors.ImageSharp.Color.Black),
        MarkerPixelSize = 17,
        MarkerBorderBits = 1,
    };

    //using var boxImage = await InkAnchorHandler.GenerateAnchorBoxImageAsync(options);

    //using var imageStream = new MemoryStream();
    //await boxImage.SaveAsPngAsync(imageStream);
    //imageStream.Position = 0;

    //var image = iTextSharp.text.Image.GetInstance(imageStream);
    //image.ScaleAbsolute(width, height);
    //image.SetAbsolutePosition(x, y);
    //var content = stamper.GetOverContent(page);
    //content.SaveState();
    //content.AddImage(image, inlineImage: true);
    //content.RestoreState();
    //stamper.Close();

    //var pageDoc = reader.GetPageN(1);
    var document = new iText.Kernel.Pdf.PdfDocument(reader, writer);
    var lastPage = document.GetLastPage();
    //PdfPage page = pdfDoc.GetPage(1);

    var boxSVG = await InkAnchorHandler.GenerateAnchorBoxSvgAsync(options);
    SvgConverter.DrawOnPage(boxSVG, lastPage, x, y);


    document.Close();



    return outputMs.ToArray();
}

var pdf = File.ReadAllBytes(@"C:\temp\702edee0-747a-4b2a-9ee5-98b3faee24bc\dokPdf_ba0718e8de.pdf");

pdf = await PrintSignatureFieldAsync(pdf, true);
File.WriteAllBytes(@"InkAnchor.pdf", pdf);

Console.WriteLine("PDF generated: output.pdf");
*/


/*Generate PDF with SVG*/
/*
Console.WriteLine("Generating PDF with signature box (SVG)...");

QuestPDF.Settings.License = LicenseType.Community;

// 1) Set up options for the signature box
var options = new InkAnchorGeneratorOptions(boxId: 1, pixelWidth: 200, pixelHeight: 100)
{
    FillColor = null, // transparent
    Border = new InkAnchorBorder(SixLabors.ImageSharp.Color.Black, 1),
    BoxLabel = new InkAnchorLabel("Please sign here", BoxLabelPlacement.BottomOutsideBox, fontSize: 12),

    // Additional marker properties
    MarkerPadding = 2,
    MarkerPixelSize = 20,
    MarkerBorderBits = 1
};

// 2) Generate the signature box as an SVG
string svgContent = await InkAnchorHandler.GenerateIngAnchorBoxSvgAsync(options);

// 3) Rasterize the SVG to a PNG (byte[]) using SkiaSharp
//    - We'll create an SKBitmap in the same pixel dimensions 
//      as the <svg width=... height=...> from your code.
//    - In your code, that was 200 wide x 100 high total (the "box" area).
//      But note the markers can be drawn inside that space. 
//    - So let's parse the "width" and "height" from the SVG or
//      just match "options.PixelWidth" and "options.PixelHeight" plus any label space.

// For simplicity, let's just do a big enough canvas 
// to match the final <svg width="..." height="..."> 
// (that is "width=200" / "height=100" plus label offsets).
// But the actual final <svg> from your code is "width=200" / "height=someTotalHeight"
// We'll just guess for demonstration, or you can parse from the string if needed.

// Let's find the "width" / "height" from your final SVG.
// The easiest approach is: we know that final 'totalHeight' = 100 + label stuff,
// i.e. "labelExtraBottom" might be 17 if the label is outside the box.
// We'll do a small helper if we want to be exact:
int finalWidth = options.PixelWidth;
int finalHeight = options.PixelHeight
    + (options.BoxLabel?.LabelPlacement == BoxLabelPlacement.TopOutsideBox ? (options.BoxLabel.FontSize + 5) : 0)
    + (options.BoxLabel?.LabelPlacement == BoxLabelPlacement.BottomOutsideBox ? (options.BoxLabel.FontSize + 5) : 0);

// Load the SVG data into Skia
var svg = new SkiaSharp.Extended.Svg.SKSvg();
using var stringReader = new StringReader(svgContent);
using var xmlReader = XmlReader.Create(stringReader);
svg.Load(xmlReader);
// Create a bitmap
using var bitmap = new SKBitmap(finalWidth, finalHeight);
using var canvas = new SKCanvas(bitmap);

// Clear background (white or transparent)
canvas.Clear(SKColors.Transparent);

// The SkiaSharp.Extended.Svg library automatically 
// normalizes the SVG's viewBox to the canvas. 
// We do need to scale or translate if the sizes differ.
// If the <svg> "width" and "height" match finalWidth/finalHeight, 
// then simply:
canvas.DrawPicture(svg.Picture);
canvas.Flush();

// 4) Convert that SKBitmap => PNG bytes
using var image = SKImage.FromPixels(bitmap.PeekPixels());
using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
var svgAsPngBytes = pngData.ToArray();

// 5) Now we can embed svgAsPngBytes in a QuestPDF doc
var document = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(20);
        page.DefaultTextStyle(x => x.FontSize(20));

        page.Content().PaddingVertical(20).Column(column =>
        {
            column.Item().Text("This is a test document with an SVG-based signature box.");
            column.Item().Element(e => e.Extend()); // flexible space
        });

        // We'll place the image in the footer
        page.Footer().Row(row =>
        {
            row.RelativeItem(); // left side spacer
            row.ConstantItem(200) // width (or finalWidth) if you want exact dimension
               .PaddingRight(20)
               .PaddingBottom(20)
               .Image(svgAsPngBytes);
        });
    });
});

// 6) Save PDF to file
var outputPath = "InkAnchor-SVG.pdf";
document.GeneratePdf(outputPath);
Console.WriteLine($"PDF generated: {outputPath}");
*/





/*

string inputImagePath = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\Scan_0001.jpg";
string outputFolder = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\";

if (!File.Exists(inputImagePath))
{
    Console.WriteLine($"Error: Cannot find input image at '{inputImagePath}'");
    return;
}

if (!Directory.Exists(outputFolder))
{
    Console.WriteLine($"Warning: Output folder '{outputFolder}' does not exist. Creating it...");
    Directory.CreateDirectory(outputFolder);
}

try
{
    // 2) Load the input image (ImageSharp)
    using Image<Rgba32> inputImage = Image.Load<Rgba32>(inputImagePath);

    // 3) Extract all anchor boxes => returns List<(int BoxId, Image<Rgba32> Cropped)>
    var anchorBoxes = InkAnchorHandler.GetAnchorBoxesContentImage(inputImage);

    // 4) Save each extracted box image
    if (anchorBoxes.Count == 0)
    {
        Console.WriteLine("No anchor boxes found in the image.");
    }
    else
    {
        Console.WriteLine($"Found {anchorBoxes.Count} anchor box(es).");

        foreach (var (boxId, croppedImg) in anchorBoxes)
        {
            // e.g. "anchor_box_0.png", "anchor_box_1.png", etc.
            string outFileName = $"anchor_box_{boxId}.png";
            string outPath = Path.Combine(outputFolder, outFileName);

            croppedImg.SaveAsPng(outPath);
            Console.WriteLine($"  -> Saved box {boxId} to '{outPath}'");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Error while processing the image:");
    Console.WriteLine(ex.Message);
}

*/


/*
string inputImagePath = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\anchor_box_3.png";

using Image<Rgba32> inputImage = Image.Load<Rgba32>(inputImagePath);
Console.WriteLine(InkAnchorHandler.GetFilledAreaPercentage(inputImage));
*/





/*
string inputImagePath = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\anchor_box_1.png";
string outputFolder = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\";

if (!File.Exists(inputImagePath))
{
    Console.WriteLine($"Error: Cannot find input image at '{inputImagePath}'");
    return;
}

if (!Directory.Exists(outputFolder))
{
    Console.WriteLine($"Warning: Output folder '{outputFolder}' does not exist. Creating it...");
    Directory.CreateDirectory(outputFolder);
}

try
{
    // 2) Load the input image (ImageSharp)
    using Image<Rgba32> inputImage = Image.Load<Rgba32>(inputImagePath);

    // 3) Extract all anchor boxes => returns List<(int BoxId, Image<Rgba32> Cropped)>
    var binarizedCroppedImage = InkAnchorHandler.TrimAndBinarise(
                   inputImage,
                   whiteLum: 240,   // looser – keeps faint ink
                   whiteVar: 10,
                   minBlob: 50,
                   keepTopK: 1,
                   smoothR: 0,     // no closing/opening
                   preBlurSigma: 1);  // still knocks out specks;

    string outFileName = $"binarized_anchor_box.png";
    string outPath = Path.Combine(outputFolder, outFileName);
    binarizedCroppedImage.SaveAsPng(outPath);
    Console.WriteLine($"  -> Saved binarized box to '{outPath}'");
}
catch (Exception ex)
{
    Console.WriteLine("Error while processing the image:");
    Console.WriteLine(ex.Message);
}
*/

/*Test extraction and trimming*/


string inputImagePath = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\InkAnchor_page-0001 (1).jpg";
string outputFolder = @"C:\Dev\ECoding\ECoding.InkAnchor\ECoding.InkAnchor.TesterApp\";

if (!File.Exists(inputImagePath))
{
    Console.WriteLine($"Error: Cannot find input image at '{inputImagePath}'");
    return;
}

if (!Directory.Exists(outputFolder))
{
    Console.WriteLine($"Warning: Output folder '{outputFolder}' does not exist. Creating it...");
    Directory.CreateDirectory(outputFolder);
}

try
{
    // 2) Load the input image (ImageSharp)
    using Image<Rgba32> inputImage = Image.Load<Rgba32>(inputImagePath);

    // 3) Extract all anchor boxes => returns List<(int BoxId, Image<Rgba32> Cropped)>
    var anchorBoxes = InkAnchorHandler.GetAnchorBoxesContentImage(inputImage);

    // 4) Save each extracted box image
    if (anchorBoxes.Count == 0)
    {
        Console.WriteLine("No anchor boxes found in the image.");
    }
    else
    {
        Console.WriteLine($"Found {anchorBoxes.Count} anchor box(es).");

        foreach (var (boxId, croppedImg) in anchorBoxes)
        {
            string outFileName = $"anchor_box_{boxId}.png";
            string outPath = Path.Combine(outputFolder, outFileName);

            var binarizedCroppedImage = InkAnchorHandler.TrimBasedOnBinarisedImage(croppedImg, padding: 10);

            binarizedCroppedImage.SaveAsPng(outPath);
            Console.WriteLine($"  -> Saved binarized box to '{outPath}'");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Error while processing the image:");
    Console.WriteLine(ex.Message);
}