using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;
using OpenerTimeline.Training;

namespace OpenerTimeline.UI.Timeline;

public enum HintKind : byte
{
    /// <summary>Nothing to show (wildcard steps).</summary>
    None,

    /// <summary>A keyboard or mouse binding.</summary>
    Key,

    /// <summary>On a hotbar, but that slot has no binding: show where it is instead.</summary>
    Slot,

    /// <summary>A cross hotbar position.</summary>
    Pad,

    /// <summary>Not on any hotbar.</summary>
    Missing,

    /// <summary>Several different buttons can satisfy the step.</summary>
    Varies
}

/// <summary>What the player has to press for a step, resolved from their own hotbars and keybinds.</summary>
public struct KeyHint
{
    public HintKind Kind;

    public bool Ctrl;
    public bool Shift;
    public bool Alt;

    /// <summary>Key label for <see cref="HintKind.Key"/>: "1", "Q", "F1", "M4"...</summary>
    public string? Key;

    /// <summary>1-based hotbar number, or cross hotbar set for <see cref="HintKind.Pad"/>.</summary>
    public byte Bar;

    /// <summary>1-based slot on that hotbar.</summary>
    public byte Slot;

    /// <summary>Cross hotbar: held with the right trigger instead of the left.</summary>
    public bool PadRightTrigger;

    /// <summary>Cross hotbar: face buttons instead of the directional pad.</summary>
    public bool PadFace;

    /// <summary>Cross hotbar: 0 left, 1 up, 2 right, 3 down.</summary>
    public byte PadDirection;

    public static KeyHint Missing => new() { Kind = HintKind.Missing };
    public static KeyHint Varies => new() { Kind = HintKind.Varies };
}

public enum StepVisual : byte
{
    Upcoming,
    Current,
    Done,

    /// <summary>Executed, but only after one or more wrong presses.</summary>
    DoneAfterMistake,
    Skipped
}

public struct TimelineStep
{
    public uint IconId;

    /// <summary>For groups: the icons of the members, drawn as a collage.</summary>
    public uint[]? GroupIcons;

    /// <summary>A step that accepts any action; drawn as a placeholder instead of an icon.</summary>
    public bool IsWildcard;

    public string Name;
    public bool IsGcd;
    public float Recast;
    public CooldownClass Cooldown;

    /// <summary>For a GCD its ordinal; otherwise the ordinal of the GCD it is woven after (0: pre-pull).</summary>
    public int GcdNumber;

    /// <summary>Estimated seconds from the pull; negative before it.</summary>
    public float Time;

    public KeyHint Hint;
    public StepVisual Visual;
    public int WrongPresses;
}

/// <summary>Everything the renderer needs for one frame. Rebuilt by the owner every frame.</summary>
public sealed class TimelineFrame
{
    public readonly List<TimelineStep> Steps = [];

    /// <summary>Changes whenever a new attempt starts, so animations can snap instead of sliding.</summary>
    public int RunId;

    public string Title = string.Empty;
    public string JobLabel = string.Empty;

    public RunState State;
    public FinishReason FinishReason;
    public int CurrentIndex;
    public double Elapsed;
    public int Hits;
    public int Wrong;
    public int Skipped;

    /// <summary>0..1 while the player is casting, negative otherwise.</summary>
    public float CastProgress = -1f;

    /// <summary>The cast in progress is the expected step.</summary>
    public bool CastMatches;

    /// <summary>Icon of the last wrong press and how long ago it happened (negative: none).</summary>
    public uint WrongIconId;

    public float WrongAge = -1f;

    /// <summary>Seconds left on the practice countdown, negative when not counting down.</summary>
    public float Countdown = -1f;

    /// <summary>Overall fade, 0..1.</summary>
    public float Opacity = 1f;

    /// <summary>The overlay can be moved and resized right now.</summary>
    public bool Unlocked;

    /// <summary>Draw the minimize and hide buttons; set while the mouse can reach the overlay.</summary>
    public bool Controls;

    /// <summary>Which of those buttons the mouse is over: 0 minimize, 1 hide, negative for neither.</summary>
    public int HoveredControl = -1;

    /// <summary>Shown instead of the strip when there is nothing to practice.</summary>
    public string? Notice;
}

public enum TimelineLayoutMode
{
    /// <summary>One line, abilities raised and smaller between the global cooldowns they are woven into.</summary>
    Staggered,

