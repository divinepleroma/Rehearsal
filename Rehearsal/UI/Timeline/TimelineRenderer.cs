using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.Training;

namespace Rehearsal.UI.Timeline;

/// <summary>
/// Draws the live opener timeline into a draw list. It owns nothing but animation state, and talks
/// to the outside through <see cref="ITimelineHost"/>, so the exact same code renders in game and
/// in the offline preview tool.
/// </summary>
public sealed class TimelineRenderer
{
    /// <summary>How long the icon of a wrong press stays on screen, in seconds.</summary>
    public const float WrongPressLinger = 1.5f;

    private static readonly string[] Ordinals = Enumerable.Range(0, 128).Select(n => n.ToString()).ToArray();
    private static readonly string[] GcdLabels = Enumerable.Range(0, 128).Select(n => n == 0 ? "pre-pull" : $"GCD {n}").ToArray();

    private float[] done = new float[64];
    private float[] focus = new float[64];
    private string fittedTitle = string.Empty;
    private float fittedTitleWidth = -1f;
    private string fittedTitleSource = string.Empty;
    private int lastCount = -1;
    private int lastRunId = int.MinValue;
    private float[] left = new float[64];
    private float manualScroll;
    private float progress;
    private int progressRunId = int.MinValue;
    private float scroll;
    private string?[] timeText = new string?[64];
    private float[] timeValue = new float[64];

    public float MeasureHeight(in TimelineStyle style)
    {
        return TimelineLayout.Measure(style).TotalHeight;
    }

    /// <summary>Scrolls the strip by hand. The offset is dropped as soon as a run is in progress.</summary>
    public void ScrollBy(float pixels)
    {
        manualScroll += pixels;
    }

    public void Render(
        ImDrawListPtr dl, Vector2 pos, Vector2 size, TimelineFrame frame, in TimelineStyle style, ITimelineHost host, float dt,
        double time)
    {
        var alpha = Gfx.Saturate(frame.Opacity);
        if (alpha <= 0.005f || size.X < 8f || size.Y < 8f)
            return;

        var m = TimelineLayout.Measure(style);
        var min = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        var max = new Vector2(MathF.Round(pos.X + size.X), MathF.Round(pos.Y + size.Y));
        var panelMin = new Vector2(min.X, min.Y + m.PanelTop);

        DrawPanel(dl, panelMin, max, frame, style, m, host, alpha);

        // The buttons share the header (or status line) with its text, which stops short of them.
        var controls = TimelineLayout.Controls(style, m, min, max);
        var textRight = frame.Controls ? controls.Left - MathF.Round(8f * m.Scale) : float.MaxValue;

        if (style.Compact)
        {
            if (m.StatusHeight > 0f)
                DrawStatusLine(dl, min, max, frame, style, m, host, alpha, time, textRight);
        }
        else if (style.ShowHeader)
        {
            DrawHeader(dl, min, max, frame, style, m, host, alpha, time, textRight);
        }

        if (frame.Notice != null || frame.Steps.Count == 0)
        {
            DrawNotice(dl, panelMin, max, frame, m, host, alpha);
        }
        else
        {
            DrawStrip(dl, min, max, frame, style, m, host, alpha, dt, time);

            if (style.ShowProgress)
                DrawProgress(dl, min, max, frame, style, m, alpha, dt);
        }

        if (frame.Controls)
            DrawControls(dl, controls, frame.HoveredControl, m.Scale, alpha);
    }

    /// <summary>Minimize and hide: two small buttons, drawn only while the mouse can reach the overlay.</summary>
    private static void DrawControls(ImDrawListPtr dl, in TimelineControls controls, int hovered, float s, float alpha)
    {
        var size = new Vector2(controls.Size);
        var rounding = MathF.Round(4f * s);
        var thickness = MathF.Max(1.5f, 1.6f * s);

        for (var i = 0; i < 2; i++)
        {
            var min = i == 0 ? controls.Minimize : controls.Hide;
            var max = min + size;
            var isHovered = hovered == i;

            // Their own backing: without a header they sit on top of the icons.
            dl.AddRectFilled(min, max, Palette.Pack(Palette.Surface, 0.72f * alpha), rounding);
            dl.AddRectFilled(min, max, Palette.Pack(Palette.White, (isHovered ? 0.22f : 0.08f) * alpha), rounding);

            var color = Palette.Pack(Palette.White, (isHovered ? 1f : Palette.TextDimAlpha) * alpha);
            var center = new Vector2(MathF.Round((min.X + max.X) * 0.5f), MathF.Round((min.Y + max.Y) * 0.5f));
            var reach = MathF.Round(controls.Size * 0.24f);
            if (i == 0)
            {
                // A bar along the bottom: "put it down there".
                var y = center.Y + reach;
                dl.AddLine(new Vector2(center.X - reach, y), new Vector2(center.X + reach, y), color, thickness);
            }
            else
            {
                Gfx.Cross(dl, center, reach, color, thickness);
            }
        }
    }

