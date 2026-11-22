using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenerTimeline.UI;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline;

public enum HintSource
{
    /// <summary>Follow the game's own gamepad mode setting.</summary>
    Auto,
    Keyboard,
    Gamepad
}

/// <summary>Which form of the timeline is on screen.</summary>
public enum TimelineView
{
    /// <summary>The full panel: both lanes, header, key caps spelled out, ruler.</summary>
    Expanded,

    /// <summary>A small floating strip showing just what is coming up.</summary>
    Compact,

    /// <summary>The full panel while you wait for the pull, the small strip once the opener is under way.</summary>
    Automatic
}

/// <summary>Whether the timeline is on the HUD at all.</summary>
public enum TimelinePresence
{
    Shown,

    /// <summary>Tucked away as a small tab that brings it back when clicked. Openers are still followed.</summary>
    Minimized,

    /// <summary>Off: nothing is drawn and nothing is followed.</summary>
    Hidden
}

public sealed class SavedSelection
{
    public bool IsDefault { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Everything the player can configure. Plain data: no Dalamud types, so tools can use it too.</summary>
public sealed class Settings
{
    public const int CurrentVersion = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public int Version = CurrentVersion;

    // ---- Timeline ----
    public bool OverlayEnabled = true;

    /// <summary>Shown as a small tab instead of the timeline. Only means something while it is enabled.</summary>
    public bool OverlayMinimized;

    /// <summary>Starts unlocked so it can be dragged into place first.</summary>
    public bool OverlayLocked;

    /// <summary>Holding Ctrl+Shift lets the mouse reach a locked timeline (for its right-click menu).</summary>
    public bool MenuWhileLocked = true;

    public TimelineView View = TimelineView.Expanded;

    /// <summary>How long the result of a run stays on screen.</summary>
    public float ResultSeconds = 12f;

    // ---- When it shows up before the pull ----
    public bool ShowAtDummy = true;
    public bool ShowOnEnemyTarget = true;

    /// <summary>Inside a trial or raid it is there from the moment you zone in.</summary>
    public bool ShowInBossDuties = true;

    public bool ShowAlways;

    // ---- Where openers are tracked at all ----
    public bool TrackOpenWorld = true;
    public bool TrackDungeons;
    public bool TrackTrials = true;
    public bool TrackRaids = true;
    public bool TrackUltimates = true;
    public bool TrackAllianceRaids;
    public bool TrackOtherDuties;

    // ---- Full view ----
    public TimelineLayoutMode Layout = TimelineLayoutMode.Staggered;
    public float IconSize = 48f;
    public float OffGcdScale = 0.76f;
    public float IconGap = 6f;
    public bool ShowHeader = true;
    public bool ShowProgress = true;
    public bool ShowLaneLabels = true;
    public CooldownBadgeMode CooldownBadges = CooldownBadgeMode.Burst;

    /// <summary>GCD numbers and estimated times under the global cooldowns.</summary>
    public bool ShowRuler = true;

    /// <summary>A cast started before the pull counts as GCD 1.</summary>
    public bool CountPrecast = true;

    public BurstCallouts BurstCallouts = BurstCallouts.TwoMinute;

    // ---- Compact view ----
    public float CompactIconSize = 40f;
    public bool CompactShowStatus = true;

    /// <summary>Key caps in the compact strip; <see cref="ShowKeybinds"/> is the same switch for the full panel.</summary>
    public bool CompactShowKeybinds = true;

    public bool CompactShowBadges = true;

    // ---- Look ----
    public Vector3 Accent = Palette.DefaultAccent;
    public float PanelOpacity = 0.58f;
    public float Blur = 0.45f;
    public float WindowOpacity = 0.78f;

    // ---- Keybind hints ----
    /// <summary>Key caps in the full panel.</summary>
    public bool ShowKeybinds = true;
    public KeybindStyle KeybindStyle = KeybindStyle.Auto;
    public HintSource HintSource = HintSource.Auto;
    public PadGlyphs PadGlyphs = PadGlyphs.Xbox;

    // ---- Practice ----
    public bool StopAtFirstMistake;

    /// <summary>Sprint and role actions that are not part of the opener are not counted as mistakes.</summary>
    public bool IgnoreUtilityActions = true;

    /// <summary>Make the game highlight the next action on the hotbar.</summary>
    public bool AbilityAnts;

    public FeedbackStyle MistakeFeedback = FeedbackStyle.Gentle;

    /// <summary>The game sound played on a wrong press: 0 for none, otherwise the N of &lt;se.N&gt;.</summary>
    public int MistakeSound;

    /// <summary>Fit the opener to your level when you are below what it needs (level sync, old content).</summary>
    public bool AutoAdjustLevel = true;

    /// <summary>A run with no input for this long is considered abandoned.</summary>
    public float IdleTimeoutSeconds = 15f;

    public int CountdownSeconds = 7;
    public bool BigCountdown = true;

    // ---- State ----
    /// <summary>
    /// The opener chosen for each job, keyed by job abbreviation; a choice made for an older
    /// level cap is keyed "JOB@90".
    /// </summary>
    public Dictionary<string, SavedSelection> Selected = new();

    /// <summary>
    /// Actions observed to replace another on the hotbar (transformed id to the id that is actually
    /// slotted), so keybind hints also work for buttons that only change during combat.
    /// </summary>
    public Dictionary<uint, uint> LearnedBases = new();

    public bool ImportedLegacyOpeners;

    /// <summary>The first-run chat note of the very first version has been shown.</summary>
    public bool Welcomed;

    /// <summary>The walkthrough has been opened once on its own.</summary>
    public bool GuideShown;

    /// <summary>
    /// The two on/off switches as the one choice the player makes. Hiding keeps the minimized
    /// flag, so showing the timeline again brings it back the way it was.
    /// </summary>
    [JsonIgnore]
    public TimelinePresence Presence
    {
        get => !OverlayEnabled ? TimelinePresence.Hidden : OverlayMinimized ? TimelinePresence.Minimized : TimelinePresence.Shown;
        set
        {
            OverlayEnabled = value != TimelinePresence.Hidden;
            if (value != TimelinePresence.Hidden)
                OverlayMinimized = value == TimelinePresence.Minimized;
        }
    }

    /// <summary>The key under which the choice for a job at a level cap is stored.</summary>
    public static string SelectionKey(string job, int levelCap, int maxLevel)
    {
        return levelCap >= maxLevel ? job : $"{job}@{levelCap}";
    }

    /// <summary>The renderer settings for one of the two views.</summary>
    public TimelineStyle ToStyle(float scale, bool compact = false)
    {
        var style = new TimelineStyle
        {
            Scale = scale,
            Compact = compact,
            OffGcdScale = OffGcdScale,
            Gap = IconGap,
            ShowProgress = ShowProgress,
            Feedback = MistakeFeedback,
            HintStyle = KeybindStyle,
            PanelOpacity = PanelOpacity,
            Blur = Blur,
            Accent = Accent,
            Pad = PadGlyphs
        };

        if (compact)
        {
            style.IconSize = CompactIconSize;
            style.Layout = TimelineLayoutMode.Inline;
            style.ShowStatus = CompactShowStatus;
            style.ShowHints = CompactShowKeybinds;
            style.Badges = CompactShowBadges ? CooldownBadges : CooldownBadgeMode.Off;
            style.Callouts = BurstCallouts.Off;
        }
        else
        {
            style.IconSize = IconSize;
            style.Layout = Layout;
            style.ShowHeader = ShowHeader;
            style.ShowHints = ShowKeybinds;
            style.ShowLaneLabels = ShowLaneLabels;
            style.ShowRuler = ShowRuler;
            style.Badges = CooldownBadges;
            style.Callouts = BurstCallouts;
        }

        return style;
    }

    /// <summary>Keeps hand-edited or outdated files from producing a broken overlay.</summary>
    public void Sanitize()
    {
        IconSize = Math.Clamp(IconSize, 24f, 96f);
        CompactIconSize = Math.Clamp(CompactIconSize, 20f, 72f);
        OffGcdScale = Math.Clamp(OffGcdScale, 0.5f, 1f);
        IconGap = Math.Clamp(IconGap, 0f, 24f);
        PanelOpacity = Math.Clamp(PanelOpacity, 0f, 1f);
        Blur = Math.Clamp(Blur, 0f, 1f);
        WindowOpacity = Math.Clamp(WindowOpacity, 0.3f, 1f);
        ResultSeconds = Math.Clamp(ResultSeconds, 2f, 120f);
        IdleTimeoutSeconds = Math.Clamp(IdleTimeoutSeconds, 5f, 120f);
        CountdownSeconds = Math.Clamp(CountdownSeconds, 3, 30);
        MistakeSound = Math.Clamp(MistakeSound, 0, 16);
        Accent = Vector3.Clamp(Accent, Vector3.Zero, Vector3.One);
        Selected ??= new Dictionary<string, SavedSelection>();
        LearnedBases ??= new Dictionary<uint, uint>();

        // "BLM": null, or a selection without a name, would trip up everything that reads it.
        List<string>? broken = null;
        foreach (var (key, selection) in Selected)
        {
            if (selection?.Name == null)
                (broken ??= []).Add(key);
        }

        if (broken != null)
        {
            foreach (var key in broken)
                Selected.Remove(key);
        }
    }

    public static Settings Load(string path, Action<string, Exception>? logError = null)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
                if (loaded.Version < CurrentVersion)
                    Migrate(loaded, json);
                loaded.Sanitize();
                return loaded;
            }
        }
        catch (Exception ex)
        {
            logError?.Invoke($"Could not read settings from {path}; using defaults", ex);
        }

