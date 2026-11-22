using System.Numerics;
using OpenerTimeline.UI;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"OpenerTimelineSettings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(path);
    }

    [Fact]
    public void Settings_SurviveARoundTrip()
    {
        var settings = new Settings
        {
            OverlayLocked = true,
            View = TimelineView.Automatic,
            ShowAlways = true,
            TrackDungeons = true,
            Layout = TimelineLayoutMode.Inline,
            IconSize = 64f,
            CompactIconSize = 32f,
            Accent = new Vector3(0.9f, 0.4f, 0.2f),
            CooldownBadges = CooldownBadgeMode.All,
            BurstCallouts = BurstCallouts.OneAndTwoMinute,
            KeybindStyle = KeybindStyle.Compact,
            HintSource = HintSource.Gamepad,
            MistakeFeedback = FeedbackStyle.Strong,
            MistakeSound = 11,
            StopAtFirstMistake = true,
            CountdownSeconds = 12
        };
        settings.Selected["BLM"] = new SavedSelection { Name = "Standard (The Balance)", IsDefault = true };
        settings.Selected["BLM@90"] = new SavedSelection { Name = "Endwalker", IsDefault = false };
        settings.LearnedBases[25797] = 141;

        settings.Save(path);
        var loaded = Settings.Load(path, (message, ex) => Assert.Fail($"{message}: {ex.Message}"));

        Assert.True(loaded.OverlayLocked);
        Assert.Equal(TimelineView.Automatic, loaded.View);
        Assert.True(loaded.ShowAlways);
        Assert.True(loaded.TrackDungeons);
        Assert.Equal(TimelineLayoutMode.Inline, loaded.Layout);
        Assert.Equal(64f, loaded.IconSize);
        Assert.Equal(32f, loaded.CompactIconSize);
        Assert.Equal(new Vector3(0.9f, 0.4f, 0.2f), loaded.Accent);
        Assert.Equal(CooldownBadgeMode.All, loaded.CooldownBadges);
        Assert.Equal(BurstCallouts.OneAndTwoMinute, loaded.BurstCallouts);
        Assert.Equal(KeybindStyle.Compact, loaded.KeybindStyle);
        Assert.Equal(HintSource.Gamepad, loaded.HintSource);
        Assert.Equal(FeedbackStyle.Strong, loaded.MistakeFeedback);
        Assert.Equal(11, loaded.MistakeSound);
        Assert.True(loaded.StopAtFirstMistake);
        Assert.Equal(12, loaded.CountdownSeconds);
        Assert.Equal("Standard (The Balance)", loaded.Selected["BLM"].Name);
        Assert.Equal("Endwalker", loaded.Selected["BLM@90"].Name);
        Assert.Equal(141u, loaded.LearnedBases[25797]);
        Assert.Equal(Settings.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Defaults_AreQuietAndStayOutOfTheWay()
    {
        var settings = Settings.Load(path);

        Assert.True(settings.OverlayEnabled);
        Assert.False(settings.OverlayLocked);
        Assert.False(settings.AbilityAnts);
        Assert.Equal(FeedbackStyle.Gentle, settings.MistakeFeedback);
        Assert.Equal(0, settings.MistakeSound);
        Assert.False(settings.ShowAlways);
        Assert.True(settings.ShowAtDummy);
        Assert.False(settings.TrackDungeons);
        Assert.True(settings.TrackRaids);
        Assert.True(settings.AutoAdjustLevel);
    }

    [Fact]
    public void BrokenFile_IsReportedAndGivesDefaults()
    {
        File.WriteAllText(path, "{ definitely not json");
        var errors = 0;

        var loaded = Settings.Load(path, (_, _) => errors++);

        Assert.Equal(1, errors);
        Assert.Equal(48f, loaded.IconSize);
    }

    [Fact]
    public void OutOfRangeValues_AreClampedOnLoad()
    {
        File.WriteAllText(path, """{ "Version": 3, "IconSize": 9000, "PanelOpacity": -3, "CountdownSeconds": 0, "MistakeSound": 99, "Selected": null }""");

        var loaded = Settings.Load(path);

        Assert.Equal(96f, loaded.IconSize);
        Assert.Equal(0f, loaded.PanelOpacity);
        Assert.Equal(3, loaded.CountdownSeconds);
        Assert.Equal(16, loaded.MistakeSound);
        Assert.NotNull(loaded.Selected);
    }

    [Fact]
    public void BrokenSelections_AreDroppedOnLoad()
    {
        File.WriteAllText(path, """{ "Version": 3, "Selected": { "BLM": null, "RDM": { "Name": null }, "SAM": { "Name": "Mine" } } }""");

        var loaded = Settings.Load(path, (message, ex) => Assert.Fail($"{message}: {ex.Message}"));

        Assert.Equal("Mine", Assert.Single(loaded.Selected).Value.Name);
    }

    [Fact]
    public void VersionOne_AlwaysVisible_BecomesShowAlways()
    {
        File.WriteAllText(path, """{ "Version": 1, "ArmedVisibility": 0, "HideInDuties": true, "IconSize": 56 }""");

        var loaded = Settings.Load(path);

        Assert.True(loaded.ShowAlways);
        Assert.False(loaded.TrackDungeons);
        Assert.Equal(56f, loaded.IconSize);
        Assert.Equal(Settings.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void VersionOne_NeverVisible_TurnsTheTriggersOff()
    {
        File.WriteAllText(path, """{ "Version": 1, "ArmedVisibility": 2 }""");

        var loaded = Settings.Load(path);

        Assert.False(loaded.ShowAlways);
        Assert.False(loaded.ShowAtDummy);
        Assert.False(loaded.ShowOnEnemyTarget);
        Assert.False(loaded.ShowInBossDuties);
    }

    [Fact]
    public void VersionOne_ShownInDuties_TracksEveryDuty()
    {
        File.WriteAllText(path, """{ "Version": 1, "ArmedVisibility": 1, "HideInDuties": false }""");

        var loaded = Settings.Load(path);

        Assert.True(loaded.ShowAtDummy);
        Assert.True(loaded.TrackDungeons);
        Assert.True(loaded.TrackAllianceRaids);
        Assert.True(loaded.TrackOtherDuties);
    }

    [Fact]
    public void BeforeVersionThree_SpelledOut_BecomesAutomatic()
    {
        File.WriteAllText(path, """{ "Version": 2, "KeybindStyle": 1, "View": 1 }""");

        var loaded = Settings.Load(path);

        Assert.Equal(KeybindStyle.Auto, loaded.KeybindStyle);
        Assert.Equal(TimelineView.Compact, loaded.View);
    }

    [Fact]
    public void CurrentVersion_KeepsAnExplicitChoice()
    {
        var settings = new Settings { KeybindStyle = KeybindStyle.Full };
        settings.Save(path);

        Assert.Equal(KeybindStyle.Full, Settings.Load(path).KeybindStyle);
    }

    [Fact]
    public void ToStyle_FullView()
    {
        var settings = new Settings { IconSize = 56f, ShowKeybinds = false, Layout = TimelineLayoutMode.Inline, ShowRuler = false };

        var style = settings.ToStyle(1.5f);

        Assert.False(style.Compact);
        Assert.Equal(1.5f, style.Scale);
        Assert.Equal(56f, style.IconSize);
        Assert.False(style.ShowHints);
        Assert.False(style.ShowRuler);
        Assert.Equal(TimelineLayoutMode.Inline, style.Layout);
        Assert.Equal(settings.Accent, style.Accent);
        Assert.Equal(BurstCallouts.TwoMinute, style.Callouts);
    }

    [Fact]
    public void ToStyle_CompactView_IsAFlatRowWithoutTheExtras()
    {
        var settings = new Settings { CompactIconSize = 36f, Layout = TimelineLayoutMode.Staggered, ShowHeader = true, ShowRuler = true };

        var style = settings.ToStyle(1f, true);

        Assert.True(style.Compact);
        Assert.Equal(36f, style.IconSize);
        Assert.Equal(TimelineLayoutMode.Inline, style.Layout);
        Assert.False(style.ShowHeader);
        Assert.False(style.ShowRuler);
        Assert.False(style.ShowLaneLabels);
        Assert.Equal(BurstCallouts.Off, style.Callouts);
        Assert.True(style.ShowStatus);
    }

    [Fact]
    public void SelectionKey_SeparatesOlderLevelCaps()
    {
        Assert.Equal("RDM", Settings.SelectionKey("RDM", 100, 100));
        Assert.Equal("RDM@90", Settings.SelectionKey("RDM", 90, 100));
        Assert.Equal("RDM@70", Settings.SelectionKey("RDM", 70, 100));
    }
}

public class AppearanceTests
{
    [Theory]
    [InlineData("#59B9FF", 0x59, 0xB9, 0xFF)]
    [InlineData("59b9ff", 0x59, 0xB9, 0xFF)]
    [InlineData(" #FFF ", 0xFF, 0xFF, 0xFF)]
    [InlineData("#0a0", 0x00, 0xAA, 0x00)]
    public void Hex_IsParsed(string text, int r, int g, int b)
    {
        Assert.True(Appearance.TryParseHex(text, out var color));
        Assert.Equal(new Vector3(r / 255f, g / 255f, b / 255f), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void BadHex_IsRejected(string text)
    {
        Assert.False(Appearance.TryParseHex(text, out _));
    }

    [Fact]
    public void Hex_RoundTrips()
    {
        foreach (var accent in Appearance.Accents)
        {
            Assert.True(Appearance.TryParseHex(Appearance.ToHex(accent.Color), out var parsed));
            Assert.True(Vector3.Distance(accent.Color, parsed) < 0.005f, accent.Name);
        }
    }

    [Fact]
    public void DefaultSettings_MatchTheFirstPreset()
    {
        var settings = new Settings();

        Assert.True(Appearance.Presets[0].Matches(settings));
        Assert.False(Appearance.Presets[1].Matches(settings));

        Appearance.Presets[1].Apply(settings);
        Assert.True(Appearance.Presets[1].Matches(settings));
    }
}