    private static void DrawPanel(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineStyle style, in TimelineMetrics m,
        ITimelineHost host, float alpha)
    {
        if (style.Blur > 0.01f)
            host.DrawBackdrop(dl, min, max, m.Rounding, style.Blur, alpha);

        dl.AddRectFilled(min, max, Palette.Pack(Palette.Surface, style.PanelOpacity * alpha), m.Rounding);

        // A hairline of light along the top edge is what sells the "sheet of glass" look.
        var edge = Gfx.Saturate(0.25f + style.PanelOpacity);
        dl.AddLine(new Vector2(min.X + m.Rounding, min.Y + 1f), new Vector2(max.X - m.Rounding, min.Y + 1f),
                   Palette.Pack(Palette.White, 0.07f * alpha * edge), 1f);
        dl.AddRect(min, max, Palette.Pack(Palette.White, 0.10f * alpha * edge), m.Rounding, 1f);

        if (frame.Unlocked)
            dl.AddRect(min, max, Palette.Pack(style.Accent, 0.8f * alpha), m.Rounding, MathF.Max(1f, 1.5f * m.Scale));
    }

    // ---- Header (full view) ----

    private void DrawHeader(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineStyle style, in TimelineMetrics m,
        ITimelineHost host, float alpha, double time, float textRight)
    {
        var s = m.Scale;
        var centerY = MathF.Round(min.Y + m.Padding + m.HeaderHeight * 0.5f);
        var leftX = min.X + m.Padding;
        var rightX = MathF.Min(max.X - m.Padding, textRight);
        var gap = MathF.Round(10f * s);
        var total = frame.Steps.Count;

        // Right side first: timer, counter, status. Whatever is left belongs to the title.
        using (host.PushFont(TimelineFont.Number))
        {
            if (frame.Countdown >= 0f)
            {
                var text = "-" + frame.Countdown.ToString("0.0");
                rightX -= Gfx.TextRight(dl, rightX, centerY, Palette.Pack(style.Accent, alpha), text) + gap;
            }
            else if (frame.State != RunState.Armed && total > 0)
            {
                rightX -= Gfx.TextRight(dl, rightX, centerY, Palette.Pack(Palette.White, Palette.TextAlpha * alpha),
                                        FormatTime(frame.Elapsed)) + gap;
            }

            if (total > 0)
            {
                var counter = $"{Math.Min(total, frame.Hits + frame.Skipped)}/{total}";
                rightX -= Gfx.TextRight(dl, rightX, centerY, Palette.Pack(Palette.White, Palette.TextDimAlpha * alpha), counter) + gap;
            }
        }

        using (host.PushFont(TimelineFont.Small))
        {
            var pillHeight = MathF.Round(17f * s);
            var pillPad = MathF.Round(7f * s);

            var (status, statusColor) = StatusOf(frame, style);
            if (status.Length > 0)
            {
                var pulse = frame.State == RunState.Armed ? 0.75f + 0.25f * MathF.Sin((float)time * 3.2f) : 1f;
                rightX -= Gfx.Pill(dl, rightX, centerY, pillHeight, pillPad, status, Palette.Pack(statusColor, 0.17f * alpha),
                                   Palette.Pack(statusColor, alpha * pulse), true) + gap;
            }

            // Job tag.
            if (frame.JobLabel.Length > 0)
            {
                leftX += Gfx.Pill(dl, leftX, centerY, pillHeight, pillPad, frame.JobLabel, Palette.Pack(style.Accent, 0.18f * alpha),
                                  Palette.Pack(Palette.Mix(style.Accent, Palette.White, 0.2f), alpha)) + MathF.Round(8f * s);
            }

            // Legend (or the unlock hint) goes between title and status when there is room for it.
            if (frame.Unlocked)
            {
                const string hint = "Drag to move  -  right-click for options";
                var hintWidth = ImGui.CalcTextSize(hint).X;
                if (rightX - hintWidth > leftX + MathF.Round(120f * s))
                {
                    Gfx.TextRight(dl, rightX, centerY, Palette.Pack(Palette.Mix(style.Accent, Palette.White, 0.3f), 0.9f * alpha), hint);
                    rightX -= hintWidth + gap;
                }
            }
            else if (style.Badges != CooldownBadgeMode.Off && total > 0)
            {
                var legendWidth = MeasureLegend(s);
                if (rightX - legendWidth > leftX + MathF.Round(160f * s))
                {
                    DrawLegend(dl, rightX - legendWidth, centerY, s, alpha);
                    rightX -= legendWidth + gap;
                }
            }
        }

        using (host.PushFont(TimelineFont.Body))
        {
            var title = FitTitle(frame.Title, rightX - leftX - gap);
            if (title.Length > 0)
                Gfx.TextLeft(dl, leftX, centerY, Palette.Pack(Palette.White, Palette.TextAlpha * alpha), title);
        }
    }

    // ---- Status line (compact view) ----

