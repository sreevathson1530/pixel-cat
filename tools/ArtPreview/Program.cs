// Renders contact sheets of every animation so the art can be reviewed by eye.
// Usage: dotnet run --project tools/ArtPreview -- <outDir>
using Pixel.Core.Art;

var outDir = args.Length > 0 ? args[0] : "artifacts";
Directory.CreateDirectory(outDir);
const int S = 3, Pad = 6, Cols = 7;
int cw = CatRenderer.Width * S + Pad, ch = CatRenderer.Height * S + Pad;

var ids = Enum.GetValues<AnimationId>();
for (var sheet = 0; sheet * 6 < ids.Length; sheet++)
{
    var rows = ids.Skip(sheet * 6).Take(6).ToArray();
    var img = new Img(Cols * cw, rows.Length * ch, 0xFFC9D3E0);
    for (var r = 0; r < rows.Length; r++)
    {
        var frames = Animations.Get(rows[r]).Frames;
        for (var i = 0; i < Math.Min(Cols, frames.Count); i++)
            img.Draw(CatRenderer.Render(frames[i].Pose, Palette.Black), i * cw, r * ch, S);
    }
    img.Save(Path.Combine(outDir, $"sheet-{sheet}.png"));
}

// Palettes, mirroring, overheat and pupils on light and dark backgrounds.
var sit = new CatPose(BodyShape.Sit);
var variants = new List<uint[]>();
foreach (var name in Palette.Names) variants.Add(CatRenderer.Render(sit, Palette.Named(name)));
variants.Add(CatRenderer.Render(new CatPose(BodyShape.Stand), Palette.Black, new RenderOptions(FacingLeft: true)));
variants.Add(CatRenderer.Render(sit, Palette.Black, new RenderOptions(Overheated: true)));
variants.Add(CatRenderer.Render(sit, Palette.Black, new RenderOptions(PupilDx: -1, PupilDy: -1)));
var pal = new Img(variants.Count * cw, 2 * ch, 0xFFC9D3E0);
pal.Fill(0, ch, variants.Count * cw, ch, 0xFF1E2230);
for (var i = 0; i < variants.Count; i++)
{
    pal.Draw(variants[i], i * cw, 0, S);
    pal.Draw(variants[i], i * cw, ch, S);
}
pal.Save(Path.Combine(outDir, "palettes.png"));
Console.WriteLine($"wrote sheets to {Path.GetFullPath(outDir)}");

sealed class Img(int w, int h, uint bg)
{
    private readonly uint[] _px = Enumerable.Repeat(bg, w * h).ToArray();

    public void Fill(int x, int y, int fw, int fh, uint c)
    {
        for (var yy = y; yy < y + fh; yy++)
        for (var xx = x; xx < x + fw; xx++)
            _px[yy * w + xx] = c;
    }

    public void Draw(uint[] src, int ox, int oy, int s)
    {
        for (var y = 0; y < CatRenderer.Height * s; y++)
        for (var x = 0; x < CatRenderer.Width * s; x++)
        {
            var p = src[y / s * CatRenderer.Width + x / s];
            if (p >> 24 != 0) _px[(oy + y) * w + ox + x] = p;
        }
    }

    public void Save(string path)
    {
        using var f = File.Create(path);
        PngWriter.Write(f, w, h, _px);
    }
}
