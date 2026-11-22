using System;
using System.Collections.Generic;

namespace OpenerTimeline.UI.Timeline;

/// <summary>Resolved pixel measurements for one style. Vertical values are offsets from the top of the overlay.</summary>
public struct TimelineMetrics
{
    public float Scale;
    public float Padding;
    public float Rounding;

    /// <summary>Compact view: height of the text line that floats above the panel.</summary>
    public float StatusHeight;

    /// <summary>Where the panel begins; below the status line in the compact view.</summary>
    public float PanelTop;

    public float HeaderHeight;
    public float ChipHeight;
    public float ChipGap;
    public float GcdSize;
    public float OffSize;
    public float Gap;

    /// <summary>Width reserved on the left for the lane labels.</summary>
    public float Gutter;

    /// <summary>Row of "GCD n" notes above the icons.</summary>
    public float CalloutTop;

    public float CalloutHeight;

    public float GcdIconTop;
    public float OffIconTop;
    public float GcdChipTop;
    public float OffChipTop;

    /// <summary>Row of GCD numbers and times under the icons.</summary>
    public float RulerTop;

    public float RulerHeight;

    /// <summary>Top of the area that scrolls sideways.</summary>
    public float StripTop;

    /// <summary>The line both lanes are threaded on.</summary>
    public float RailY;

    public float TotalHeight;
}

/// <summary>Pure layout maths for the timeline, kept apart from drawing so it can be tested.</summary>
public static class TimelineLayout
{
    /// <summary>Where the current step sits in the full view once the strip has to scroll (0 = left, 1 = right).</summary>
    public const float Anchor = 0.3f;

    /// <summary>Where the current step always sits in the compact view.</summary>
    public const float CompactAnchor = 0.22f;

    public static TimelineMetrics Measure(in TimelineStyle style)
    {
        var s = Math.Max(0.5f, style.Scale);
        var m = new TimelineMetrics
        {
            Scale = s,
            ChipHeight = style.ShowHints ? MathF.Round(17f * s) : 0f,
            ChipGap = style.ShowHints ? MathF.Round(5f * s) : 0f,
            GcdSize = MathF.Round(Math.Clamp(style.IconSize, 16f, 128f) * s),
            Gap = MathF.Round(Math.Clamp(style.Gap, 0f, 32f) * s)
        };
        m.OffSize = MathF.Round(m.GcdSize * Math.Clamp(style.OffGcdScale, 0.4f, 1f));

        if (style.Compact)
        {
            // A single row with as little around it as possible.
            m.Padding = MathF.Round(7f * s);
            m.Rounding = MathF.Round(8f * s);
            m.StatusHeight = style.ShowStatus ? MathF.Round(18f * s) : 0f;
            m.PanelTop = m.StatusHeight;
            m.StripTop = m.PanelTop;
            m.GcdIconTop = m.PanelTop + m.Padding;
            m.OffIconTop = m.GcdIconTop + MathF.Round((m.GcdSize - m.OffSize) * 0.5f);
            m.GcdChipTop = m.GcdIconTop + m.GcdSize + m.ChipGap;
            m.OffChipTop = m.GcdChipTop;
            m.RailY = MathF.Round(m.GcdIconTop + m.GcdSize * 0.5f);
            m.TotalHeight = m.GcdChipTop + m.ChipHeight + m.Padding;
            return m;
        }

        m.Padding = MathF.Round(10f * s);
        m.Rounding = MathF.Round(10f * s);
        m.HeaderHeight = style.ShowHeader ? MathF.Round(22f * s) : 0f;
        m.CalloutHeight = style.Callouts != BurstCallouts.Off ? MathF.Round(16f * s) : 0f;
        m.RulerHeight = style.ShowRuler ? MathF.Round(16f * s) : 0f;

        var headerGap = style.ShowHeader ? MathF.Round(8f * s) : 0f;
        var top = m.Padding + m.HeaderHeight + headerGap;
        m.StripTop = m.Padding + m.HeaderHeight;
        m.CalloutTop = top;
        top += m.CalloutHeight;

        // Without key caps the notes would sit right on the badges that stick out above the icons.
        if (m.CalloutHeight > 0f && m.ChipHeight <= 0f)
            top += MathF.Round(6f * s);

        if (style.Layout == TimelineLayoutMode.Staggered)
        {
            // Abilities ride above the line, their key caps above them; global cooldowns hang below
            // it with their key caps underneath. Because every step has its own column the two
            // lanes can overlap vertically, which is what makes it read as a single line.
            m.Gutter = style.ShowLaneLabels ? MathF.Round(38f * s) : 0f;
            m.OffChipTop = top;
            m.OffIconTop = m.OffChipTop + m.ChipHeight + m.ChipGap;
            m.GcdIconTop = m.OffIconTop + MathF.Round(m.OffSize * 0.6f);
            m.GcdChipTop = m.GcdIconTop + m.GcdSize + m.ChipGap;
            m.RailY = MathF.Round((m.OffIconTop + m.OffSize + m.GcdIconTop) * 0.5f);
        }
        else
        {
            m.Gutter = 0f;
            m.GcdIconTop = top;
            m.OffIconTop = top + MathF.Round((m.GcdSize - m.OffSize) * 0.5f);
            m.GcdChipTop = m.GcdIconTop + m.GcdSize + m.ChipGap;
            m.OffChipTop = m.GcdChipTop;
            m.RailY = MathF.Round(m.GcdIconTop + m.GcdSize * 0.5f);
        }

        m.RulerTop = m.GcdChipTop + m.ChipHeight + (m.RulerHeight > 0f ? MathF.Round(3f * s) : 0f);
        m.TotalHeight = m.RulerTop + m.RulerHeight + m.Padding;
        return m;
    }

    /// <summary>Assigns every step its column. Returns the width of the whole strip.</summary>
    /// <param name="steps">The opener.</param>
    /// <param name="metrics">Measurements from <see cref="Measure"/>.</param>
    /// <param name="left">Receives the left edge of each step, relative to the start of the strip.</param>
    public static float Arrange(IReadOnlyList<TimelineStep> steps, in TimelineMetrics metrics, float[] left)
    {
        var cursor = 0f;
        for (var i = 0; i < steps.Count; i++)
        {
            left[i] = cursor;
            cursor += (steps[i].IsGcd ? metrics.GcdSize : metrics.OffSize) + metrics.Gap;
        }

        return steps.Count == 0 ? 0f : cursor - metrics.Gap;
    }

    /// <summary>
    /// How far the strip is scrolled so the focused step stays in view. A strip that fits is never
    /// scrolled; a longer one keeps the focus at <see cref="Anchor"/> without scrolling past its ends.
    /// </summary>
    public static float ScrollTarget(float contentWidth, float viewWidth, float focusCenter)
    {
        if (contentWidth <= viewWidth)
            return 0f;

        return Math.Clamp(focusCenter - viewWidth * Anchor, 0f, contentWidth - viewWidth);
    }

    /// <summary>
    /// Scroll for the compact view: the focused step is pinned to one spot, even if that leaves
    /// empty space before the first or after the last step. The eye never has to hunt for it.
    /// </summary>
    public static float PinnedScrollTarget(float viewWidth, float focusCenter)
    {
        return focusCenter - viewWidth * CompactAnchor;
    }
}