    /// <summary>
    /// The compact view has no header; what little needs saying floats above the strip as plain
    /// text with a soft shadow, so it reads on any scenery without adding a box.
    /// </summary>
    private static void DrawStatusLine(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineStyle style, in TimelineMetrics m,
        ITimelineHost host, float alpha, double time, float textRight)
    {
        var s = m.Scale;
        var centerY = MathF.Round(min.Y + m.StatusHeight * 0.5f - s);
        var leftX = min.X + MathF.Round(4f * s);
        var rightX = MathF.Min(max.X - MathF.Round(4f * s), textRight);
        var total = frame.Steps.Count;
        var gap = MathF.Round(7f * s);

        using var font = host.PushFont(TimelineFont.Small);

        if (frame.JobLabel.Length > 0)
            leftX += Gfx.ShadowText(dl, leftX, centerY, Palette.Mix(style.Accent, Palette.White, 0.25f), alpha, frame.JobLabel) + gap;

        if (frame.Unlocked)
        {
            Gfx.ShadowTextRight(dl, rightX, centerY, Palette.Mix(style.Accent, Palette.White, 0.3f), 0.95f * alpha,
                                "drag  -  right-click for options");
            return;
        }

        if (total == 0)
            return;

        var counter = $"{Math.Min(total, frame.Hits + frame.Skipped)}/{total}";
        rightX -= Gfx.ShadowTextRight(dl, rightX, centerY, Palette.White, Palette.TextDimAlpha * alpha, counter) + gap;

        if (frame.Countdown >= 0f)
        {
            Gfx.ShadowText(dl, leftX, centerY, style.Accent, alpha, "pull in " + frame.Countdown.ToString("0.0"));
            return;
        }

        var (status, statusColor) = StatusOf(frame, style);
        if (status.Length > 0)
        {
            var pulse = frame.State == RunState.Armed ? 0.75f + 0.25f * MathF.Sin((float)time * 3.2f) : 1f;
            leftX += Gfx.ShadowText(dl, leftX, centerY, statusColor, alpha * pulse, status) + gap;
        }

        if (frame.State != RunState.Armed)
            Gfx.ShadowText(dl, leftX, centerY, Palette.White, Palette.TextAlpha * alpha, FormatTime(frame.Elapsed));
    }

    /// <summary>The run in a word or two ("READY", "CLEAN RUN"...) and its colour; empty when there is nothing to say.</summary>
    public static (string Text, Vector3 Color) StatusOf(TimelineFrame frame, in TimelineStyle style)
    {
        if (frame.Steps.Count == 0)
            return (string.Empty, Palette.White);

        if (frame.Countdown >= 0f)
            return ("PULL IN", style.Accent);

        var mistakes = frame.Wrong + frame.Skipped;
        var mistakeColor = style.Feedback == FeedbackStyle.Strong ? Palette.Bad : Palette.Slip;

        switch (frame.State)
        {
            case RunState.Armed:
                return ("READY", style.Accent);
            case RunState.Running:
                // Only the loud style keeps score while you are still playing.
                if (style.Feedback != FeedbackStyle.Strong || mistakes == 0)
                    return (string.Empty, Palette.White);
                return (mistakes == 1 ? "1 MISTAKE" : $"{mistakes} MISTAKES", mistakeColor);
            default:
                return frame.FinishReason switch
                {
                    FinishReason.Completed when mistakes == 0 => ("CLEAN RUN", Palette.Good),
                    FinishReason.Completed when mistakes == 1 => ("1 MISTAKE", mistakeColor),
                    FinishReason.Completed => ($"{mistakes} MISTAKES", mistakeColor),
                    FinishReason.StoppedOnMistake => ("STOPPED", mistakeColor),
                    FinishReason.CombatEnded or FinishReason.TimedOut => ("INCOMPLETE", Palette.Warn),
                    _ => ("RESET", Palette.LongCooldown)
                };
        }
    }

    private static float MeasureLegend(float s)
    {
        var dot = MathF.Round(12f * s);
        var gap = MathF.Round(10f * s);
        return dot + ImGui.CalcTextSize("60s").X + gap + dot + ImGui.CalcTextSize("120s").X;
    }

    private static void DrawLegend(ImDrawListPtr dl, float x, float centerY, float s, float alpha)
    {
        var radius = 3.2f * s;
        var dot = MathF.Round(12f * s);
        var gap = MathF.Round(10f * s);
        var text = Palette.Pack(Palette.White, Palette.TextFaintAlpha * 1.25f * alpha);

        dl.AddCircleFilled(new Vector2(x + radius, centerY), radius, Palette.Pack(Palette.OneMinute, alpha), 12);
        x += dot;
        x += Gfx.TextLeft(dl, x, centerY, text, "60s") + gap;

        dl.AddCircleFilled(new Vector2(x + radius, centerY), radius, Palette.Pack(Palette.TwoMinute, alpha), 12);
        x += dot;
        Gfx.TextLeft(dl, x, centerY, text, "120s");
    }

    private static void DrawNotice(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineMetrics m, ITimelineHost host, float alpha)
    {
        var text = frame.Notice ?? "This opener has no actions yet.";
        using (host.PushFont(TimelineFont.Body))
        {
            var top = min.Y + m.Padding + m.HeaderHeight;
            var center = new Vector2((min.X + max.X) * 0.5f, (top + max.Y) * 0.5f);
            dl.PushClipRect(min, max, true);
            Gfx.TextCentered(dl, center, Palette.Pack(Palette.White, Palette.TextDimAlpha * alpha), text);
            dl.PopClipRect();
        }
    }

