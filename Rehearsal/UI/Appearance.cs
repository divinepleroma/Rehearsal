using System;
using System.Globalization;
using System.Numerics;

namespace Rehearsal.UI;

/// <summary>A ready-made combination of panel opacity and blur.</summary>
public readonly record struct AppearancePreset(string Name, string Description, float PanelOpacity, float Blur, float WindowOpacity)
{
    public bool Matches(Settings settings)
    {
        return MathF.Abs(settings.PanelOpacity - PanelOpacity) < 0.006f && MathF.Abs(settings.Blur - Blur) < 0.006f
                                                                         && MathF.Abs(settings.WindowOpacity - WindowOpacity) < 0.006f;
    }

    public void Apply(Settings settings)
    {
        settings.PanelOpacity = PanelOpacity;
        settings.Blur = Blur;
        settings.WindowOpacity = WindowOpacity;
    }
}

public readonly record struct AccentColor(string Name, Vector3 Color);

/// <summary>The looks offered in the settings, and helpers for the colour field.</summary>
public static class Appearance
{
    public static readonly AppearancePreset[] Presets =
    [
        new("Acrylic", "Frosted dark glass. The default.", 0.58f, 0.45f, 0.78f),
        new("Glass", "Lighter and more see-through, strongly blurred.", 0.30f, 0.80f, 0.62f),
        new("Smoke", "Darker, for bright zones and busy fights.", 0.76f, 0.30f, 0.88f),
        new("Solid", "Opaque, no blur. Lightest on the GPU.", 0.94f, 0f, 0.97f),
        new("Ghost", "Hardly any panel at all, just the icons.", 0.12f, 0f, 0.70f)
    ];

    public static readonly AccentColor[] Accents =
    [
        new("Material blue", Palette.DefaultAccent),
        new("Teal", Palette.Rgb(0x4F, 0xD6, 0xC4)),
        new("Mint", Palette.Rgb(0x7B, 0xD8, 0x8F)),
        new("Lime", Palette.Rgb(0xC3, 0xE8, 0x6B)),
        new("Gold", Palette.Rgb(0xFF, 0xC8, 0x57)),
        new("Amber", Palette.Rgb(0xFF, 0xAC, 0x38)),
        new("Orange", Palette.Rgb(0xFF, 0x8A, 0x50)),
        new("Rose", Palette.Rgb(0xFF, 0x7A, 0x90)),
        new("Pink", Palette.Rgb(0xFF, 0x8C, 0xC6)),
        new("Violet", Palette.Rgb(0xB7, 0x94, 0xF6)),
        new("Indigo", Palette.Rgb(0x8C, 0x9E, 0xFF)),
        new("Silver", Palette.Rgb(0xD5, 0xDB, 0xE5))
    ];

    public static string ToHex(Vector3 color)
    {
        static int Channel(float value)
        {
            return (int)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        }

        return $"#{Channel(color.X):X2}{Channel(color.Y):X2}{Channel(color.Z):X2}";
    }

    /// <summary>Accepts "#RRGGBB", "RRGGBB" and the short "#RGB".</summary>
    public static bool TryParseHex(string text, out Vector3 color)
    {
        color = default;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        color = new Vector3(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }
}
