using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace OpenerTimeline.UI.Timeline;

/// <summary>Draws the icon of an opener step: a single action, or a collage for a group of actions.</summary>
public static class IconPainter
{
    /// <summary>Corner radius as a share of the icon size; matches the game's rounded action frames.</summary>
    public const float RoundingRatio = 0.17f;

    /// <summary>Game icons carry a dark hairline around the art; trimming it keeps small icons crisp.</summary>
    private const float UvInset = 0.035f;

    public static void Draw(
        ImDrawListPtr dl, ITimelineHost host, in TimelineStep step, Vector2 min, Vector2 max, float alpha, float rounding)
    {
        if (step.IsWildcard)
        {
            DrawWildcard(dl, min, max, alpha, rounding);
            return;
        }

        var tint = Palette.Pack(Palette.White, alpha);
        var icons = step.GroupIcons;

        if (icons == null || icons.Length < 2)
        {
            var icon = icons is { Length: 1 } ? icons[0] : step.IconId;
            dl.AddImageRounded(host.GetIcon(icon), min, max, new Vector2(UvInset), new Vector2(1f - UvInset), tint, rounding);
            return;
        }

        var mid = new Vector2(MathF.Round((min.X + max.X) * 0.5f), MathF.Round((min.Y + max.Y) * 0.5f));
        var divider = Palette.Pack(Palette.Black, 0.7f * alpha);

        if (icons.Length == 2)
        {
            // Two candidates: the middle half of each, side by side.
            dl.AddImageRounded(host.GetIcon(icons[0]), min, new Vector2(mid.X, max.Y), new Vector2(0.25f, UvInset),
                               new Vector2(0.75f, 1f - UvInset), tint, rounding, ImDrawFlags.RoundCornersLeft);
            dl.AddImageRounded(host.GetIcon(icons[1]), new Vector2(mid.X, min.Y), max, new Vector2(0.25f, UvInset),
                               new Vector2(0.75f, 1f - UvInset), tint, rounding, ImDrawFlags.RoundCornersRight);
            dl.AddLine(new Vector2(mid.X, min.Y), new Vector2(mid.X, max.Y), divider, 1f);
            return;
        }

        // Three or more: a two by two collage of the first four.
        Span<(Vector2 Min, Vector2 Max, ImDrawFlags Corner)> cells =
        [
            (min, mid, ImDrawFlags.RoundCornersTopLeft),
            (new Vector2(mid.X, min.Y), new Vector2(max.X, mid.Y), ImDrawFlags.RoundCornersTopRight),
            (new Vector2(min.X, mid.Y), new Vector2(mid.X, max.Y), ImDrawFlags.RoundCornersBottomLeft),
            (mid, max, ImDrawFlags.RoundCornersBottomRight)
        ];

        dl.AddRectFilled(min, max, Palette.Pack(Palette.Black, 0.55f * alpha), rounding);
        for (var i = 0; i < Math.Min(4, icons.Length); i++)
        {
            dl.AddImageRounded(host.GetIcon(icons[i]), cells[i].Min, cells[i].Max, new Vector2(UvInset),
                               new Vector2(1f - UvInset), tint, rounding, cells[i].Corner);
        }

        dl.AddLine(new Vector2(mid.X, min.Y), new Vector2(mid.X, max.Y), divider, 1f);
        dl.AddLine(new Vector2(min.X, mid.Y), new Vector2(max.X, mid.Y), divider, 1f);
    }

    /// <summary>An empty slot with an asterisk: "anything goes here".</summary>
    private static void DrawWildcard(ImDrawListPtr dl, Vector2 min, Vector2 max, float alpha, float rounding)
    {
        var size = max.X - min.X;
        var center = (min + max) * 0.5f;
        var reach = size * 0.2f;
        var thickness = MathF.Max(1.5f, size * 0.07f);
        var stroke = Palette.Pack(Palette.White, 0.78f * alpha);

        dl.AddRectFilled(min, max, Palette.Pack(Palette.White, 0.08f * alpha), rounding);
        dl.AddRect(min, max, Palette.Pack(Palette.White, 0.3f * alpha), rounding, MathF.Max(1f, size * 0.03f));

        for (var arm = 0; arm < 3; arm++)
        {
            var (sin, cos) = MathF.SinCos(MathF.PI / 2f + arm * MathF.PI / 3f);
            var offset = new Vector2(cos * reach, sin * reach);
            dl.AddLine(center - offset, center + offset, stroke, thickness);
        }
    }

    /// <summary>
    /// A compact, non-interactive row of icons used for previews in lists. Draws as many as fit and
    /// returns how many steps were left out.
    /// </summary>
    public static int DrawMiniStrip(
        ImDrawListPtr dl, ITimelineHost host, ReadOnlySpan<TimelineStep> steps, Vector2 origin, float width, float gcdSize,
        float alpha)
    {
        var offSize = MathF.Round(gcdSize * 0.78f);
        var gap = MathF.Max(2f, MathF.Round(gcdSize * 0.1f));
        var x = origin.X;

        for (var i = 0; i < steps.Length; i++)
        {
            var size = steps[i].IsGcd ? gcdSize : offSize;
            if (x + size > origin.X + width)
                return steps.Length - i;

            var top = origin.Y + (gcdSize - size) * 0.5f;
            var min = new Vector2(MathF.Round(x), MathF.Round(top));
            Draw(dl, host, steps[i], min, min + new Vector2(size), alpha, size * RoundingRatio);
            x += size + gap;
        }

        return 0;
    }
}
