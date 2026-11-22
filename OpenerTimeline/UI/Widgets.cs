using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI;

/// <summary>The handful of custom controls the windows are built from. ImGui only.</summary>
public static class Widgets
{
    public static Vector4 Dim => new(1f, 1f, 1f, Palette.TextDimAlpha);
    public static Vector4 Faint => new(1f, 1f, 1f, Palette.TextFaintAlpha);

    public static void TextDim(string text)
    {
        ImGui.TextColored(Dim, text);
    }

    public static void TextFaint(string text)
    {
        ImGui.TextColored(Faint, text);
    }

    /// <summary>Wrapped secondary text.</summary>
    public static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void SectionTitle(string text, Vector3 accent, float scale)
    {
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.TextColored(new Vector4(Palette.Mix(accent, Palette.White, 0.3f), 1f), text);
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(min, min + new Vector2(width, 0f), Palette.Pack(Palette.White, 0.08f), 1f);
        ImGui.Dummy(new Vector2(0f, 3f * scale));
    }

    public static void Tooltip(string text, float scale)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(340f * scale);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>Name, type and cooldown of a step; shown when hovering its icon.</summary>
    public static void StepTooltip(in TimelineStep step, float scale)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextUnformatted(step.Name);

        var kind = step.IsGcd ? "GCD" : "oGCD";
        if (step.Recast >= 5f)
            kind += $"  -  {step.Recast:0.#}s recast";
        ImGui.TextColored(Dim, kind);

        switch (step.Cooldown)
        {
            case CooldownClass.OneMinute:
                ImGui.TextColored(new Vector4(Palette.OneMinute, 1f), "One minute cooldown");
                break;
            case CooldownClass.TwoMinute:
                ImGui.TextColored(new Vector4(Palette.TwoMinute, 1f), "Two minute cooldown");
                break;
        }

