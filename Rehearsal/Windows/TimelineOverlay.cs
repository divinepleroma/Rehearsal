using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Rehearsal.Data;
using Rehearsal.Game;
using Rehearsal.Training;
using Rehearsal.UI;
using Rehearsal.UI.Timeline;
using Rehearsal.UI.Views;

namespace Rehearsal.Windows;

/// <summary>
/// The live timeline: a chromeless, translucent strip that sits on the HUD. Locked, it ignores the
/// mouse completely; unlocked, it can be dragged around and resized in width.
/// There is one instance for the full panel and one for the compact strip. While the timeline is
/// minimized neither draws; <see cref="MinimizedTab"/> stands in for them.
/// </summary>
internal sealed class TimelineOverlay : Window
{
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                                               | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav
                                               | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking;

    private const string MenuId = "##overlayMenu";

    private readonly TimelineFrameBuilder builder;
    private readonly bool compact;
    private readonly TimelineFrame frame = new();
    private readonly Func<int, bool, KeyHint> hintFor;
    private readonly GameHost host;
    private readonly Action<MainView.Section> open;
    private readonly TimelineRenderer renderer = new();
    private readonly Trainer trainer;
    private float opacity;
    private int pushedVars;
    private bool reachable;
    private TimelineStyle style;

    /// <param name="host">The application.</param>
    /// <param name="trainer">The live trainer.</param>
    /// <param name="compact">This instance is the compact strip rather than the full panel.</param>
    /// <param name="open">Opens a section of the main window.</param>
    public TimelineOverlay(GameHost host, Trainer trainer, bool compact, Action<MainView.Section> open)
        : base(compact ? Branding.Name + " (compact)###RehearsalCompact" : Branding.Name + "###RehearsalOverlay", BaseFlags)
    {
        this.host = host;
        this.trainer = trainer;
        this.compact = compact;
        this.open = open;
        builder = new TimelineFrameBuilder(host.Catalog);
        hintFor = trainer.HintFor;

        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
        AllowPinning = false;
        AllowClickthrough = false;
        AllowBackgroundBlur = false; // we blur exactly the panel ourselves
        ForceMainWindow = true;

        Size = compact ? new Vector2(400, 90) : new Vector2(860, 160);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Settings Settings => host.Settings;

    /// <summary>Whether this is the view that should be on screen.</summary>
    private bool IsMyView
    {
        get
        {
            // While placing things in automatic mode both are up, so each can be put where it belongs.
            if (!Settings.OverlayLocked && Settings.View == TimelineView.Automatic)
                return true;

            return (trainer.EffectiveView == TimelineView.Compact) == compact;
        }
    }

    /// <summary>Unlocked, the overlay stays up even with nothing to show so it can be positioned.</summary>
    private bool ShouldShow => Settings.Presence == TimelinePresence.Shown && IsMyView && (trainer.WantsOverlay || !Settings.OverlayLocked);

    public override void Update()
    {
        var target = ShouldShow ? 1f : 0f;
        opacity = Gfx.Approach(opacity, target, target > opacity ? 14f : 6f, ImGui.GetIO().DeltaTime);
    }

    public override bool DrawConditions()
    {
        return Settings.OverlayEnabled && (ShouldShow || opacity > 0.01f);
    }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        style = Settings.ToStyle(scale, compact);
        var height = renderer.MeasureHeight(style);

        // A locked timeline is click-through, except while Ctrl+Shift is held: then it takes the
        // mouse again so its menu can be opened without unlocking it.
        var io = ImGui.GetIO();
        reachable = Settings.OverlayLocked && Settings.MenuWhileLocked && io.KeyCtrl && io.KeyShift;

        Flags = BaseFlags;
        if (Settings.OverlayLocked)
        {
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            if (!reachable)
                Flags |= ImGuiWindowFlags.NoInputs;
        }

        // On its way out after being minimized: the tab has the mouse now.
        if (Settings.OverlayMinimized)
            Flags |= ImGuiWindowFlags.NoInputs;

        // Fixed height (it follows from the icon size), free width. Window constraints are given
        // unscaled; Dalamud multiplies them by the global scale.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(compact ? 160f : 320f, height / scale),
            MaximumSize = new Vector2(6000f, height / scale)
        };

