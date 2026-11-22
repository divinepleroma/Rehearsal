using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;

namespace OpenerTimeline.UI.Views;

/// <summary>Content of the main window: navigation plus its sections.</summary>
public sealed class MainView
{
    public enum Section
    {
        Openers,
        Editor,
        Results,
        Settings,
        Guide
    }

    private static readonly string[] PresenceLabels = ["Shown", "Minimized", "Hidden"];

    private readonly EditorView editor;
    private readonly GuideView guide;
    private readonly IAppHost host;
    private readonly LibraryView library;
    private readonly ResultsView results;
    private readonly SettingsView settings;
    private int section;

    public MainView(IAppHost host)
    {
        this.host = host;
        editor = new EditorView(host, () => library!.Invalidate());
        library = new LibraryView(host, Edit);
        results = new ResultsView(host);
        settings = new SettingsView(host);
        guide = new GuideView(host, Show, page => ShowSettings(page));
    }

    public void Show(Section target)
    {
        section = (int)target;
    }

    /// <summary>Opens the settings on a particular page, optionally with its folded part open.</summary>
    public void ShowSettings(SettingsView.Page page, bool unfolded = false)
    {
        settings.Show(page, unfolded);
        section = (int)Section.Settings;
    }

    /// <summary>Opens the walkthrough on a particular page (zero-based).</summary>
    public void ShowGuide(int page)
    {
        guide.Show(page);
        section = (int)Section.Guide;
    }

    /// <summary>Opens a library opener in the editor.</summary>
    public void Edit(OpenerRef opener)
    {
        editor.Load(opener);
        section = (int)Section.Editor;
    }

    public void Draw()
    {
        var scale = host.Scale;
        var accent = host.Settings.Accent;

        var rowStart = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        Widgets.SegmentedTabs("##sections", ["Openers", "Editor", "Results", "Settings", "Guide"], ref section, accent, scale);
        var tabsWidth = ImGui.GetItemRectSize().X;
        var below = ImGui.GetCursorScreenPos();

        var right = rowStart.X + rowWidth;
        right -= DrawPresence(rowStart, right, rowWidth - tabsWidth, scale, accent);
        DrawActiveSummary(rowStart, right, tabsWidth, scale, accent);

        ImGui.SetCursorScreenPos(below);
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        switch ((Section)section)
        {
            case Section.Openers:
                library.Draw();
                break;
            case Section.Editor:
                editor.Draw();
                break;
            case Section.Results:
                results.Draw();
                break;
            case Section.Settings:
                settings.Draw();
                break;
            default:
                guide.Draw();
                break;
        }
    }

    /// <summary>
    /// Shown, minimized or hidden: the one switch that has to be reachable from every tab, since
    /// a hidden timeline cannot offer it itself. Returns the width it took at the right of the row.
    /// </summary>
    private float DrawPresence(Vector2 rowStart, float right, float room, float scale, Vector3 accent)
    {
        var settings = host.Settings;
        var width = Widgets.SegmentedWidth(PresenceLabels, scale, true);
        var gap = MathF.Round(14f * scale);
        if (room < width + gap)
            return 0f;

        var rowHeight = Widgets.SegmentedHeight(scale);
        var height = Widgets.SegmentedHeight(scale, true);
        var top = rowStart.Y + MathF.Round((rowHeight - height) * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(right - width), top));
        var presence = (int)settings.Presence;
        if (Widgets.SegmentedTabs("##presence", PresenceLabels, ref presence, accent, scale, true))
        {
            settings.Presence = (TimelinePresence)presence;
            host.SaveSettings();
        }

        Widgets.Tooltip("The timeline on your HUD.\n\n"
                        + "Minimized tucks it away as a small tab that brings it back when clicked; your opener is still "
                        + "followed.\n\nHidden switches it off altogether.", scale);

        // Say what the three words are about, if there is still room for that.
        const string label = "Timeline";
        var labelSize = ImGui.CalcTextSize(label);
        var labelRoom = labelSize.X + MathF.Round(8f * scale);
        if (room < width + gap + labelRoom)
            return width + gap;

        ImGui.GetWindowDrawList().AddText(
            new Vector2(MathF.Round(right - width - labelRoom), MathF.Round(rowStart.Y + (rowHeight - labelSize.Y) * 0.5f)),
            Palette.Pack(Palette.White, Palette.TextFaintAlpha), label);
        return width + gap + labelRoom;
    }

    /// <summary>What is being practised right now, right-aligned next to the navigation.</summary>
    private void DrawActiveSummary(Vector2 rowStart, float right, float tabsWidth, float scale, Vector3 accent)
    {
        var trainer = host.Trainer;
        var dl = ImGui.GetWindowDrawList();
        var centerY = rowStart.Y + 16f * scale;
        var room = right - rowStart.X - tabsWidth - 24f * scale;

        if (trainer.ActiveActions.Count == 0)
        {
            const string none = "No opener selected for this job";
            var size = ImGui.CalcTextSize(none);
            if (room > size.X)
                dl.AddText(new Vector2(right - size.X, centerY - size.Y * 0.5f), Palette.Pack(Palette.White, Palette.TextFaintAlpha), none);
            return;
        }

        var title = trainer.ActiveTitle;
        var titleSize = ImGui.CalcTextSize(title);
        var job = trainer.ActiveJob == Jobs.ANY ? string.Empty : trainer.ActiveJob.ToString();
        var jobSize = ImGui.CalcTextSize(job);
        var pad = 8f * scale;
        var tagWidth = job.Length > 0 ? jobSize.X + pad * 2f : 0f;
        var total = tagWidth + pad + titleSize.X;
        if (room < total)
            return;

        var x = right - total;
        if (job.Length > 0)
        {
            var tagMin = new Vector2(x, centerY - jobSize.Y * 0.5f - 2f * scale);
            var tagMax = tagMin + new Vector2(tagWidth, jobSize.Y + 4f * scale);
            dl.AddRectFilled(tagMin, tagMax, Palette.Pack(accent, 0.18f), (tagMax.Y - tagMin.Y) * 0.5f);
            dl.AddText(new Vector2(x + pad, centerY - jobSize.Y * 0.5f), Palette.Pack(Palette.Mix(accent, Palette.White, 0.25f)), job);
        }

        dl.AddText(new Vector2(x + tagWidth + pad, centerY - titleSize.Y * 0.5f), Palette.Pack(Palette.White, Palette.TextDimAlpha), title);
    }
}