    // ---- The strip ----

    private void DrawStrip(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineStyle style, in TimelineMetrics m,
        ITimelineHost host, float alpha, float dt, double time)
    {
        var steps = frame.Steps;
        var count = steps.Count;
        EnsureCapacity(count);

        var s = m.Scale;
        var clipMin = new Vector2(min.X + m.Padding + m.Gutter, min.Y + m.StripTop);
        var clipMax = new Vector2(max.X - m.Padding, max.Y - 1f);
        var viewWidth = clipMax.X - clipMin.X;
        if (viewWidth <= 4f)
            return;

        // A little air at both ends so the glow around the first and last icon is not clipped.
        var inset = MathF.Round((style.Compact ? 5f : 8f) * s);
        var contentWidth = TimelineLayout.Arrange(steps, m, left) + inset * 2f;

        var snap = frame.RunId != lastRunId || count != lastCount;
        lastRunId = frame.RunId;
        lastCount = count;

        // Follow the step that is expected next.
        var focusIndex = Math.Clamp(frame.CurrentIndex, 0, count - 1);
        var focusWidth = steps[focusIndex].IsGcd ? m.GcdSize : m.OffSize;
        var focusCenter = inset + left[focusIndex] + focusWidth * 0.5f;

        float target, minScroll, maxScroll;
        if (style.Compact)
        {
            target = TimelineLayout.PinnedScrollTarget(viewWidth, focusCenter);
            minScroll = -viewWidth * TimelineLayout.CompactAnchor;
            maxScroll = contentWidth - viewWidth * TimelineLayout.CompactAnchor;
        }
        else
        {
            target = TimelineLayout.ScrollTarget(contentWidth, viewWidth, focusCenter);
            minScroll = 0f;
            maxScroll = MathF.Max(0f, contentWidth - viewWidth);
        }

        if (frame.State == RunState.Running)
            manualScroll = 0f;
        manualScroll = Math.Clamp(manualScroll, minScroll - target, maxScroll - target);
        target += manualScroll;
        scroll = snap ? target : Gfx.Approach(scroll, target, 12f, dt);

        using var smallFont = host.PushFont(TimelineFont.Small);

        if (m.Gutter > 0f)
        {
            var label = Palette.Pack(Palette.White, Palette.TextFaintAlpha * alpha);
            Gfx.TextLeft(dl, min.X + m.Padding, min.Y + m.OffIconTop + m.OffSize * 0.42f, label, "oGCD");
            Gfx.TextLeft(dl, min.X + m.Padding, min.Y + m.GcdIconTop + m.GcdSize * 0.58f, label, "GCD");
        }

        dl.PushClipRect(clipMin, clipMax, true);

        // The line everything is threaded on.
        var railY = min.Y + m.RailY;
        dl.AddLine(new Vector2(clipMin.X, railY), new Vector2(clipMax.X, railY), Palette.Pack(Palette.White, 0.08f * alpha),
                   MathF.Max(1f, MathF.Round(1.5f * s)));

        var finished = frame.State == RunState.Finished;
        var fadeDistance = (style.Compact ? 22f : 30f) * s;
        var staggered = !style.Compact && style.Layout == TimelineLayoutMode.Staggered;
        var strong = style.Feedback == FeedbackStyle.Strong;
        var slipColor = strong ? Palette.Bad : Palette.Slip;
        var calloutRight = float.MinValue;

        for (var i = 0; i < count; i++)
        {
            var step = steps[i];
            var isCurrent = step.Visual == StepVisual.Current;
            var isDone = step.Visual is StepVisual.Done or StepVisual.DoneAfterMistake or StepVisual.Skipped;

            focus[i] = snap ? (isCurrent ? 1f : 0f) : Gfx.Approach(focus[i], isCurrent ? 1f : 0f, 16f, dt);
            done[i] = snap ? (isDone ? 1f : 0f) : Gfx.Approach(done[i], isDone ? 1f : 0f, 10f, dt);

            var width = step.IsGcd ? m.GcdSize : m.OffSize;
            var x = clipMin.X + inset + left[i] - scroll;
            if (x > clipMax.X + width || x + width < clipMin.X - width)
                continue;

            var top = min.Y + (step.IsGcd ? m.GcdIconTop : m.OffIconTop);
            var center = new Vector2(MathF.Round(x + width * 0.5f), MathF.Round(top + width * 0.5f));

            // Icons dissolve into the panel where the strip continues out of view.
            var edge = 1f;
            if (style.Compact || scroll > 0.5f)
                edge = MathF.Min(edge, Gfx.Saturate((center.X - clipMin.X) / fadeDistance));
            if (style.Compact || scroll < maxScroll - 0.5f)
                edge = MathF.Min(edge, Gfx.Saturate((clipMax.X - center.X) / fadeDistance));

            // While running, what is behind you recedes. Once finished it comes back for review and
            // the part that was never reached recedes instead.
            float stateAlpha;
            if (finished)
                stateAlpha = step.Visual switch
                {
                    StepVisual.Skipped => 0.45f,
                    StepVisual.Done or StepVisual.DoneAfterMistake => 0.92f,
                    _ => 0.32f
                };
            else
                stateAlpha = Gfx.Lerp(1f, step.Visual == StepVisual.Skipped ? 0.3f : 0.4f, done[i]);

            var a = alpha * edge * stateAlpha;
            if (a <= 0.01f)
                continue;

            var wrongHere = style.Feedback != FeedbackStyle.Off && i == frame.CurrentIndex && frame.WrongAge >= 0f
                            && frame.WrongAge < WrongPressLinger;

            // Only the loud style moves the icon you are trying to hit.
            if (wrongHere && strong && frame.WrongAge < 0.35f)
                center.X += MathF.Sin(frame.WrongAge * 62f) * 4f * s * (1f - frame.WrongAge / 0.35f);

            var half = width * 0.5f * (1f + 0.07f * focus[i]);
            var rmin = new Vector2(MathF.Round(center.X - half), MathF.Round(center.Y - half));
            var rmax = new Vector2(MathF.Round(center.X + half), MathF.Round(center.Y + half));
            var rounding = MathF.Round((rmax.X - rmin.X) * IconPainter.RoundingRatio);

            // The gentle wrong-press cue sits where a finished step would: just behind the current
            // one, over an icon that is already done, so the target and its key stay untouched.
            if (wrongHere && !strong)
                DrawSlip(dl, host, frame, rmin, rmax, s, alpha * edge);

            // Drop shadow, then the icon and a hairline frame to hold it together on bright scenery.
            dl.AddRectFilled(rmin + new Vector2(0f, 2f * s), rmax + new Vector2(0f, 3f * s), Palette.Pack(Palette.Black, 0.38f * a),
                             rounding);
            IconPainter.Draw(dl, host, step, rmin, rmax, a, rounding);
            dl.AddRect(rmin, rmax, Palette.Pack(Palette.White, 0.16f * a), rounding, 1f);

            if (focus[i] > 0.01f && !finished)
            {
                // After a slip the ring warms up for a moment and settles back.
                var ringColor = style.Accent;
                if (wrongHere && !strong)
                    ringColor = Palette.Mix(Palette.Slip, style.Accent, Gfx.Saturate(frame.WrongAge / 0.9f));

                var pulse = 0.72f + 0.28f * MathF.Sin((float)time * 4.2f);
                var glowAlpha = focus[i] * alpha * edge;
                Gfx.Glow(dl, rmin, rmax, ringColor, rounding, glowAlpha * pulse, s);
                dl.AddRect(rmin - new Vector2(s), rmax + new Vector2(s), Palette.Pack(ringColor, 0.95f * glowAlpha), rounding + s,
                           MathF.Max(1.5f, 2f * s));

                if (wrongHere && !strong && frame.WrongAge < 0.6f)
                {
                    // One ripple outward: noticeable in the corner of the eye, gone in a blink.
                    var ripple = frame.WrongAge / 0.6f;
                    var grow = (3f + ripple * 9f) * s;
                    dl.AddRect(rmin - new Vector2(grow), rmax + new Vector2(grow), Palette.Pack(Palette.Slip, 0.55f * (1f - ripple) * glowAlpha),
                               rounding + grow, MathF.Max(1f, 1.5f * s));
                }
            }

            if (isCurrent && frame.CastProgress >= 0f && !finished)
            {
                // Cast bar along the bottom of the icon; a different colour if it is the wrong spell.
                var barHeight = MathF.Round(5f * s);
                var barColor = frame.CastMatches ? style.Accent : slipColor;
                var barMin = new Vector2(rmin.X, rmax.Y - barHeight);
                dl.AddRectFilled(barMin, rmax, Palette.Pack(Palette.Black, 0.6f * a), rounding, ImDrawFlags.RoundCornersBottom);
                var fill = rmin.X + (rmax.X - rmin.X) * Gfx.Saturate(frame.CastProgress);
                dl.PushClipRect(barMin, new Vector2(fill, rmax.Y), true);
                dl.AddRectFilled(barMin, rmax, Palette.Pack(barColor, a), rounding, ImDrawFlags.RoundCornersBottom);
                dl.PopClipRect();
            }

            DrawOutcomeMark(dl, step, rmin, rmax, rounding, s, alpha * edge, strong, finished);

            Gfx.CooldownBadge(dl, rmin, rmax, step, style, a);

            var columnCenter = MathF.Round(x + width * 0.5f);
            if (style.ShowHints)
            {
                // Each lane has its own row of key caps, so a cap may spill sideways into a column
                // that belongs to the other lane.
                var room = width + m.Gap;
                if (staggered)
                {
                    var freeLeft = i > 0 && steps[i - 1].IsGcd != step.IsGcd
                                       ? (steps[i - 1].IsGcd ? m.GcdSize : m.OffSize) * 0.5f
                                       : 0f;
                    var freeRight = i + 1 < count && steps[i + 1].IsGcd != step.IsGcd
                                        ? (steps[i + 1].IsGcd ? m.GcdSize : m.OffSize) * 0.5f
                                        : 0f;
                    room += 2f * MathF.Min(freeLeft, freeRight);
                }

                var chipTop = min.Y + (step.IsGcd ? m.GcdChipTop : m.OffChipTop);
                var chipAlpha = alpha * edge * (isDone ? (finished ? 0.6f : 0.35f) : finished ? 0.4f : 1f);
                Gfx.KeyChip(dl, columnCenter, chipTop, m.ChipHeight, room - 2f * s, step.Hint, style, isCurrent && !finished, chipAlpha);
            }

            var noteAlpha = alpha * edge * (isDone && !finished ? 0.4f : 1f);

            if (m.RulerHeight > 0f && step.IsGcd)
                DrawRulerEntry(dl, i, step, columnCenter, min.Y + m.RulerTop + m.RulerHeight * 0.5f, width + m.Gap, isCurrent && !finished, style,
                               s, noteAlpha);

            if (m.CalloutHeight > 0f && WantsCallout(step, style))
            {
                calloutRight = DrawCallout(dl, step, columnCenter, min.Y + m.CalloutTop + m.CalloutHeight * 0.5f, calloutRight, rmin.Y,
                                           staggered && step.IsGcd, s, noteAlpha);
            }

            if (wrongHere && strong)
                DrawWrongPress(dl, host, frame, rmin, rmax, rounding, s, alpha * edge);
        }

        dl.PopClipRect();
    }