        ImGui.EndTooltip();
    }

    /// <summary>Filled accent button for the main action of a view.</summary>
    public static bool PrimaryButton(string label, Vector3 accent, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(accent, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Palette.Mix(accent, Palette.White, 0.18f), 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(Palette.Mix(accent, Palette.Black, 0.15f), 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(Palette.OnAccent, 1f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    /// <summary>Button for destructive actions.</summary>
    public static bool DangerButton(string label, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(Palette.Bad, 0.14f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Palette.Bad, 0.26f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(Palette.Bad, 0.38f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(Palette.Mix(Palette.Bad, Palette.White, 0.25f), 1f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    /// <summary>A pill that can be switched on and off, for filters.</summary>
    public static bool Chip(string label, bool active, Vector3 accent, float scale)
    {
        var text = VisibleLabel(label);
        var pad = new Vector2(12f * scale, 5f * scale);
        var size = ImGui.CalcTextSize(text) + pad * 2f;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(label, size);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var max = pos + size;
        if (active)
        {
            dl.AddRectFilled(pos, max, Palette.Pack(accent, hovered ? 0.32f : 0.24f), size.Y * 0.5f);
            dl.AddRect(pos, max, Palette.Pack(accent, 0.7f), size.Y * 0.5f, 1f);
            dl.AddText(pos + pad, Palette.Pack(Palette.Mix(accent, Palette.White, 0.4f)), text);
        }
        else
        {
            dl.AddRectFilled(pos, max, Palette.Pack(Palette.White, hovered ? 0.12f : 0.06f), size.Y * 0.5f);
            dl.AddText(pos + pad, Palette.Pack(Palette.White, hovered ? Palette.TextAlpha : 0.7f), text);
        }

        return clicked;
    }

    /// <summary>A small coloured tag, e.g. a job abbreviation.</summary>
    public static void Tag(string text, Vector3 color, float scale)
    {
        var pad = new Vector2(8f * scale, 2f * scale);
        var size = ImGui.CalcTextSize(text) + pad * 2f;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Palette.Pack(color, 0.18f), size.Y * 0.5f);
        dl.AddText(pos + pad, Palette.Pack(Palette.Mix(color, Palette.White, 0.25f)), text);
        ImGui.Dummy(size);
    }

    /// <summary>A material style switch with its label to the right.</summary>
    public static bool Toggle(string label, ref bool value, Vector3 accent, float scale)
    {
        var text = VisibleLabel(label);
        var height = MathF.Round(18f * scale);
        var width = MathF.Round(height * 1.85f);
        var textSize = ImGui.CalcTextSize(text);
        var gap = 9f * scale;
        var total = new Vector2(width + gap + textSize.X, MathF.Max(height, ImGui.GetFrameHeight()));

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(label, total);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var trackMin = new Vector2(pos.X, MathF.Round(pos.Y + (total.Y - height) * 0.5f));
        var trackMax = trackMin + new Vector2(width, height);
        var radius = height * 0.5f;
        if (value)
        {
            dl.AddRectFilled(trackMin, trackMax, Palette.Pack(accent, hovered ? 1f : 0.9f), radius);
            dl.AddCircleFilled(new Vector2(trackMax.X - radius, trackMin.Y + radius), radius - 3f * scale, Palette.Pack(Palette.OnAccent), 20);
        }
        else
        {
            dl.AddRectFilled(trackMin, trackMax, Palette.Pack(Palette.White, hovered ? 0.22f : 0.15f), radius);
            dl.AddCircleFilled(new Vector2(trackMin.X + radius, trackMin.Y + radius), radius - 3f * scale, Palette.Pack(Palette.White, 0.85f), 20);
        }

        dl.AddText(new Vector2(trackMax.X + gap, MathF.Round(pos.Y + (total.Y - textSize.Y) * 0.5f)),
                   Palette.Pack(Palette.White, Palette.TextAlpha), text);
        return clicked;
    }

    /// <summary>The window's top level navigation: one rounded bar, the selected section filled.</summary>
    public static bool SegmentedTabs(string id, ReadOnlySpan<string> labels, ref int selected, Vector3 accent, float scale)
    {
        var changed = false;
        var padX = MathF.Round(16f * scale);
        var inner = MathF.Round(3f * scale);
        var height = MathF.Round(32f * scale);
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var total = inner * 2f;
        foreach (var label in labels)
            total += ImGui.CalcTextSize(label).X + padX * 2f;

        dl.AddRectFilled(start, start + new Vector2(total, height), Palette.Pack(Palette.White, 0.05f), height * 0.5f);

        ImGui.PushID(id);
        var x = start.X + inner;
        for (var i = 0; i < labels.Length; i++)
        {
            var textSize = ImGui.CalcTextSize(labels[i]);
            var size = new Vector2(textSize.X + padX * 2f, height - inner * 2f);
            var min = new Vector2(x, start.Y + inner);
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton(labels[i], size) && selected != i)
            {
                selected = i;
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            if (i == selected)
            {
                dl.AddRectFilled(min, min + size, Palette.Pack(accent, 0.26f), size.Y * 0.5f);
                dl.AddText(new Vector2(min.X + padX, MathF.Round(min.Y + (size.Y - textSize.Y) * 0.5f)),
                           Palette.Pack(Palette.Mix(accent, Palette.White, 0.45f)), labels[i]);
            }
            else
            {
                if (hovered)
                    dl.AddRectFilled(min, min + size, Palette.Pack(Palette.White, 0.07f), size.Y * 0.5f);
                dl.AddText(new Vector2(min.X + padX, MathF.Round(min.Y + (size.Y - textSize.Y) * 0.5f)),
                           Palette.Pack(Palette.White, hovered ? Palette.TextAlpha : Palette.TextDimAlpha), labels[i]);
            }

            x += size.X;
        }

        ImGui.PopID();
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(total, height));
        return changed;
    }

    /// <summary>
    /// A raised rounded area. Always pair with <see cref="EndCard"/>, whatever this returns.
    /// </summary>
    public static bool BeginCard(string id, Vector2 size, float scale, bool highlight = false)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, highlight ? 0.085f : 0.045f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f * scale, 10f * scale));
        var open = ImGui.BeginChild(id, size, false,
                                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                                    ImGuiWindowFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        return open;
    }

    public static void EndCard()
    {
        ImGui.EndChild();
    }

    /// <summary>Starts a settings row: label on the left, the next item fills the rest of the line.</summary>
    public static void Label(string label, float column, float scale, string? help = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (help != null)
            Tooltip(help, scale);
        ImGui.SameLine(column);
        ImGui.SetNextItemWidth(-1f);
    }

    public static bool EnumCombo<T>(string id, ref T value, Func<T, string> describe) where T : struct, Enum
    {
        var changed = false;
        if (!ImGui.BeginCombo(id, describe(value)))
            return false;

        foreach (var option in Enum.GetValues<T>())
        {
            var isSelected = option.Equals(value);
            if (ImGui.Selectable(describe(option), isSelected) && !isSelected)
            {
                value = option;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    /// <summary>Strips the "##id" part ImGui uses for identity.</summary>
    private static string VisibleLabel(string label)
    {
        var marker = label.IndexOf("##", StringComparison.Ordinal);
        return marker >= 0 ? label[..marker] : label;
    }
}
