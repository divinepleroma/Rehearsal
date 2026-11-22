using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;

namespace Rehearsal.Game;

/// <summary>
/// A private practice countdown. The large numerals reuse the game's own countdown art
/// (drawing code carried over from OpenerCreator).
/// </summary>
internal sealed class Countdown
{
    private static readonly Vector2 NumberSize = new(240, 320);

    private readonly ISharedImmediateTexture go;
    private readonly ISharedImmediateTexture numbers;
    private int seconds;
    private Stopwatch? started;

    public Countdown()
    {
        var language = Plugin.DataManager.Language switch
        {
            ClientLanguage.French => "fr",
            ClientLanguage.German => "de",
            ClientLanguage.Japanese => "ja",
            _ => "en"
        };

        go = Plugin.TextureProvider.GetFromGame($"ui/icon/121000/{language}/121841_hr1.tex");
        numbers = Plugin.TextureProvider.GetFromGame("ui/uld/ScreenInfo_CountDown_hr1.tex");
    }

    public bool IsActive => started != null;

    /// <summary>Seconds until zero; negative once it has passed (the "go" lingers briefly), or when idle.</summary>
    public float Remaining => started == null ? -1f : seconds - (float)started.Elapsed.TotalSeconds;

    public void Start(int length)
    {
        seconds = length;
        started = Stopwatch.StartNew();
    }

    public void Stop()
    {
        started = null;
    }

    /// <summary>Retires the countdown shortly after it reached zero.</summary>
    public void Update()
    {
        if (started != null && Remaining < -2f)
            started = null;
    }

    /// <summary>Draws the large numerals in the middle of the screen.</summary>
    public void DrawLarge()
    {
        if (started == null)
            return;

        var timer = Remaining;
        if (timer < -2f)
            return;

        var drawList = ImGui.GetForegroundDrawList();
        var ceil = (float)Math.Ceiling(timer);
        const float uSpacing = 1.0f / 6.0f;

        ceil = timer switch
        {
            <= 0 => 0,
            > 5 => (int)Math.Ceiling(timer / 5.0) * 5.0f,
            _ => ceil
        };

        var anim = 1.0f - Math.Clamp(ceil - timer - 0.5f, 0.0f, 1.0f);
        var color = 0x00FFFFFF + ((uint)(anim * 255) << 24);
        var center = ImGui.GetMainViewport().GetCenter();

        switch (timer)
        {
            case <= 0:
            {
                var wrap = go.GetWrapOrEmpty();
                drawList.AddImage(wrap.Handle, center - (wrap.Size / 2), center + (wrap.Size / 2), Vector2.Zero, Vector2.One, color);
                break;
            }
            case <= 5:
                drawList.AddImage(numbers.GetWrapOrEmpty().Handle, center - (NumberSize / 2), center + (NumberSize / 2),
                                  new Vector2(ceil * uSpacing, 0.0f), new Vector2((ceil * uSpacing) + uSpacing, 1.0f), color);
                break;
            default:
            {
                var dig1 = (int)Math.Floor(ceil / 10.0f);
                var dig2 = ceil % 10;
                var handle = numbers.GetWrapOrEmpty().Handle;
                drawList.AddImage(handle, center - NumberSize with { Y = NumberSize.Y / 2 }, center + new Vector2(0.0f, NumberSize.Y / 2),
                                  new Vector2(dig1 * uSpacing, 0.0f), new Vector2((dig1 * uSpacing) + uSpacing, 1.0f), color);
                drawList.AddImage(handle, center - new Vector2(0.0f, NumberSize.Y / 2), center + NumberSize with { Y = NumberSize.Y / 2 },
                                  new Vector2(dig2 * uSpacing, 0.0f), new Vector2((dig2 * uSpacing) + uSpacing, 1.0f), color);
                break;
            }
        }
    }
}