    /// <summary>How a finished step went: wrong presses before it, or skipped altogether.</summary>
    private static void DrawOutcomeMark(
        ImDrawListPtr dl, in TimelineStep step, Vector2 rmin, Vector2 rmax, float rounding, float s, float alpha, bool strong,
        bool finished)
    {
        if (step.Visual != StepVisual.DoneAfterMistake && step.Visual != StepVisual.Skipped)
            return;

        var thick = MathF.Max(1.5f, 2f * s);

        if (strong)
        {
            if (step.Visual == StepVisual.DoneAfterMistake)
            {
                var mark = alpha * 0.95f;
                dl.AddRect(rmin, rmax, Palette.Pack(Palette.Bad, mark), rounding, thick);
                var dot = rmin + new Vector2(2f * s);
                dl.AddCircleFilled(dot, 6.5f * s, Palette.Pack(Palette.Bad, mark), 16);
                Gfx.Cross(dl, dot, 2.6f * s, Palette.Pack(Palette.OnAccent, mark), 1.6f * s);
            }
            else
            {
                var mark = alpha * 0.9f;
                dl.AddRect(rmin, rmax, Palette.Pack(Palette.Warn, mark), rounding, thick);
                dl.AddLine(new Vector2(rmin.X + 4f * s, rmax.Y - 4f * s), new Vector2(rmax.X - 4f * s, rmin.Y + 4f * s),
                           Palette.Pack(Palette.Warn, mark), thick);
            }

            return;
        }

        // Quiet marks: a small tick under the icon while you play, a soft outline once it is over
        // and you are looking back at what happened.
        var color = step.Visual == StepVisual.Skipped ? Palette.Warn : Palette.Slip;
        var under = new Vector2(MathF.Round((rmin.X + rmax.X) * 0.5f), rmax.Y + MathF.Round(3f * s));
        if (step.Visual == StepVisual.Skipped)
        {
            var halfDash = MathF.Round(5f * s);
            dl.AddLine(under - new Vector2(halfDash, 0f), under + new Vector2(halfDash, 0f), Palette.Pack(color, 0.9f * alpha), thick);
        }
        else
        {
            dl.AddCircleFilled(under, 2.4f * s, Palette.Pack(color, 0.95f * alpha), 12);
        }

        if (finished)
            dl.AddRect(rmin, rmax, Palette.Pack(color, 0.75f * alpha), rounding, MathF.Max(1f, 1.5f * s));
    }

