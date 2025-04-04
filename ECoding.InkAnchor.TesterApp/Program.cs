using ECoding.InkAnchor;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;

Console.WriteLine("Generating PDF with signature box...");

QuestPDF.Settings.License = LicenseType.Community;

var options = new InkAnchorGeneratorOptions(boxId: 1, pixelWidth: 200, pixelHeight: 100)
{
    FillColor = null, // transparent
    Border = new InkAnchorBorder(SixLabors.ImageSharp.Color.Black, 1),
    BoxLabel = new InkAnchorLabel("Please sign here", BoxLabelPlacement.BottomOutsideBox, fontSize: 12),
    MarkerPadding = 2,
    MarkerPixelSize = 20,
    MarkerBorderBits = 1
};

// Generate the signature box image
var boxImage = await InkAnchorHandler.GenerateIngAnchorBoxImageAsync(options);

// Convert ImageSharp image to byte array (PNG)
using var ms = new MemoryStream();
await boxImage.SaveAsPngAsync(ms);
var imageBytes = ms.ToArray();

// Define PDF document
var pdf = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(20);
        page.DefaultTextStyle(x => x.FontSize(20));

        page.Content().PaddingVertical(20).Column(column =>
        {
            column.Item().Text("This is a test document.");
            column.Item().Element(e => e.Extend()); // flexible space
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem(); // Spacer on the left side
            row.ConstantItem(boxImage.Width)
               .PaddingRight(20)
               .PaddingBottom(20)
               .Image(imageBytes);
        });
    });
});

// Save PDF to file
pdf.GeneratePdf(@"InkAnchor.pdf");
Console.WriteLine("PDF generated: output.pdf");