        var viewport = ImGui.GetMainViewport();
        var anchor = compact ? new Vector2(0.5f, 0.8f) : new Vector2(0.5f, 0.68f);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * anchor, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(64f, 16f));
        pushedVars = 3;
    }

    public override void PostDraw()
    {
        if (pushedVars > 0)
            ImGui.PopStyleVar(pushedVars);
        pushedVars = 0;
    }

    public override void Draw()
    {
        var locked = Settings.OverlayLocked;
        var minimized = Settings.OverlayMinimized;

        // Whether the mouse can do anything with the overlay right now.
        var interactive = (!locked || reachable) && !minimized;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (!minimized)
            host.TimelinePosition = pos;

        frame.RunId = trainer.RunId;
        frame.Title = trainer.ActiveTitle;
        frame.JobLabel = trainer.ActiveJob == Jobs.ANY ? string.Empty : trainer.ActiveJob.ToString();
        frame.Opacity = opacity;
        frame.Unlocked = !locked;
        frame.Countdown = trainer.CountdownRemaining;
        frame.CastProgress = trainer.CastProgress;
        frame.CastMatches = trainer.CastMatches;
        frame.Notice = null;

        var actions = trainer.ActiveActions;
        if (actions.Count == 0)
        {
            frame.Steps.Clear();
            frame.State = RunState.Armed;
            frame.Notice = compact ? "No opener  -  " + Branding.Command : "No opener selected for this job  -  type " + Branding.Command + " to pick one";
        }
        else
        {
            builder.Build(frame, actions, trainer.Session, hintFor, Plugin.Now, trainer.Speed, Settings.CountPrecast);
        }

        // Minimize and hide, as two small buttons at the top right. The buttons themselves are
        // invisible ImGui items; the renderer draws them, so the preview tool shows them too.
        frame.Controls = interactive;
        frame.HoveredControl = -1;
        if (interactive)
        {
            var controls = TimelineLayout.Controls(style, TimelineLayout.Measure(style), pos, pos + size);
            var button = new Vector2(controls.Size);

            ImGui.SetCursorScreenPos(controls.Minimize);
            if (ImGui.InvisibleButton("##minimize", button))
                SetPresence(TimelinePresence.Minimized);
            if (ImGui.IsItemHovered())
                frame.HoveredControl = 0;

            ImGui.SetCursorScreenPos(controls.Hide);
            if (ImGui.InvisibleButton("##hide", button))
            {
                SetPresence(TimelinePresence.Hidden);
                Notify(OverlayMenu.HiddenNotice);
            }

            if (ImGui.IsItemHovered())
                frame.HoveredControl = 1;
        }

        renderer.Render(ImGui.GetWindowDrawList(), pos, size, frame, style, host, ImGui.GetIO().DeltaTime, ImGui.GetTime());

        if (interactive && ImGui.IsWindowHovered())
        {
            // The wheel scrubs through the opener when it does not fit.
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
                renderer.ScrollBy(-wheel * style.IconSize * style.Scale * 1.5f);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup(MenuId);
        }

        // Submitted every frame: once open, the menu has to outlive the keys that let the mouse in.
        using var theme = Theme.Push(Settings.Accent, 0.96f, style.Scale);
        switch (frame.HoveredControl)
        {
            case 0:
                Widgets.TooltipText("Minimize to a small tab. Click the tab to bring the timeline back.", style.Scale);
                break;
            case 1:
                Widgets.TooltipText("Hide the timeline. The " + Branding.Name + " window (" + Branding.Command + ") brings it back.", style.Scale);
                break;
        }

        if (ImGui.BeginPopup(MenuId))
        {
            OverlayMenu.Draw(host, compact ? OverlayMenu.Origin.CompactStrip : OverlayMenu.Origin.FullPanel, open, Notify);
            ImGui.EndPopup();
        }
    }

    private static void Notify(string message)
    {
        Plugin.Chat.Print(Branding.ChatTag + message);
    }

    private void SetPresence(TimelinePresence presence)
    {
        Settings.Presence = presence;
        host.SaveSettings();
    }
}