    /// <summary>The gentle cue for a wrong press: what you hit, shown small beside the step that was expected.</summary>
    private static void DrawSlip(ImDrawListPtr dl, ITimelineHost host, TimelineFrame frame, Vector2 rmin, Vector2 rmax, float s, float alpha)
    {
        if (frame.WrongIconId == 0)
            return;

        var t = frame.WrongAge / WrongPressLinger;
        var fade = alpha * (1f - t * t) * 0.92f;
        if (fade <= 0.01f)
            return;

        var size = MathF.Round((rmax.X - rmin.X) * 0.6f);
        var centerY = (rmin.Y + rmax.Y) * 0.5f;

        // It eases in from the expected icon to its resting place, like something being set aside.
        var settle = 1f - MathF.Pow(1f - Gfx.Saturate(frame.WrongAge / 0.18f), 3f);
        var right = rmin.X - MathF.Round(5f * s) * settle;
        var gmax = new Vector2(MathF.Round(right), MathF.Round(centerY + size * 0.5f));
        var gmin = gmax - new Vector2(size);
        var rounding = size * IconPainter.RoundingRatio;

        dl.AddRectFilled(gmin - new Vector2(s), gmax + new Vector2(s), Palette.Pack(Palette.Surface, 0.9f * fade), rounding + s);
        dl.AddImageRounded(host.GetIcon(frame.WrongIconId), gmin, gmax, Vector2.Zero, Vector2.One, Palette.Pack(Palette.White, 0.85f * fade),
                           rounding);
        dl.AddRect(gmin, gmax, Palette.Pack(Palette.Slip, fade), rounding, MathF.Max(1f, 1.5f * s));
    }

