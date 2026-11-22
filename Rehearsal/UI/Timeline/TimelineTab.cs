using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Training;

namespace Rehearsal.UI.Timeline;

/// <summary>
/// The minimized timeline: a small pill that says where the run stands and brings the timeline
/// back when clicked. Drawing only, like <see cref="TimelineRenderer"/>, so the preview tool can
/// render it too.
/// </summary>
public static class TimelineTab
{
    /// <summary>What the pill says, split into the parts that are coloured differently.</summary>
    private readonly record struct Content(string Job, string Detail, string Status, Vector3 StatusColor);

    /// <summary>The size the pill needs for this frame.</summary>
    public static Vector2 Measure(TimelineFrame frame, in TimelineStyle style, ITimelineHost host)
    {
        using var font = host.PushFont(TimelineFont.Small);
        var s = MathF.Max(0.5f, style.Scale);
        var content = Describe(frame, style);
        var gap = MathF.Round(7f * s);

        var width = MathF.Round(10f * s) * 2f + DotSize(s) + Room(content.Job, gap) + Room(content.Detail, gap) + Room(content.Status, gap);
        return new Vector2(MathF.Round(width), MathF.Round(24f * s));
    }

    /// <summary>The width a piece of text takes up, including the gap before it; nothing when it is empty.</summary>
    private static float Room(string text, float gap)
    {
        return text.Length > 0 ? gap + ImGui.CalcTextSize(text).X : 0f;
    }

    public static void Render(
        ImDrawListPtr dl, Vector2 pos, Vector2 size, TimelineFrame frame, in TimelineStyle style, ITimelineHost host, bool hovered,
        double time)
    {
        var alpha = Gfx.Saturate(frame.Opacity);
        if (alpha <= 0.005f)
            return;

        using var font = host.PushFont(TimelineFont.Small);
        var s = MathF.Max(0.5f, style.Scale);
        var min = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        var max = min + size;
        var rounding = size.Y * 0.5f;

        if (style.Blur > 0.01f)
            host.DrawBackdrop(dl, min, max, rounding, style.Blur, alpha);

        // Never as see-through as the timeline may be set to: this one is there to be found.
        dl.AddRectFilled(min, max, Palette.Pack(Palette.Surface, MathF.Max(style.PanelOpacity, 0.55f) * alpha), rounding);
        if (hovered)
            dl.AddRectFilled(min, max, Palette.Pack(Palette.White, 0.08f * alpha), rounding);
        dl.AddRect(min, max, Palette.Pack(Palette.White, 0.13f * alpha), rounding, 1f);
        if (frame.Unlocked)
            dl.AddRect(min, max, Palette.Pack(style.Accent, 0.8f * alpha), rounding, MathF.Max(1f, 1.5f * s));

        var content = Describe(frame, style);
        var centerY = MathF.Round((min.Y + max.Y) * 0.5f);
        var gap = MathF.Round(7f * s);
        var x = min.X + MathF.Round(10f * s);

        // A status light: it breathes while waiting for the pull, like the READY in the header.
        var pulse = frame.State == RunState.Armed && frame.Steps.Count > 0 ? 0.7f + 0.3f * MathF.Sin((float)time * 3.2f) : 1f;
        var dot = DotSize(s);
        dl.AddCircleFilled(new Vector2(x + dot * 0.5f, centerY), dot * 0.5f, Palette.Pack(content.StatusColor, alpha * pulse), 14);
        x += dot;

        if (content.Job.Length > 0)
        {
            x += gap;
            x += Gfx.TextLeft(dl, x, centerY, Palette.Pack(Palette.Mix(style.Accent, Palette.White, 0.25f), alpha), content.Job);
        }

        if (content.Detail.Length > 0)
        {
            x += gap;
            x += Gfx.TextLeft(dl, x, centerY, Palette.Pack(Palette.White, Palette.TextDimAlpha * alpha), content.Detail);
        }

        if (content.Status.Length > 0)
            Gfx.TextLeft(dl, x + gap, centerY, Palette.Pack(content.StatusColor, alpha), content.Status);
    }

    private static float DotSize(float s)
    {
        return MathF.Round(7f * s);
    }

    private static Content Describe(TimelineFrame frame, in TimelineStyle style)
    {
        var total = frame.Steps.Count;
        if (frame.Notice != null || total == 0)
            return new Content(frame.JobLabel, string.Empty, "No opener", Palette.LongCooldown);

        if (frame.Countdown >= 0f)
            return new Content(frame.JobLabel, string.Empty, "PULL IN " + frame.Countdown.ToString("0.0"), style.Accent);

        var (status, color) = TimelineRenderer.StatusOf(frame, style);
        if (frame.State == RunState.Armed)
            return new Content(frame.JobLabel, string.Empty, status, color);

        // While it runs the quiet styles have no status; the counter says enough.
        var counter = $"{Math.Min(total, frame.Hits + frame.Skipped)}/{total}";
        return new Content(frame.JobLabel, counter, status, status.Length > 0 ? color : style.Accent);
    }
}