        return new Settings();
    }

    public void Save(string path, Action<string, Exception>? logError = null)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            logError?.Invoke($"Could not write settings to {path}", ex);
        }
    }

    /// <summary>Carries choices made with an older layout of this file over to the current one.</summary>
    private static void Migrate(Settings settings, string json)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(json, options);
        var root = document.RootElement;

        if (settings.Version < 2)
        {
            // Version 1 had a single "when to show" choice (0 always, 1 enemy targeted, 2 never) and
            // one switch for all duties.
            if (root.TryGetProperty("ArmedVisibility", out var visibility) && visibility.TryGetInt32(out var mode))
            {
                settings.ShowAlways = mode == 0;
                if (mode == 2)
                {
                    settings.ShowAtDummy = false;
                    settings.ShowOnEnemyTarget = false;
                    settings.ShowInBossDuties = false;
                }
            }

            if (root.TryGetProperty("HideInDuties", out var hide) && hide.ValueKind == JsonValueKind.False)
            {
                settings.TrackDungeons = true;
                settings.TrackAllianceRaids = true;
                settings.TrackOtherDuties = true;
            }
        }

        if (settings.Version < 3)
        {
            // Until now "spelled out" was how you got full modifier names in the wide panel.
            // Automatic does exactly that today, and switches to letters in the compact strip.
            if (settings.KeybindStyle == KeybindStyle.Full)
                settings.KeybindStyle = KeybindStyle.Auto;
        }

        if (settings.Version < 4)
        {
            // Key caps used to have a master switch with the compact strip's own switch under it.
            // Each view now has just its own, so "off everywhere" has to be spelled out for both.
            if (!settings.ShowKeybinds)
                settings.CompactShowKeybinds = false;
        }

        settings.Version = CurrentVersion;
    }
}
