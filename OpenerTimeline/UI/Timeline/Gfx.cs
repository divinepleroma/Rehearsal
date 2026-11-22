using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;

namespace OpenerTimeline.UI.Timeline;

/// <summary>Small drawing primitives used by the timeline. ImGui only, no game or Dalamud services.</summary>
public static class Gfx
{
    /// <summary>Frame-rate independent exponential smoothing.</summary>
    public static float Approach(float value, float target, float rate, float dt)
    {
        if (dt <= 0f)
            return value;

        var next = value + (target - value) * (1f - MathF.Exp(-rate * dt));
        return MathF.Abs(target - next) < 0.001f ? target : next;
    }

    public static float Saturate(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    /// <summary>Draws text with its vertical centre on <paramref name="centerY"/>. Returns its width.</summary>
    public static float TextLeft(ImDrawListPtr dl, float x, float centerY, uint color, string text)
    {
        var size = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(MathF.Round(x), MathF.Round(centerY - size.Y * 0.5f)), color, text);
        return size.X;
    }

    /// <summary>Draws text ending at <paramref name="right"/>. Returns its width.</summary>
    public static float TextRight(ImDrawListPtr dl, float right, float centerY, uint color, string text)
    {
        var size = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(MathF.Round(right - size.X), MathF.Round(centerY - size.Y * 0.5f)), color, text);
        return size.X;
    }

    /// <summary>Text straight on the game image, with a soft shadow so it holds up on any scenery. Returns its width.</summary>
    public static float ShadowText(ImDrawListPtr dl, float x, float centerY, Vector3 color, float alpha, string text)
    {
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(MathF.Round(x), MathF.Round(centerY - size.Y * 0.5f));
        var shadow = Palette.Pack(Palette.Black, 0.75f * alpha);
        dl.AddText(pos + new Vector2(1f, 1f), shadow, text);
        dl.AddText(pos + new Vector2(0f, 1f), shadow, text);
        dl.AddText(pos, Palette.Pack(color, alpha), text);
        return size.X;
    }

    /// <summary>Like <see cref="ShadowText"/>, ending at <paramref name="right"/>.</summary>
    public static float ShadowTextRight(ImDrawListPtr dl, float right, float centerY, Vector3 color, float alpha, string text)
    {
        var width = ImGui.CalcTextSize(text).X;
        ShadowText(dl, right - width, centerY, color, alpha, text);
        return width;
    }

    public static void TextCentered(ImDrawListPtr dl, Vector2 center, uint color, string text)
    {
        var size = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(MathF.Round(center.X - size.X * 0.5f), MathF.Round(center.Y - size.Y * 0.5f)), color, text);
    }

    /// <summary>A pill with a label. Returns its width.</summary>
    public static float Pill(
        ImDrawListPtr dl, float x, float centerY, float height, float padX, string text, uint background, uint foreground,
        bool alignRight = false)
    {
        var size = ImGui.CalcTextSize(text);
        var width = MathF.Round(size.X + padX * 2f);
        var left = alignRight ? x - width : x;
        var min = new Vector2(MathF.Round(left), MathF.Round(centerY - height * 0.5f));
        var max = min + new Vector2(width, height);
        dl.AddRectFilled(min, max, background, height * 0.5f);
        dl.AddText(new Vector2(min.X + padX, MathF.Round(centerY - size.Y * 0.5f)), foreground, text);
        return width;
    }

    /// <summary>Soft coloured halo around a rounded rectangle.</summary>
    public static void Glow(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector3 color, float rounding, float intensity, float scale)
    {
        if (intensity <= 0.01f)
            return;

        for (var ring = 3; ring >= 1; ring--)
        {
            var grow = ring * 2.2f * scale;
            var ringAlpha = 0.13f * (4 - ring) * intensity;
            dl.AddRect(min - new Vector2(grow), max + new Vector2(grow), Palette.Pack(color, ringAlpha), rounding + grow, 2.4f * scale);
        }
    }

    public static void Cross(ImDrawListPtr dl, Vector2 center, float radius, uint color, float thickness)
    {
        dl.AddLine(center - new Vector2(radius), center + new Vector2(radius), color, thickness);
        dl.AddLine(center + new Vector2(-radius, radius), center + new Vector2(radius, -radius), color, thickness);
    }

    public static void Check(ImDrawListPtr dl, Vector2 center, float radius, uint color, float thickness)
    {
        var a = center + new Vector2(-radius, 0f);
        var b = center + new Vector2(-radius * 0.25f, radius * 0.7f);
        var c = center + new Vector2(radius, -radius * 0.7f);
        dl.AddLine(a, b, color, thickness);
        dl.AddLine(b, c, color, thickness);
    }

    /// <summary>
    /// The key cap telling the player what to press. Centred horizontally on <paramref name="centerX"/>.
    /// </summary>
    public static void KeyChip(
        ImDrawListPtr dl, float centerX, float top, float height, float maxWidth, in KeyHint hint, in TimelineStyle style,
        bool emphasised, float alpha)
    {
        if (hint.Kind == HintKind.None || height <= 0f || alpha <= 0.01f)
            return;

        var s = style.Scale;
        var padX = MathF.Round(5f * s);
        var innerGap = MathF.Round(3f * s);
        var rounding = MathF.Round(4f * s);
        var centerY = top + height * 0.5f;

        var mods = string.Empty;
        var key = string.Empty;
        var glyphWidth = 0f;

        switch (hint.Kind)
        {
            case HintKind.Key:
                key = hint.Key ?? "?";

                // Automatic: spelled out in the full view, the game's own letters in the compact one.
                var letters = style.HintStyle == KeybindStyle.Compact || (style.HintStyle == KeybindStyle.Auto && style.Compact);
                mods = letters ? CompactModifiers(hint) : FullModifiers(hint);
                if (!letters && style.HintStyle == KeybindStyle.Auto && mods.Length > 0)
                {
                    // ...unless a long combination would run into the neighbouring key cap.
                    var fullWidth = padX * 2f + ImGui.CalcTextSize(mods).X + innerGap + ImGui.CalcTextSize(key).X;
                    if (fullWidth > maxWidth)
                        mods = CompactModifiers(hint);
                }

                break;
            case HintKind.Slot:
                key = $"{hint.Bar}-{hint.Slot}";
                break;
            case HintKind.Pad:
                key = (hint.PadRightTrigger, style.Pad) switch
                {
                    (false, PadGlyphs.Xbox) => "LT",
                    (true, PadGlyphs.Xbox) => "RT",
                    (false, _) => "L2",
                    (true, _) => "R2"
                };
                glyphWidth = MathF.Round(13f * s);
                break;
            case HintKind.Missing:
            case HintKind.Varies:
                glyphWidth = MathF.Round(12f * s);
                break;
        }

        var modsWidth = mods.Length > 0 ? ImGui.CalcTextSize(mods).X : 0f;
        var keySize = key.Length > 0 ? ImGui.CalcTextSize(key) : Vector2.Zero;
        var contentWidth = modsWidth + keySize.X + glyphWidth;
        if (modsWidth > 0f && keySize.X > 0f)
            contentWidth += innerGap;
        if (glyphWidth > 0f && keySize.X > 0f)
            contentWidth += innerGap;

        var width = MathF.Max(height, MathF.Round(contentWidth + padX * 2f));
        var min = new Vector2(MathF.Round(centerX - width * 0.5f), MathF.Round(top));
        var max = min + new Vector2(width, height);

        var accent = style.Accent;
        uint keyColor, modColor;
        if (emphasised && hint.Kind != HintKind.Missing)
        {
            // The step to press right now: a filled cap, so the eye lands on it.
            dl.AddRectFilled(min, max, Palette.Pack(accent, 0.96f * alpha), rounding);
            keyColor = Palette.Pack(Palette.OnAccent, alpha);
            modColor = Palette.Pack(Palette.OnAccent, 0.72f * alpha);
        }
        else if (hint.Kind == HintKind.Missing)
        {
            dl.AddRectFilled(min, max, Palette.Pack(Palette.Warn, 0.10f * alpha), rounding);
            dl.AddRect(min, max, Palette.Pack(Palette.Warn, 0.55f * alpha), rounding, 1f);
            keyColor = modColor = Palette.Pack(Palette.Warn, 0.9f * alpha);
        }
        else
        {
            dl.AddRectFilled(min, max, Palette.Pack(Palette.White, 0.09f * alpha), rounding);
            dl.AddRect(min, max, Palette.Pack(Palette.White, 0.16f * alpha), rounding, 1f);
            var unbound = hint.Kind == HintKind.Slot;
            keyColor = Palette.Pack(Palette.White, (unbound ? Palette.TextDimAlpha : Palette.TextAlpha) * alpha);
            modColor = Palette.Pack(Palette.Mix(accent, Palette.White, 0.25f), 0.95f * alpha);
        }

        var x = MathF.Round(centerX - contentWidth * 0.5f);
        if (modsWidth > 0f)
        {
            dl.AddText(new Vector2(x, MathF.Round(centerY - keySize.Y * 0.5f)), modColor, mods);
            x += modsWidth + innerGap;
        }

        if (keySize.X > 0f)
        {
            dl.AddText(new Vector2(x, MathF.Round(centerY - keySize.Y * 0.5f)), keyColor, key);
            x += keySize.X + innerGap;
        }

        switch (hint.Kind)
        {
            case HintKind.Pad:
                PadGlyph(dl, new Vector2(x + glyphWidth * 0.5f, centerY), s, hint, keyColor, modColor);
                break;
            case HintKind.Missing:
                dl.AddLine(new Vector2(x + 2f * s, centerY), new Vector2(x + glyphWidth - 2f * s, centerY), keyColor, 1.6f * s);
                break;
            case HintKind.Varies:
                for (var i = 0; i < 3; i++)
                    dl.AddCircleFilled(new Vector2(x + (2f + i * 4f) * s, centerY), 1.3f * s, keyColor, 8);
                break;
        }
    }

    /// <summary>Cooldown length in the icon's top right corner, colour coded by burst window.</summary>
    public static void CooldownBadge(
        ImDrawListPtr dl, Vector2 iconMin, Vector2 iconMax, in TimelineStep step, in TimelineStyle style, float alpha)
    {
        if (style.Badges == CooldownBadgeMode.Off || step.Cooldown == CooldownClass.None)
            return;
        if (style.Badges == CooldownBadgeMode.Burst && step.Cooldown == CooldownClass.Long)
            return;

        var s = style.Scale;
        var color = CooldownColor(step.Cooldown);
        var text = ((int)MathF.Round(step.Recast)).ToString();
        var textSize = ImGui.CalcTextSize(text);
        var height = MathF.Round(14f * s);
        var width = MathF.Max(MathF.Round(height * 1.35f), MathF.Round(textSize.X + 7f * s));
        var overhang = MathF.Round(4f * s);

        var max = new Vector2(MathF.Round(iconMax.X + overhang), MathF.Round(iconMin.Y - overhang + height));
        var min = max - new Vector2(width, height);
        var outline = new Vector2(MathF.Max(1f, 1.5f * s));

        dl.AddRectFilled(min - outline, max + outline, Palette.Pack(Palette.Surface, 0.92f * alpha), height * 0.5f + outline.X);
        dl.AddRectFilled(min, max, Palette.Pack(color, alpha), height * 0.5f);
        TextCentered(dl, (min + max) * 0.5f, Palette.Pack(Palette.OnAccent, alpha), text);
    }

    public static Vector3 CooldownColor(CooldownClass cooldown)
    {
        return cooldown switch
        {
            CooldownClass.OneMinute => Palette.OneMinute,
            CooldownClass.TwoMinute => Palette.TwoMinute,
            _ => Palette.LongCooldown
        };
    }

    private static string FullModifiers(in KeyHint hint)
    {
        return (hint.Ctrl, hint.Shift, hint.Alt) switch
        {
            (true, false, false) => "ctrl",
            (false, true, false) => "shift",
            (false, false, true) => "alt",
            (true, true, false) => "ctrl+shift",
            (true, false, true) => "ctrl+alt",
            (false, true, true) => "shift+alt",
            (true, true, true) => "ctrl+shift+alt",
            _ => string.Empty
        };
    }

    /// <summary>The single-letter convention the game's own hotbars use.</summary>
    private static string CompactModifiers(in KeyHint hint)
    {
        return (hint.Ctrl, hint.Shift, hint.Alt) switch
        {
            (true, false, false) => "c",
            (false, true, false) => "s",
            (false, false, true) => "a",
            (true, true, false) => "cs",
            (true, false, true) => "ca",
            (false, true, true) => "sa",
            (true, true, true) => "csa",
            _ => string.Empty
        };
    }

    /// <summary>Four positions in a diamond with the one to press lit. Squares for the d-pad, dots for face buttons.</summary>
    private static void PadGlyph(ImDrawListPtr dl, Vector2 center, float scale, in KeyHint hint, uint active, uint idle)
    {
        var reach = 4.2f * scale;
        var dot = 1.9f * scale;
        Span<Vector2> offsets =
        [
            new Vector2(-reach, 0f),
            new Vector2(0f, -reach),
            new Vector2(reach, 0f),
            new Vector2(0f, reach)
        ];

        var idleColor = (idle & 0x00FFFFFF) | ((uint)(((idle >> 24) & 0xFF) * 0.4f) << 24);
        for (var i = 0; i < 4; i++)
        {
            var color = i == hint.PadDirection ? active : idleColor;
            var p = center + offsets[i];
            if (hint.PadFace)
                dl.AddCircleFilled(p, dot, color, 10);
            else
                dl.AddRectFilled(p - new Vector2(dot * 0.85f), p + new Vector2(dot * 0.85f), color);
        }
    }
}
