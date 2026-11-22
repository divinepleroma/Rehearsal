using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI.Views;

/// <summary>
/// The timeline's right-click menu: everything you might want to change without opening the main
/// window - which opener, which view, what is shown.
/// </summary>
public static class OverlayMenu
{
    /// <summary>Draws the menu items. Call between BeginPopup and EndPopup.</summary>
    /// <param name="host">The application.</param>
    /// <param name="open">Opens a section of the main window.</param>
    /// <param name="notify">Tells the player something in chat.</param>
    public static void Draw(IAppHost host, Action<MainView.Section> open, Action<string> notify)
    {
        var settings = host.Settings;
        var trainer = host.Trainer;
        var changed = false;

        var title = trainer.ActiveActions.Count > 0 ? trainer.ActiveTitle : "No opener selected";
        ImGui.TextDisabled(trainer.ActiveJob == Jobs.ANY ? title : $"{trainer.ActiveJob}  {title}");
        ImGui.Separator();

        if (ImGui.BeginMenu("Opener"))
        {
            var job = host.CurrentJob;
            var any = false;
            foreach (var opener in host.Library.ForJob(job).OrderBy(o => !o.IsDefault))
            {
                any = true;
                var label = opener.IsDefault ? opener.Name : opener.Name + "  (saved)";
                if (ImGui.MenuItem(label, trainer.Active == opener))
                    trainer.Select(opener);
            }

            if (!any)
                ImGui.TextDisabled(job == Jobs.ANY ? "No openers for this job" : $"No openers for {job} yet");

            ImGui.Separator();
            if (any && ImGui.MenuItem("None", trainer.ActiveActions.Count == 0))
                trainer.Deselect(job);
            if (ImGui.MenuItem("Browse all openers..."))
                open(MainView.Section.Openers);
            if (ImGui.MenuItem("Build or edit an opener..."))
                open(MainView.Section.Editor);

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            changed |= Choice("Full panel", ref settings.View, TimelineView.Expanded);
            changed |= Choice("Compact strip", ref settings.View, TimelineView.Compact);
            changed |= Choice("Automatic (full before the pull, compact after)", ref settings.View, TimelineView.Automatic);
            ImGui.Separator();
            changed |= Choice("Two lanes (oGCDs raised)", ref settings.Layout, TimelineLayoutMode.Staggered);
            changed |= Choice("Flat row", ref settings.Layout, TimelineLayoutMode.Inline);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Show"))
        {
            changed |= ImGui.MenuItem("Key caps", ref settings.ShowKeybinds);
            changed |= ImGui.MenuItem("GCD numbers and time", ref settings.ShowRuler);
            changed |= ImGui.MenuItem("Header", ref settings.ShowHeader);
            changed |= ImGui.MenuItem("Lane labels", ref settings.ShowLaneLabels);
            changed |= ImGui.MenuItem("Progress bar", ref settings.ShowProgress);

            ImGui.Separator();
            if (ImGui.BeginMenu("Cooldown markers"))
            {
                changed |= Choice("Off", ref settings.CooldownBadges, CooldownBadgeMode.Off);
                changed |= Choice("60s and 120s cooldowns", ref settings.CooldownBadges, CooldownBadgeMode.Burst);
                changed |= Choice("Every cooldown of 30s or more", ref settings.CooldownBadges, CooldownBadgeMode.All);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Burst notes (GCD n)"))
            {
                changed |= Choice("Off", ref settings.BurstCallouts, BurstCallouts.Off);
                changed |= Choice("Two minute cooldowns", ref settings.BurstCallouts, BurstCallouts.TwoMinute);
                changed |= Choice("One and two minute cooldowns", ref settings.BurstCallouts, BurstCallouts.OneAndTwoMinute);
                ImGui.EndMenu();
            }

            ImGui.Separator();
            changed |= ImGui.MenuItem("Compact strip: status line", ref settings.CompactShowStatus);
            changed |= ImGui.MenuItem("Compact strip: key caps", ref settings.CompactShowKeybinds);
            changed |= ImGui.MenuItem("Compact strip: cooldown markers", ref settings.CompactShowBadges);
            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Reset run", false, trainer.ActiveActions.Count > 0))
            trainer.Reset();

        var counting = trainer.CountdownRemaining >= 0f;
        if (ImGui.MenuItem(counting ? "Stop countdown" : $"Start {settings.CountdownSeconds}s countdown", false, trainer.ActiveActions.Count > 0))
        {
            if (counting)
                trainer.StopCountdown();
            else
                trainer.StartCountdown();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Locked in place", settings.OverlayLocked))
        {
            settings.OverlayLocked = !settings.OverlayLocked;
            changed = true;
            if (settings.OverlayLocked)
            {
                notify(settings.MenuWhileLocked
                           ? "Locked. Hold Ctrl+Shift and right-click it to get this menu back, or type /opener unlock."
                           : "Locked. /opener unlock moves it again.");
            }
        }

        if (ImGui.MenuItem("Hide the timeline"))
        {
            settings.OverlayEnabled = false;
            changed = true;
            notify("Timeline hidden. /opener toggle brings it back.");
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Results..."))
            open(MainView.Section.Results);
        if (ImGui.MenuItem("Settings..."))
            open(MainView.Section.Settings);
        if (ImGui.MenuItem("How to use..."))
            open(MainView.Section.Guide);

        if (changed)
        {
            settings.Sanitize();
            host.SaveSettings();
        }
    }

    /// <summary>A menu entry that is ticked while the setting has this value.</summary>
    private static bool Choice<T>(string label, ref T current, T value) where T : struct, Enum
    {
        var selected = current.Equals(value);
        if (!ImGui.MenuItem(label, selected) || selected)
            return false;

        current = value;
        return true;
    }
}
