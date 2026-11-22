using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using OpenerTimeline.Data;
using OpenerTimeline.UI;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.Game;

/// <summary>
/// The plugin's implementation of <see cref="IAppHost"/>: game icons, the game's own fonts and
/// Dalamud's backdrop blur.
/// </summary>
internal sealed class GameHost : IAppHost, IDisposable
{
    /// <summary>Settings are written at most this often while a slider is being dragged.</summary>
    private const double SaveDelaySeconds = 0.75d;

    private readonly IFontHandle bodyFont;
    private readonly IFontHandle numberFont;
    private readonly string settingsPath;
    private readonly IFontHandle smallFont;
    private double dirtySince = -1d;
    private Trainer? trainer;

    public GameHost(Settings settings, string settingsPath, ActionCatalog catalog, OpenerLibrary library)
    {
        Settings = settings;
        this.settingsPath = settingsPath;
        Catalog = catalog;
        Library = library;

        // The game's own UI typeface, so the overlay reads as part of the HUD. The atlas is scaled
        // with Dalamud's global scale, which is also what the layout is multiplied by.
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        smallFont = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis96));
        bodyFont = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));
        numberFont = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));
    }

    public float Scale => ImGuiHelpers.GlobalScale;
    public Settings Settings { get; }
    public ActionCatalog Catalog { get; }
    public OpenerLibrary Library { get; }
    public ITrainer Trainer => trainer ?? throw new InvalidOperationException("The trainer has not been attached yet.");
    public Jobs CurrentJob => trainer?.CurrentJob ?? Jobs.ANY;
    public string Version => Plugin.PluginInterface.Manifest.AssemblyVersion.ToString(3);

    public void Attach(Trainer value)
    {
        trainer = value;
    }

    public ImTextureID GetIcon(uint iconId)
    {
        return Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty().Handle;
    }

    public IDisposable? PushFont(TimelineFont font)
    {
        var handle = font switch
        {
            TimelineFont.Small => smallFont,
            TimelineFont.Number => numberFont,
            _ => bodyFont
        };

        // Until the atlas has been built the current font simply stays in place.
        return handle.Available ? handle.Push() : null;
    }

    public void DrawBackdrop(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float strength, float opacity)
    {
        if (strength <= 0.01f || opacity <= 0.05f)
            return;

        // The blur samples the game's frame, so it only makes sense inside the game window.
        if (ImGui.GetWindowViewport().ID != ImGui.GetMainViewport().ID)
            return;

        var blur = (0.6f + strength * 5.4f) * opacity;
        var luminosity = new Vector4(Palette.Surface, 0.4f * opacity);
        ImGuiHelpers.PrependBlurBehind(drawList, min, max, blur, rounding, default, luminosity, 0.1f * opacity);
    }

    public void SaveSettings()
    {
        if (dirtySince < 0d)
            dirtySince = Plugin.Now;
    }

    /// <summary>Writes pending settings changes once they have settled.</summary>
    public void FlushSettings(bool force = false)
    {
        if (dirtySince < 0d)
            return;
        if (!force && Plugin.Now - dirtySince < SaveDelaySeconds)
            return;

        dirtySince = -1d;
        Settings.Save(settingsPath, (message, ex) => Plugin.Log.Error(ex, message));
    }

    public void OpenLink(string url)
    {
        Dalamud.Utility.Util.OpenLink(url);
    }

    public unsafe void PlaySound(int soundEffect)
    {
        if (soundEffect is < 1 or > 16)
            return;

        try
        {
            // The macro sounds <se.1> to <se.16> are effects 37 to 52.
            UIGlobals.PlaySoundEffect((uint)(36 + soundEffect));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not play a sound effect");
        }
    }

    public void Dispose()
    {
        FlushSettings(true);
        smallFont.Dispose();
        bodyFont.Dispose();
        numberFont.Dispose();
    }
}
