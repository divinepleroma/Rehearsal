using System;
using System.Numerics;

namespace Rehearsal.UI;

/// <summary>
/// Colours shared by every surface of the plugin. Tuned to sit next to Material UI (dark):
/// near-black neutral surfaces, white text at material opacities and a single accent.
/// </summary>
public static class Palette
{
    public static readonly Vector3 White = new(1f, 1f, 1f);
    public static readonly Vector3 Black = new(0f, 0f, 0f);

    /// <summary>Panel base. Slightly cool so it does not turn muddy over warm scenery.</summary>
    public static readonly Vector3 Surface = Rgb(0x12, 0x13, 0x16);

    /// <summary>Text drawn on top of the accent colour.</summary>
    public static readonly Vector3 OnAccent = Rgb(0x08, 0x12, 0x1C);

    /// <summary>Material UI - Reborn's default accent.</summary>
    public static readonly Vector3 DefaultAccent = new(0.35f, 0.725f, 1.0f);

    public static readonly Vector3 Good = Rgb(0x7B, 0xD8, 0x8F);
    public static readonly Vector3 Bad = Rgb(0xFF, 0x6B, 0x6B);

    /// <summary>A wrong press, said quietly: a muted coral rather than an alarm red.</summary>
    public static readonly Vector3 Slip = Rgb(0xF0, 0x9A, 0x7C);

    /// <summary>Material UI - Reborn's proc highlight amber.</summary>
    public static readonly Vector3 Warn = new(1.0f, 0.675f, 0.22f);

    /// <summary>One minute cooldowns.</summary>
    public static readonly Vector3 OneMinute = Rgb(0x4F, 0xD6, 0xC4);

    /// <summary>Two minute cooldowns.</summary>
    public static readonly Vector3 TwoMinute = Rgb(0xFF, 0xC8, 0x57);

    /// <summary>Other long cooldowns.</summary>
    public static readonly Vector3 LongCooldown = Rgb(0xB9, 0xBE, 0xC9);

    public const float TextAlpha = 0.92f;
    public const float TextDimAlpha = 0.62f;
    public const float TextFaintAlpha = 0.40f;

    public static Vector3 Rgb(byte r, byte g, byte b)
    {
        return new Vector3(r / 255f, g / 255f, b / 255f);
    }

    /// <summary>Packs a colour the way ImGui expects it (ABGR).</summary>
    public static uint Pack(Vector3 rgb, float alpha = 1f)
    {
        var r = (uint)(Math.Clamp(rgb.X, 0f, 1f) * 255f + 0.5f);
        var g = (uint)(Math.Clamp(rgb.Y, 0f, 1f) * 255f + 0.5f);
        var b = (uint)(Math.Clamp(rgb.Z, 0f, 1f) * 255f + 0.5f);
        var a = (uint)(Math.Clamp(alpha, 0f, 1f) * 255f + 0.5f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    public static Vector4 V4(Vector3 rgb, float alpha = 1f)
    {
        return new Vector4(rgb, alpha);
    }

    public static Vector3 Mix(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, Math.Clamp(t, 0f, 1f));
    }
}
