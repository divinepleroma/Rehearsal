using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace OpenerTimeline.UI;

/// <summary>
/// Scoped ImGui styling for the plugin's own windows: dark, translucent, rounded, one accent.
/// Applied only while our windows draw, so the rest of Dalamud keeps the player's theme.
/// </summary>
public static class Theme
{
    public static Scope Push(Vector3 accent, float windowOpacity, float scale)
    {
        var colors = 0;
        var vars = 0;

        void Color(ImGuiCol target, Vector3 rgb, float alpha)
        {
            ImGui.PushStyleColor(target, new Vector4(rgb, alpha));
            colors++;
        }

        void Var(ImGuiStyleVar target, float value)
        {
            ImGui.PushStyleVar(target, value);
            vars++;
        }

        void Var2(ImGuiStyleVar target, float x, float y)
        {
            ImGui.PushStyleVar(target, new Vector2(x, y));
            vars++;
        }

        var white = Palette.White;
        var surface = Palette.Surface;
        var raised = Palette.Mix(surface, white, 0.06f);

        Color(ImGuiCol.Text, white, Palette.TextAlpha);
        Color(ImGuiCol.TextDisabled, white, Palette.TextFaintAlpha);
        Color(ImGuiCol.WindowBg, surface, windowOpacity);
        Color(ImGuiCol.ChildBg, surface, 0f);
        Color(ImGuiCol.PopupBg, raised, 0.98f);
        Color(ImGuiCol.Border, white, 0.10f);
        Color(ImGuiCol.BorderShadow, Palette.Black, 0f);
        Color(ImGuiCol.FrameBg, white, 0.06f);
        Color(ImGuiCol.FrameBgHovered, white, 0.10f);
        Color(ImGuiCol.FrameBgActive, white, 0.14f);
        Color(ImGuiCol.TitleBg, surface, windowOpacity);
        Color(ImGuiCol.TitleBgActive, raised, MathF.Min(1f, windowOpacity + 0.04f));
        Color(ImGuiCol.TitleBgCollapsed, surface, windowOpacity * 0.85f);
        Color(ImGuiCol.MenuBarBg, surface, 0f);
        Color(ImGuiCol.ScrollbarBg, surface, 0f);
        Color(ImGuiCol.ScrollbarGrab, white, 0.14f);
        Color(ImGuiCol.ScrollbarGrabHovered, white, 0.22f);
        Color(ImGuiCol.ScrollbarGrabActive, white, 0.30f);
        Color(ImGuiCol.CheckMark, accent, 1f);
        Color(ImGuiCol.SliderGrab, accent, 0.9f);
        Color(ImGuiCol.SliderGrabActive, Palette.Mix(accent, white, 0.25f), 1f);
        Color(ImGuiCol.Button, white, 0.07f);
        Color(ImGuiCol.ButtonHovered, white, 0.13f);
        Color(ImGuiCol.ButtonActive, white, 0.19f);
        Color(ImGuiCol.Header, accent, 0.20f);
        Color(ImGuiCol.HeaderHovered, accent, 0.28f);
        Color(ImGuiCol.HeaderActive, accent, 0.36f);
        Color(ImGuiCol.Separator, white, 0.08f);
        Color(ImGuiCol.SeparatorHovered, accent, 0.6f);
        Color(ImGuiCol.SeparatorActive, accent, 0.9f);
        Color(ImGuiCol.ResizeGrip, white, 0.08f);
        Color(ImGuiCol.ResizeGripHovered, accent, 0.6f);
        Color(ImGuiCol.ResizeGripActive, accent, 0.9f);
        Color(ImGuiCol.Tab, white, 0.04f);
        Color(ImGuiCol.TabHovered, accent, 0.30f);
        Color(ImGuiCol.TabActive, accent, 0.24f);
        Color(ImGuiCol.TabUnfocused, white, 0.03f);
        Color(ImGuiCol.TabUnfocusedActive, accent, 0.16f);
        Color(ImGuiCol.TextSelectedBg, accent, 0.35f);
        Color(ImGuiCol.DragDropTarget, accent, 0.9f);
        Color(ImGuiCol.NavHighlight, accent, 0.8f);
        Color(ImGuiCol.ModalWindowDimBg, Palette.Black, 0.45f);

        Var(ImGuiStyleVar.WindowRounding, 10f * scale);
        Var(ImGuiStyleVar.ChildRounding, 8f * scale);
        Var(ImGuiStyleVar.FrameRounding, 6f * scale);
        Var(ImGuiStyleVar.PopupRounding, 8f * scale);
        Var(ImGuiStyleVar.ScrollbarRounding, 8f * scale);
        Var(ImGuiStyleVar.GrabRounding, 6f * scale);
        Var(ImGuiStyleVar.TabRounding, 6f * scale);
        Var(ImGuiStyleVar.WindowBorderSize, 1f);
        Var(ImGuiStyleVar.ChildBorderSize, 0f);
        Var(ImGuiStyleVar.FrameBorderSize, 0f);
        Var(ImGuiStyleVar.PopupBorderSize, 1f);
        Var(ImGuiStyleVar.ScrollbarSize, 10f * scale);
        Var(ImGuiStyleVar.GrabMinSize, 10f * scale);
        Var2(ImGuiStyleVar.WindowPadding, 14f * scale, 12f * scale);
        Var2(ImGuiStyleVar.FramePadding, 10f * scale, 6f * scale);
        Var2(ImGuiStyleVar.ItemSpacing, 8f * scale, 8f * scale);
        Var2(ImGuiStyleVar.ItemInnerSpacing, 6f * scale, 6f * scale);
        Var2(ImGuiStyleVar.WindowTitleAlign, 0f, 0.5f);

        return new Scope(colors, vars);
    }

    public readonly struct Scope(int colors, int vars) : IDisposable
    {
        public void Dispose()
        {
            if (vars > 0)
                ImGui.PopStyleVar(vars);
            if (colors > 0)
                ImGui.PopStyleColor(colors);
        }
    }
}
