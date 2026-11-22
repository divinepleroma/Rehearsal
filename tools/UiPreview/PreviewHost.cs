using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Lumina;
using Lumina.Data.Files;
using OpenerTimeline.Data;
using OpenerTimeline.UI;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.Preview;

/// <summary>
/// The preview tool's side of <see cref="IAppHost"/>: icons come straight out of the game's data
/// files, fonts from a TTF, and backdrop blur requests are recorded for the rasteriser.
/// </summary>
internal sealed unsafe class PreviewHost : IAppHost
{
    private const ulong FirstIconTexture = 0x1000;
    private const ulong FirstFontTexture = 0x10;

    private readonly Dictionary<TimelineFont, ImFontPtr> fonts = new();
    private readonly GameData gameData;
    private readonly Dictionary<uint, ulong> iconTextures = new();
    private readonly Dictionary<ulong, Texture> textures = new();
    private ulong nextTexture = FirstIconTexture;

    public PreviewHost(GameData gameData, ActionCatalog catalog, OpenerLibrary library, float scale)
    {
        this.gameData = gameData;
        Catalog = catalog;
        Library = library;
        Scale = scale;
    }

    public float Scale { get; }
    public Settings Settings { get; } = new();
    public ActionCatalog Catalog { get; }
    public OpenerLibrary Library { get; }
    public FakeTrainer FakeTrainer { get; set; } = null!;
    public ITrainer Trainer => FakeTrainer;
    public Jobs CurrentJob { get; set; } = Jobs.BLM;
    public string Version => "0.1.0";

    public void SaveSettings()
    {
    }

    public void OpenLink(string url)
    {
    }

    public void PlaySound(int soundEffect)
    {
    }

    public IReadOnlyDictionary<ulong, Texture> Textures => textures;

    /// <summary>Backdrop requests made during the current frame.</summary>
    public List<(Vector2 Min, Vector2 Max, float Rounding, float Strength)> Backdrops { get; } = [];

    public ImFontPtr DefaultFont { get; private set; }

    /// <summary>Loads the fonts and uploads the atlas into the texture table.</summary>
    public void BuildFonts(string fontPath)
    {
        var scale = Scale;
        var io = ImGui.GetIO();
        DefaultFont = io.Fonts.AddFontFromFileTTF(fontPath, MathF.Round(17f * scale));
        fonts[TimelineFont.Small] = io.Fonts.AddFontFromFileTTF(fontPath, MathF.Round(12.5f * scale));
        fonts[TimelineFont.Body] = io.Fonts.AddFontFromFileTTF(fontPath, MathF.Round(15f * scale));
        fonts[TimelineFont.Number] = io.Fonts.AddFontFromFileTTF(fontPath, MathF.Round(15f * scale));
        io.Fonts.Build();

        for (var t = 0; t < io.Fonts.Textures.Size; t++)
        {
            byte* data;
            int width, height, bytesPerPixel;
            io.Fonts.GetTexDataAsRGBA32(t, &data, &width, &height, &bytesPerPixel);
            var copy = new byte[width * height * 4];
            new ReadOnlySpan<byte>(data, copy.Length).CopyTo(copy);

            var id = FirstFontTexture + (ulong)t;
            textures[id] = new Texture(width, height, copy);
            io.Fonts.SetTexID(t, new ImTextureID(id));
        }
    }

    public ImTextureID GetIcon(uint iconId)
    {
        if (iconTextures.TryGetValue(iconId, out var known))
            return new ImTextureID(known);

        var id = nextTexture++;
        iconTextures[iconId] = id;
        textures[id] = LoadIcon(iconId) ?? Placeholder();
        return new ImTextureID(id);
    }

    public IDisposable? PushFont(TimelineFont font)
    {
        if (!fonts.TryGetValue(font, out var handle))
            return null;

        ImGui.PushFont(handle);
        return FontScope.Instance;
    }

    public void DrawBackdrop(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float strength, float opacity)
    {
        Backdrops.Add((min, max, rounding, strength * opacity));
    }

    private Texture? LoadIcon(uint iconId)
    {
        var folder = iconId / 1000 * 1000;
        var file = gameData.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}_hr1.tex")
                   ?? gameData.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}.tex");
        if (file == null)
            return null;

        // Lumina hands back BGRA.
        var bgra = file.ImageData;
        var rgba = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return new Texture(file.Header.Width, file.Header.Height, rgba);
    }

    private static Texture Placeholder()
    {
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 90;
            rgba[i + 1] = 40;
            rgba[i + 2] = 110;
            rgba[i + 3] = 255;
        }

        return new Texture(4, 4, rgba);
    }

    private sealed class FontScope : IDisposable
    {
        public static readonly FontScope Instance = new();

        public void Dispose()
        {
            ImGui.PopFont();
        }
    }
}