    /// <summary>One flat row.</summary>
    Inline
}

public enum KeybindStyle
{
    /// <summary>Spelled out in the full view (letters where they do not fit), letters in the compact view.</summary>
    Auto,
    Full,
    Compact
}

public enum CooldownBadgeMode
{
    Off,

    /// <summary>One and two minute cooldowns only.</summary>
    Burst,

    /// <summary>Every cooldown of 30 seconds or more.</summary>
    All
}

public enum PadGlyphs
{
    Xbox,
    PlayStation
}

/// <summary>How loudly a wrong press is shown while the opener is still running.</summary>
public enum FeedbackStyle
{
    /// <summary>A colour pulse on the expected step and the wrong icon set aside; nothing moves or is covered.</summary>
    Gentle,

    /// <summary>Flash, shake and a crossed-out icon on the step itself.</summary>
    Strong,

    /// <summary>Nothing during the run; mistakes only show in the result.</summary>
    Off
}

/// <summary>Which cooldowns get a "GCD n" note above them.</summary>
public enum BurstCallouts
{
    Off,
    TwoMinute,
    OneAndTwoMinute
}

public struct TimelineStyle
{
    /// <summary>Dalamud's global UI scale.</summary>
    public float Scale;

    /// <summary>
    /// The small floating view: one flat row, no header, the step to press always in the same
    /// spot. The other layout switches do not apply to it.
    /// </summary>
    public bool Compact;

    /// <summary>Size of a global cooldown icon, before scaling.</summary>
    public float IconSize;

    /// <summary>Size of an ability icon relative to <see cref="IconSize"/>.</summary>
    public float OffGcdScale;

    public float Gap;
    public TimelineLayoutMode Layout;
    public bool ShowHeader;
    public bool ShowHints;
    public bool ShowLaneLabels;
    public bool ShowProgress;

    /// <summary>Compact view: a line of text above the strip with state, counter and timer.</summary>
    public bool ShowStatus;

    /// <summary>GCD numbers and estimated times under the global cooldowns.</summary>
    public bool ShowRuler;

    public BurstCallouts Callouts;
    public FeedbackStyle Feedback;
    public KeybindStyle HintStyle;
    public CooldownBadgeMode Badges;

    /// <summary>Opacity of the panel tint, 0..1.</summary>
    public float PanelOpacity;

    /// <summary>Backdrop blur strength, 0..1. Zero disables it.</summary>
    public float Blur;

    public Vector3 Accent;
    public PadGlyphs Pad;

    public static TimelineStyle Default => new()
    {
        Scale = 1f,
        IconSize = 48f,
        OffGcdScale = 0.76f,
        Gap = 6f,
        Layout = TimelineLayoutMode.Staggered,
        ShowHeader = true,
        ShowHints = true,
        ShowLaneLabels = true,
        ShowProgress = true,
        ShowStatus = true,
        ShowRuler = true,
        Callouts = BurstCallouts.TwoMinute,
        Feedback = FeedbackStyle.Gentle,
        HintStyle = KeybindStyle.Auto,
        Badges = CooldownBadgeMode.Burst,
        PanelOpacity = 0.58f,
        Blur = 0.45f,
        Accent = Palette.DefaultAccent,
        Pad = PadGlyphs.Xbox
    };

    /// <summary>The defaults of the compact view.</summary>
    public static TimelineStyle DefaultCompact => Default with
    {
        Compact = true,
        IconSize = 40f,
        Layout = TimelineLayoutMode.Inline,
        ShowHeader = false,
        ShowLaneLabels = false,
        ShowRuler = false,
        Callouts = BurstCallouts.Off
    };
}

public enum TimelineFont
{
    /// <summary>Key caps, badges, lane labels.</summary>
    Small,

    /// <summary>Header text.</summary>
    Body,

    /// <summary>Timer and counters.</summary>
    Number
}

/// <summary>What the renderer needs from its surroundings. Implemented by the plugin and by the preview tool.</summary>
public interface ITimelineHost
{
    ImTextureID GetIcon(uint iconId);

    /// <summary>Makes a font current until the returned handle is disposed. Null leaves the current font.</summary>
    IDisposable? PushFont(TimelineFont font);

    /// <summary>Blurs what is behind the given rectangle (acrylic). May do nothing.</summary>
    void DrawBackdrop(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float strength, float opacity);
}
