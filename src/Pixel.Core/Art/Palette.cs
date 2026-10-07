namespace Pixel.Core.Art;

/// <summary>Fur colours for the cat. Colours are opaque 0xAARRGGBB. <see cref="Line"/> draws closed eyes and mouth.</summary>
public sealed record Palette(
    string Name,
    uint Outline,
    uint Fur,
    uint Shade,
    uint Highlight,
    uint Iris,
    uint InnerEar,
    uint Nose,
    uint Line)
{
    public static readonly Palette Black = new("black",
        Outline: 0xFF08070C, Fur: 0xFF2A2738, Shade: 0xFF1A1824, Highlight: 0xFF544E72,
        Iris: 0xFFC6E33C, InnerEar: 0xFF7A4560, Nose: 0xFFE58FA8, Line: 0xFF8A83A3);

    public static readonly Palette Orange = new("orange",
        Outline: 0xFF4A2410, Fur: 0xFFEE9640, Shade: 0xFFC46A26, Highlight: 0xFFFFC27A,
        Iris: 0xFF7CC94A, InnerEar: 0xFFF2A0A0, Nose: 0xFFE8707E, Line: 0xFF4A2410);

    public static readonly Palette Grey = new("grey",
        Outline: 0xFF23242B, Fur: 0xFF8C91A0, Shade: 0xFF6A6E7C, Highlight: 0xFFB4B9C6,
        Iris: 0xFFF2C23C, InnerEar: 0xFFE6A0B0, Nose: 0xFFE58FA8, Line: 0xFF23242B);

    public static readonly Palette White = new("white",
        Outline: 0xFF4E4A52, Fur: 0xFFF3F0EA, Shade: 0xFFD3CDC2, Highlight: 0xFFFFFFFF,
        Iris: 0xFF5AB4F0, InnerEar: 0xFFF4A9B8, Nose: 0xFFF08C9E, Line: 0xFF4E4A52);

    private static readonly Palette[] All = [Black, Orange, Grey, White];

    public static IReadOnlyList<string> Names { get; } = All.Select(p => p.Name).ToArray();

    public static Palette Named(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Black;

    /// <summary>The red-hot variant used while the cat is overheating from fast typing.</summary>
    public Palette Overheated() => this with
    {
        Name = Name + "-hot",
        Outline = 0xFF3A0808,
        Fur = 0xFFD9412E,
        Shade = 0xFFA82A1E,
        Highlight = 0xFFFF7A52,
    };
}