    /// <summary>The loud cue: shows what was pressed instead, crossed out, on the corner of the expected step.</summary>
    private static void DrawWrongPress(
        ImDrawListPtr dl, ITimelineHost host, TimelineFrame frame, Vector2 rmin, Vector2 rmax, float rounding, float s, float alpha)
    {
        var t = frame.WrongAge / WrongPressLinger;
        var fade = alpha * (1f - t * t);

        // A quick red flash on the icon itself.
        var flash = Gfx.Saturate(1f - frame.WrongAge / 0.5f);
        if (flash > 0.01f)
        {
            dl.AddRectFilled(rmin, rmax, Palette.Pack(Palette.Bad, 0.28f * flash * alpha), rounding);
            dl.AddRect(rmin - new Vector2(s), rmax + new Vector2(s), Palette.Pack(Palette.Bad, flash * alpha), rounding + s,
                       MathF.Max(1.5f, 2.5f * s));
        }

        if (frame.WrongIconId == 0 || fade <= 0.01f)
            return;

        var size = MathF.Round((rmax.X - rmin.X) * 0.58f);
        var gmax = new Vector2(rmax.X + size * 0.3f, rmax.Y + size * 0.22f);
        var gmin = gmax - new Vector2(size);
        var ghostRounding = size * IconPainter.RoundingRatio;

        dl.AddRectFilled(gmin - new Vector2(2f * s), gmax + new Vector2(2f * s), Palette.Pack(Palette.Surface, 0.95f * fade),
                         ghostRounding + 2f * s);
        dl.AddImageRounded(host.GetIcon(frame.WrongIconId), gmin, gmax, Vector2.Zero, Vector2.One, Palette.Pack(Palette.White, fade),
                           ghostRounding);
        dl.AddRectFilled(gmin, gmax, Palette.Pack(Palette.Bad, 0.22f * fade), ghostRounding);
        dl.AddRect(gmin, gmax, Palette.Pack(Palette.Bad, fade), ghostRounding, MathF.Max(1.5f, 2f * s));
        Gfx.Cross(dl, (gmin + gmax) * 0.5f, size * 0.2f, Palette.Pack(Palette.White, 0.95f * fade), MathF.Max(1.5f, 2.2f * s));
    }

    // ---- Ruler and callouts ----

    /// <summary>"4 . 5.0" under a global cooldown: which GCD it is and roughly when it happens.</summary>
    private void DrawRulerEntry(
        ImDrawListPtr dl, int index, in TimelineStep step, float centerX, float centerY, float room, bool current, in TimelineStyle style,
        float s, float alpha)
    {
        var number = Ordinals[Math.Clamp(step.GcdNumber, 0, Ordinals.Length - 1)];
        var numberSize = step.GcdNumber > 0 ? ImGui.CalcTextSize(number) : Vector2.Zero;
        var timeLabel = TimeText(index, step.Time);
        var timeSize = ImGui.CalcTextSize(timeLabel);
        var dotGap = MathF.Round(4f * s);

        var numberColor = current
                              ? Palette.Pack(Palette.Mix(style.Accent, Palette.White, 0.2f), alpha)
                              : Palette.Pack(Palette.White, Palette.TextDimAlpha * alpha);
        var timeColor = Palette.Pack(Palette.White, Palette.TextFaintAlpha * alpha);

        // The pre-pull cast may be left uncounted, in which case only its time is shown.
        if (step.GcdNumber <= 0)
        {
            dl.AddText(new Vector2(MathF.Round(centerX - timeSize.X * 0.5f), MathF.Round(centerY - timeSize.Y * 0.5f)), timeColor, timeLabel);
            return;
        }

        var full = numberSize.X + dotGap * 2f + timeSize.X;
        if (full > room - 2f * s)
        {
            // Small icons: there is only room for the number.
            dl.AddText(new Vector2(MathF.Round(centerX - numberSize.X * 0.5f), MathF.Round(centerY - numberSize.Y * 0.5f)), numberColor, number);
            return;
        }

        var x = MathF.Round(centerX - full * 0.5f);
        dl.AddText(new Vector2(x, MathF.Round(centerY - numberSize.Y * 0.5f)), numberColor, number);
        x += numberSize.X + dotGap;
        dl.AddCircleFilled(new Vector2(x, centerY + s), MathF.Max(1f, 1.1f * s), timeColor, 8);
        x += dotGap;
        dl.AddText(new Vector2(x, MathF.Round(centerY - timeSize.Y * 0.5f)), timeColor, timeLabel);
    }

    private static bool WantsCallout(in TimelineStep step, in TimelineStyle style)
    {
        return step.Cooldown switch
        {
            CooldownClass.TwoMinute => true,
            CooldownClass.OneMinute => style.Callouts == BurstCallouts.OneAndTwoMinute,
            _ => false
        };
    }

