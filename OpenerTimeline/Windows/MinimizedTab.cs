using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using OpenerTimeline.Data;
using OpenerTimeline.Game;
using OpenerTimeline.Training;
using OpenerTimeline.UI;
using OpenerTimeline.UI.Timeline;
using OpenerTimeline.UI.Views;

namespace OpenerTimeline.Windows;

/// <summary>
/// What a minimized timeline leaves behind: a small pill with where the run stands. A click brings
/// the timeline back, a right-click opens its menu. It shows up exactly when the timeline would.
/// Unlike the timeline it takes clicks even while locked - being clicked is what it is for - but
/// it can only be dragged somewhere else while unlocked. It keeps its own position.
/// </summary>
internal sealed class MinimizedTab : Window
{
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                                               | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav
                                               | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoResize
                                               | ImGuiWindowFlags.AlwaysAutoResize;

    private const string MenuId = "##tabMenu";

    private readonly TimelineFrameBuilder builder;
    private readonly TimelineFrame frame = new();
    private readonly GameHost host;
    private readonly Action<MainView.Section> open;
    private readonly Trainer trainer;
    private float opacity;
    private Vector2? pressedAt;
    private int pushedVars;
    private TimelineStyle style;

    /// <param name="host">The application.</param>
    /// <param name="trainer">The live trainer.</param>
    /// <param name="open">Opens a section of the main window.</param>
    public MinimizedTab(GameHost host, Trainer trainer, Action<MainView.Section> open)
        : base("Opener Timeline (minimized)###OpenerTimelineTab", BaseFlags)
    {
        this.host = host;
        this.trainer = trainer;
        this.open = open;
        builder = new TimelineFrameBuilder(host.Catalog);

        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
        AllowPinning = false;
        AllowClickthrough = false;
        AllowBackgroundBlur = false; // the pill is blurred by its own drawing code
        ForceMainWindow = true;
    }

    private Settings Settings => host.Settings;

    /// <summary>The same rule as the timeline it stands in for.</summary>
    private bool ShouldShow => Settings.Presence == TimelinePresence.Minimized && (trainer.WantsOverlay || !Settings.OverlayLocked);

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
        style = Settings.ToStyle(ImGuiHelpers.GlobalScale, true);

        Flags = BaseFlags;
        if (Settings.OverlayLocked)
            Flags |= ImGuiWindowFlags.NoMove;

        // Fading out after the timeline came back: leave the mouse to the timeline.
        if (!Settings.OverlayMinimized)
            Flags |= ImGuiWindowFlags.NoInputs;

        // The very first time it takes the place of the timeline it was minimized from.
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(host.TimelinePosition ?? viewport.Pos + viewport.Size * new Vector2(0.5f, 0.68f), ImGuiCond.FirstUseEver);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(16f, 16f));
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
        var interactive = Settings.OverlayMinimized;

        frame.RunId = trainer.RunId;
        frame.Title = trainer.ActiveTitle;
        frame.JobLabel = trainer.ActiveJob == Jobs.ANY ? string.Empty : trainer.ActiveJob.ToString();
        frame.Opacity = opacity;
        frame.Unlocked = !Settings.OverlayLocked;
        frame.Countdown = trainer.CountdownRemaining;
        frame.Notice = null;

        var actions = trainer.ActiveActions;
        if (actions.Count == 0)
        {
            frame.Steps.Clear();
            frame.State = RunState.Armed;
        }
        else
        {
            builder.Build(frame, actions, trainer.Session, null, Plugin.Now, trainer.Speed, Settings.CountPrecast);
        }

        // The window sizes itself around this, so the pill is exactly as wide as what it says.
        var size = TimelineTab.Measure(frame, style, host);
        var pos = ImGui.GetWindowPos();
        ImGui.Dummy(size);

        var hovered = interactive && ImGui.IsWindowHovered();
        TimelineTab.Render(ImGui.GetWindowDrawList(), pos, size, frame, style, host, hovered, ImGui.GetTime());

        // A click, as opposed to the start of a drag: pressed and let go in the same place.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            pressedAt = ImGui.GetMousePos();
        if (pressedAt is { } pressed && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            pressedAt = null;
            if (hovered && Vector2.Distance(pressed, ImGui.GetMousePos()) < 4f * style.Scale)
            {
                Settings.Presence = TimelinePresence.Shown;
                host.SaveSettings();
            }
        }

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(MenuId);

        using var theme = Theme.Push(Settings.Accent, 0.96f, style.Scale);
        if (hovered && !ImGui.IsPopupOpen(MenuId))
        {
            Widgets.TooltipText(Settings.OverlayLocked
                                    ? "Opener Timeline, minimized. Click to bring it back, right-click for options."
                                    : "Opener Timeline, minimized. Click to bring it back, drag to move, right-click for options.", style.Scale);
        }

        if (ImGui.BeginPopup(MenuId))
        {
            OverlayMenu.Draw(host, OverlayMenu.Origin.Tab, open, message => Plugin.Chat.Print("[Opener Timeline] " + message));
            ImGui.EndPopup();
        }
    }
}
