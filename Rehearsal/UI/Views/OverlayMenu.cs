using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI.Views;

/// <summary>
/// The timeline's right-click menu: what you want within reach while playing - which opener,
/// which view, minimize, hide, lock. It only offers the switches of the view it was opened on;
/// everything else is one click away in the settings.
/// </summary>
public static class OverlayMenu
{
    /// <summary>What was right-clicked.</summary>
    public enum Origin
    {
        FullPanel,
        CompactStrip,

        /// <summary>The small tab the minimized timeline leaves behind.</summary>
        Tab
    }

    /// <summary>Draws the menu items. Call between BeginPopup and EndPopup.</summary>
    /// <param name="host">The application.</param>
    /// <param name="origin">What the menu was opened on.</param>
    /// <param name="open">Opens a section of the main window.</param>
    /// <param name="notify">Tells the player something in chat.</param>
    public static void Draw(IAppHost host, Origin origin, Action<MainView.Section> open, Action<string> notify)
    {
        var settings = host.Settings;
        var trainer = host.Trainer;
        var changed = false;
        var hasOpener = trainer.ActiveActions.Count > 0;

        var title = hasOpener ? trainer.ActiveTitle : "No opener selected";
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
            if (any && ImGui.MenuItem("None", !hasOpener))
                trainer.Deselect(job);
            if (ImGui.MenuItem("Browse or edit openers..."))
                open(MainView.Section.Openers);

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Display"))
        {
            changed |= Choice("Compact strip", ref settings.View, TimelineView.Compact);
            changed |= Choice("Full panel", ref settings.View, TimelineView.Expanded);
            changed |= Choice("Automatic (full before the pull, compact after)", ref settings.View, TimelineView.Automatic);

            switch (origin)
            {
                case Origin.FullPanel:
                    ImGui.Separator();
                    changed |= Choice("Two lanes (oGCDs raised)", ref settings.Layout, TimelineLayoutMode.Staggered);
                    changed |= Choice("Flat row", ref settings.Layout, TimelineLayoutMode.Inline);
                    ImGui.Separator();
                    changed |= ImGui.MenuItem("Header", ref settings.ShowHeader);
                    changed |= ImGui.MenuItem("Key caps", ref settings.ShowKeybinds);
                    changed |= ImGui.MenuItem("GCD numbers and time", ref settings.ShowRuler);
                    changed |= ImGui.MenuItem("Lane labels", ref settings.ShowLaneLabels);
                    changed |= ImGui.MenuItem("Progress bar", ref settings.ShowProgress);
                    break;
                case Origin.CompactStrip:
                    ImGui.Separator();
                    changed |= ImGui.MenuItem("Status line", ref settings.CompactShowStatus);
                    changed |= ImGui.MenuItem("Key caps", ref settings.CompactShowKeybinds);
                    changed |= ImGui.MenuItem("Cooldown markers", ref settings.CompactShowBadges);
                    changed |= ImGui.MenuItem("Progress bar", ref settings.ShowProgress);
                    break;
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        var counting = trainer.CountdownRemaining >= 0f;
        if (ImGui.MenuItem(counting ? "Stop countdown" : $"Start {settings.CountdownSeconds}s countdown", false, hasOpener))
        {
            if (counting)
                trainer.StopCountdown();
            else
                trainer.StartCountdown();
        }

        if (ImGui.MenuItem("Reset run", false, hasOpener))
            trainer.Reset();

        ImGui.Separator();

        if (origin == Origin.Tab)
        {
            if (ImGui.MenuItem("Show the timeline"))
            {
                settings.Presence = TimelinePresence.Shown;
                changed = true;
            }
        }
        else if (ImGui.MenuItem("Minimize"))
        {
            settings.Presence = TimelinePresence.Minimized;
            changed = true;
        }

        if (ImGui.MenuItem("Hide"))
        {
            settings.Presence = TimelinePresence.Hidden;
            changed = true;
            notify(HiddenNotice);
        }

        if (ImGui.MenuItem("Locked in place", settings.OverlayLocked))
        {
            settings.OverlayLocked = !settings.OverlayLocked;
            changed = true;
            if (settings.OverlayLocked)
            {
                notify(settings.MenuWhileLocked
                           ? "Locked. Hold Ctrl+Shift and right-click it to get this menu back, or type " + Branding.Command + " unlock."
                           : "Locked. " + Branding.Command + " unlock moves it again.");
            }
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Results..."))
            open(MainView.Section.Results);
        if (ImGui.MenuItem("Settings..."))
            open(MainView.Section.Settings);

        if (changed)
        {
            settings.Sanitize();
            host.SaveSettings();
        }
    }

    /// <summary>Said in chat whenever the timeline is hidden from the HUD itself, so it is clear how to get it back.</summary>
    public const string HiddenNotice = "Timeline hidden. Bring it back from the " + Branding.Name + " window (" + Branding.Command + "), or with " + Branding.Command + " show.";

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