    /// <summary>
    /// "GCD 4" above a burst cooldown: the global cooldown it goes out on. Returns the right edge of
    /// the note so the next one can keep clear of it.
    /// </summary>
    private static float DrawCallout(
        ImDrawListPtr dl, in TimelineStep step, float centerX, float centerY, float previousRight, float iconTop, bool leader, float s,
        float alpha)
    {
        var label = GcdLabels[Math.Clamp(step.GcdNumber, 0, GcdLabels.Length - 1)];
        var size = ImGui.CalcTextSize(label);
        var color = Gfx.CooldownColor(step.Cooldown);

        var x = MathF.Round(centerX - size.X * 0.5f);
        var spacing = MathF.Round(6f * s);
        if (x < previousRight + spacing)
            x = previousRight + spacing;

        dl.AddText(new Vector2(x, MathF.Round(centerY - size.Y * 0.5f)), Palette.Pack(color, 0.95f * alpha), label);

        // A global cooldown sits a lane further down; tie the note to it with a thin lead.
        if (leader)
        {
            dl.AddLine(new Vector2(centerX, centerY + size.Y * 0.5f + s), new Vector2(centerX, iconTop - 2f * s),
                       Palette.Pack(color, 0.3f * alpha), MathF.Max(1f, s));
        }

        return x + size.X;
    }

    private string TimeText(int index, float seconds)
    {
        if (timeText[index] is { } cached && timeValue[index] == seconds)
            return cached;

        timeValue[index] = seconds;
        var text = FormatSeconds(seconds);
        timeText[index] = text;
        return text;
    }

    /// <summary>Seconds with one decimal; a minus sign before the pull.</summary>
    public static string FormatSeconds(float seconds)
    {
        var tenths = (int)MathF.Round(seconds * 10f);
        var sign = tenths < 0 ? "-" : string.Empty;
        tenths = Math.Abs(tenths);
        return $"{sign}{tenths / 10}.{tenths % 10}";
    }

    // ---- Progress ----

    private void DrawProgress(
        ImDrawListPtr dl, Vector2 min, Vector2 max, TimelineFrame frame, in TimelineStyle style, in TimelineMetrics m, float alpha,
        float dt)
    {
        var count = frame.Steps.Count;
        var s = m.Scale;
        var thickness = MathF.Max(2f, MathF.Round(2f * s));
        var y = max.Y - thickness - MathF.Round(2f * s);
        var x0 = min.X + m.Rounding;
        var x1 = max.X - m.Rounding;

        var fraction = count == 0 ? 0f : Gfx.Saturate((frame.Hits + frame.Skipped) / (float)count);
        progress = frame.RunId != progressRunId ? fraction : Gfx.Approach(progress, fraction, 10f, dt);
        progressRunId = frame.RunId;

        var color = style.Accent;
        if (frame.State == RunState.Finished)
        {
            color = frame.FinishReason switch
            {
                FinishReason.Completed when frame.Wrong + frame.Skipped == 0 => Palette.Good,
                FinishReason.Completed or FinishReason.StoppedOnMistake => style.Feedback == FeedbackStyle.Strong ? Palette.Bad : Palette.Slip,
                _ => Palette.Warn
            };
        }

        dl.AddRectFilled(new Vector2(x0, y), new Vector2(x1, y + thickness), Palette.Pack(Palette.White, 0.07f * alpha), thickness * 0.5f);
        if (progress > 0.002f)
        {
            dl.AddRectFilled(new Vector2(x0, y), new Vector2(x0 + (x1 - x0) * progress, y + thickness), Palette.Pack(color, 0.95f * alpha),
                             thickness * 0.5f);
        }
    }

    private string FitTitle(string title, float maxWidth)
    {
        if (maxWidth <= 8f || title.Length == 0)
            return string.Empty;

        // Truncating measures text repeatedly; only redo it when something changed.
        if (title == fittedTitleSource && MathF.Abs(maxWidth - fittedTitleWidth) < 0.5f)
            return fittedTitle;

        fittedTitleSource = title;
        fittedTitleWidth = maxWidth;

        if (ImGui.CalcTextSize(title).X <= maxWidth)
            return fittedTitle = title;

        const string ellipsis = "...";
        for (var length = title.Length - 1; length > 0; length--)
        {
            var candidate = title[..length].TrimEnd() + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                return fittedTitle = candidate;
        }

        return fittedTitle = string.Empty;
    }

    private static string FormatTime(double seconds)
    {
        var tenths = (long)(Math.Max(0d, seconds) * 10d);
        return $"{tenths / 600}:{tenths % 600 / 10:00}.{tenths % 10}";
    }

    private void EnsureCapacity(int count)
    {
        if (left.Length >= count)
            return;

        var size = Math.Max(count, left.Length * 2);
        Array.Resize(ref left, size);
        Array.Resize(ref focus, size);
        Array.Resize(ref done, size);
        Array.Resize(ref timeText, size);
        Array.Resize(ref timeValue, size);
    }
}
