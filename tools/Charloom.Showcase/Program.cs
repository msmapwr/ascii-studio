using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Charloom.Core;
using Charloom.Services;
using ImageConverter = Charloom.Core.ImageConverter;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "assets/showcase");
Directory.CreateDirectory(output);
using var landscape = new Bitmap(720, 420);
using (var g = Graphics.FromImage(landscape))
{
    g.SmoothingMode = SmoothingMode.AntiAlias;
    using var sky = new LinearGradientBrush(new Rectangle(0, 0, 720, 420), Color.FromArgb(26, 36, 99), Color.FromArgb(251, 155, 95), 90);
    g.FillRectangle(sky, 0, 0, 720, 420);
    using var sun = new SolidBrush(Color.FromArgb(255, 214, 134)); g.FillEllipse(sun, 445, 75, 90, 90);
    using var far = new SolidBrush(Color.FromArgb(106, 85, 141));
    g.FillPolygon(far, [new(0, 255), new(130, 145), new(240, 270), new(405, 175), new(620, 290), new(720, 220), new(720, 420), new(0, 420)]);
    using var near = new SolidBrush(Color.FromArgb(28, 48, 75));
    g.FillPolygon(near, [new(0, 300), new(90, 230), new(190, 340), new(350, 250), new(470, 330), new(650, 220), new(720, 290), new(720, 420), new(0, 420)]);
    using var ridge = new Pen(Color.FromArgb(82, 172, 187), 4);
    g.DrawLines(ridge, [new(0, 350), new(170, 365), new(350, 340), new(520, 365), new(720, 330)]);
}
landscape.Save(Path.Combine(output, "landscape-source.png"), ImageFormat.Png);
var source = ImagingService.Pixels(landscape);
var colored = ImageConverter.Convert(source.pixels, source.width, source.height,
    new() { Columns = 100, CellAspect = .5, Style = ImageArtStyle.HalfBlock, Color = true });
File.WriteAllBytes(Path.Combine(output, "landscape-result.png"), ImagingService.Render(colored, 12, 16));
File.WriteAllText(Path.Combine(output, "landscape.ans"), ExportService.Ansi(colored));
Compare(landscape, "landscape-result.png", "IMAGE → ANSI · HALF BLOCK", "landscape-before-after.png");

var letters = ImagingService.RasterizeText("ASCII", "Segoe UI", 110, true);
var mono = ImageConverter.Convert(letters.pixels, letters.width, letters.height, new() { Columns = 100, CellAspect = .5, Characters = " .:+#@" });
File.WriteAllBytes(Path.Combine(output, "text-source.png"), ImagingService.Thumbnail(letters.pixels, letters.width, letters.height, 720));
File.WriteAllBytes(Path.Combine(output, "text-result.png"), ImagingService.Render(mono, 12, 16));
File.WriteAllText(Path.Combine(output, "text.txt"), mono.Text);
using var textSource = Image.FromFile(Path.Combine(output, "text-source.png"));
Compare(textSource, "text-result.png", "TEXT → ASCII · SYSTEM FONT", "text-before-after.png");
Console.WriteLine($"Generated actual converter/export samples: {output}");

void Compare(Image before, string afterPath, string title, string name)
{
    using var after = Image.FromFile(Path.Combine(output, afterPath));
    using var canvas = new Bitmap(1440, 540);
    using var g = Graphics.FromImage(canvas);
    g.Clear(Color.FromArgb(18, 24, 34));
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    using var label = new Font("Segoe UI", 22, FontStyle.Bold);
    using var caption = new Font("Segoe UI", 14);
    using var white = new SolidBrush(Color.FromArgb(231, 237, 247));
    using var muted = new SolidBrush(Color.FromArgb(154, 174, 200));
    g.DrawString(title, label, white, 24, 20);
    g.DrawString("BEFORE", caption, muted, 24, 72); g.DrawString("AFTER", caption, muted, 744, 72);
    Draw(before, new(24, 115, 672, 395)); Draw(after, new(744, 115, 672, 395));
    canvas.Save(Path.Combine(output, name), ImageFormat.Png);
    void Draw(Image image, Rectangle area)
    {
        var scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
        var w = (int)(image.Width * scale); var h = (int)(image.Height * scale);
        g.DrawImage(image, new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }
}
