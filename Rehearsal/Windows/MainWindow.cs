using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Rehearsal.Game;
using Rehearsal.UI;
using Rehearsal.UI.Views;

namespace Rehearsal.Windows;

/// <summary>The opener library, editor, results and settings in one themed window.</summary>
internal sealed class MainWindow : Window
{
    private readonly GameHost host;
    private readonly MainView view;
    private Theme.Scope? theme;

    public MainWindow(GameHost host)
        : base(Branding.Name + "###RehearsalMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.host = host;
        view = new MainView(host);

        Size = new Vector2(940, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            // Wide enough for the navigation and the shown / minimized / hidden switch beside it.
            MinimumSize = new Vector2(700, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        // The window draws its own acrylic backdrop to match the timeline.
        AllowBackgroundBlur = false;
    }

    public void Show(MainView.Section section)
    {
        view.Show(section);
        IsOpen = true;
        BringToFront();
    }

    /// <summary>Opens the window on the first-run setup.</summary>
    /// <param name="restart">Begin at the first step even if the setup is already under way.</param>
    public void ShowSetup(bool restart = true)
    {
        if (restart || !view.InSetup)
            view.ShowSetup();
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        theme = Theme.Push(host.Settings.Accent, host.Settings.WindowOpacity, ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw()
    {
        theme?.Dispose();
        theme = null;
    }

    public override void Draw()
    {
        var pos = ImGui.GetWindowPos();
        host.DrawBackdrop(ImGui.GetWindowDrawList(), pos, pos + ImGui.GetWindowSize(), ImGui.GetStyle().WindowRounding, host.Settings.Blur, 1f);
        view.Draw();
    }
}
