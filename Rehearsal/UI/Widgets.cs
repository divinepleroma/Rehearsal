using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI;

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

    /// <summary>A wrapped line of secondary text behind an accent dot.</summary>
    public static void Bullet(string text, Vector3 accent, float scale)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddCircleFilled(start + new Vector2(8f * scale, ImGui.GetTextLineHeight() * 0.55f), 2.6f * scale,
                                                  Palette.Pack(accent, 0.9f), 12);
        ImGui.Indent(20f * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.Unindent(20f * scale);
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
        if (ImGui.IsItemHovered())
            TooltipText(text, scale);
    }

    /// <summary>A tooltip regardless of what is hovered, for things that are not ImGui items.</summary>
    public static void TooltipText(string text, float scale)
    {
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

    /// <summary>The width <see cref="Chip"/> gives a label.</summary>
    public static float ChipWidth(string label, float scale)
    {
        return ImGui.CalcTextSize(VisibleLabel(label)).X + 24f * scale;
    }

    /// <summary>A pill that can be switched on and off, for filters and compact option rows.</summary>
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

    /// <summary>Height of <see cref="SegmentedTabs"/>.</summary>
    public static float SegmentedHeight(float scale, bool small = false)
    {
        return MathF.Round((small ? 26f : 32f) * scale);
    }

    /// <summary>Width <see cref="SegmentedTabs"/> needs for these labels.</summary>
    public static float SegmentedWidth(ReadOnlySpan<string> labels, float scale, bool small = false)
    {
        var padX = MathF.Round((small ? 11f : 16f) * scale);
        var total = MathF.Round(3f * scale) * 2f;
        foreach (var label in labels)
            total += ImGui.CalcTextSize(label).X + padX * 2f;
        return total;
    }

    /// <summary>
    /// One rounded bar with the selected segment filled: the window's top level navigation, and
    /// (small) any one-of-a-few choice that deserves to be seen at a glance.
    /// </summary>
    public static bool SegmentedTabs(string id, ReadOnlySpan<string> labels, ref int selected, Vector3 accent, float scale, bool small = false)
    {
        var changed = false;
        var padX = MathF.Round((small ? 11f : 16f) * scale);
        var inner = MathF.Round(3f * scale);
        var height = SegmentedHeight(scale, small);
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var total = SegmentedWidth(labels, scale, small);

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

    /// <summary>
    /// A heading that opens and closes what is under it, for options most people set once or
    /// never. Returns whether it is open.
    /// </summary>
    public static bool Fold(string label, ref bool open, float scale)
    {
        var text = VisibleLabel(label);
        var textSize = ImGui.CalcTextSize(text);
        var arrow = MathF.Round(18f * scale);
        var height = ImGui.GetFrameHeight();

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton(label, new Vector2(arrow + textSize.X + 8f * scale, height)))
            open = !open;

        var color = Palette.Pack(Palette.White, ImGui.IsItemHovered() ? Palette.TextAlpha : Palette.TextDimAlpha);
        var dl = ImGui.GetWindowDrawList();
        var center = new Vector2(MathF.Round(pos.X + arrow * 0.4f), MathF.Round(pos.Y + height * 0.5f));
        var reach = 4f * scale;
        if (open)
        {
            dl.AddTriangleFilled(center + new Vector2(-reach, -reach * 0.55f), center + new Vector2(reach, -reach * 0.55f),
                                 center + new Vector2(0f, reach * 0.75f), color);
        }
        else
        {
            dl.AddTriangleFilled(center + new Vector2(-reach * 0.55f, -reach), center + new Vector2(reach * 0.75f, 0f),
                                 center + new Vector2(-reach * 0.55f, reach), color);
        }

        dl.AddText(new Vector2(pos.X + arrow, MathF.Round(pos.Y + (height - textSize.Y) * 0.5f)), color, text);
        return open;
    }

    /// <summary>Strips the "##id" part ImGui uses for identity.</summary>
    private static string VisibleLabel(string label)
    {
        var marker = label.IndexOf("##", StringComparison.Ordinal);
        return marker >= 0 ? label[..marker] : label;
    }
}

/// <summary>
/// A row of chips that wraps onto further lines when it runs out of room. Several switches, or a
/// pick-one choice, in the space one of them used to take.
/// </summary>
public ref struct ChipRow
{
    private readonly Vector3 accent;
    private readonly float right;
    private readonly float scale;
    private int count;

    /// <summary>Starts the row at the cursor; it may use the rest of the line.</summary>
    public ChipRow(Vector3 accent, float scale)
    {
        this.accent = accent;
        this.scale = scale;
        right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        count = 0;

        // Inside a group, wrapped lines start under the first chip instead of at the window edge.
        ImGui.BeginGroup();
    }

    /// <summary>A chip that switches one setting on and off.</summary>
    public bool Flag(string label, ref bool value)
    {
        Place(label);
        if (!Widgets.Chip(label, value, accent, scale))
            return false;

        value = !value;
        return true;
    }

    /// <summary>A chip that is lit while the setting has this value, and sets it when clicked.</summary>
    public bool Choice<T>(string label, ref T current, T value) where T : struct, Enum
    {
        Place(label);
        var selected = EqualityComparer<T>.Default.Equals(current, value);
        if (!Widgets.Chip(label, selected, accent, scale) || selected)
            return false;

        current = value;
        return true;
    }

    public void End()
    {
        ImGui.EndGroup();
    }

    /// <summary>Keeps the next chip on the line of the last one if it still fits there.</summary>
    private void Place(string label)
    {
        if (count++ == 0)
            return;

        var needed = ImGui.GetStyle().ItemSpacing.X + Widgets.ChipWidth(label, scale);
        if (ImGui.GetItemRectMax().X + needed <= right)
            ImGui.SameLine();
    }
}
