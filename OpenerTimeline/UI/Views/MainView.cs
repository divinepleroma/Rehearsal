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
        settings = new SettingsView(host, () => Show(Section.Guide));
        guide = new GuideView(host, Show, ShowSettings);
    }

    public void Show(Section target)
    {
        section = (int)target;
    }

    /// <summary>Opens the settings on a particular page.</summary>
    public void ShowSettings(SettingsView.Page page)
    {
        settings.Show(page);
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
        Widgets.SegmentedTabs("##sections", ["Openers", "Editor", "Results", "Settings", "How to use"], ref section, accent, scale);
        var tabsWidth = ImGui.GetItemRectSize().X;
        DrawActiveSummary(rowStart, rowWidth, tabsWidth, scale, accent);

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

    /// <summary>What is being practised right now, right-aligned next to the navigation.</summary>
    private void DrawActiveSummary(Vector2 rowStart, float rowWidth, float tabsWidth, float scale, Vector3 accent)
    {
        var trainer = host.Trainer;
        var dl = ImGui.GetWindowDrawList();
        var centerY = rowStart.Y + 16f * scale;
        var right = rowStart.X + rowWidth;
        var room = rowWidth - tabsWidth - 24f * scale;

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
