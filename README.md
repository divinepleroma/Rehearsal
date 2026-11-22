# Opener Timeline

A Dalamud plugin for practising your opener. It puts the opener on your HUD as a single
translucent line and follows along while you play.

It is a fork of [OpenerCreator](https://github.com/Infiziert90/OpenerCreator) by Infi, herulume
and sevii, rebuilt around a live overlay. The bundled openers are the ones published by
[The Balance](https://www.thebalanceffxiv.com/), unchanged from upstream.

## What it does

- **Live timeline.** A chromeless strip, in the spirit of ActionTimeline: no window, no title
  bar, click-through once locked. The action to press next is ringed and its key cap filled in;
  what you have done slides away to the left; a long opener scrolls to keep your place.
- **Two views.** The *full panel* shows the whole plan. The *compact strip* is a small floating
  row with only what is coming up, and keeps the action to press in one fixed spot. Each view
  remembers its own position; *automatic* shows the panel before the pull and the strip once the
  opener is under way.
- **Out of the way when you want it to be.** Two small buttons on the timeline minimize it to a
  tab or hide it; they appear whenever the mouse can reach it (unlocked, or with Ctrl+Shift held).
  Minimized, a small pill stays behind with where the run stands, and a click on it brings the
  timeline back. The same shown / minimized / hidden switch sits at the top of the plugin window.
- **One line, two lanes.** GCDs sit on the line, oGCDs are raised and smaller between the GCDs
  they are woven into, so the weave structure is visible at a glance. A flat single-row layout
  is available too.
- **Cooldown markers.** One minute cooldowns carry a teal badge and two minute cooldowns a gold
  one, each with its actual recast (so 55s and 110s abilities are classed correctly).
- **GCD numbers and time.** Under every GCD: which one it is and roughly how many seconds into
  the fight it happens, at your own GCD speed. Above your two minute cooldowns a note says which
  GCD they go out on ("GCD 4"), so you can see whether your buffs line up with the party.
- **What to press.** Under every action is the key it is bound to on *your* hotbars, read live
  from your keybinds, including actions that only appear when another button transforms. An
  amber dash means the action is not on any hotbar; `3-7` means hotbar 3, slot 7 has no key.
  Modifiers are spelled out in the full panel and shortened to the game's own letters in the
  compact strip. Cross hotbar positions are shown for controller players.
- **Quiet feedback.** A wrong press warms the ring around the expected action for a moment and
  shows what you hit, small, beside it - nothing moves and nothing is covered, so you can carry
  on. A skipped weave is marked and the timeline jumps ahead with you. A louder style, no cue at
  all, and an optional game sound are available.
- **It starts itself.** No "record" button: press the first action of the opener and the attempt
  begins. When you stop, the result stays up for a moment and the timeline arms itself again.
- **Shows up when it is wanted.** Before the pull it appears when you target a striking dummy or
  an enemy, or as soon as you are inside a trial or raid; which kinds of duty it follows at all
  is up to you.
- **Fits your level.** Below the level an opener was written for (level sync, an old Ultimate, a
  job still being levelled) actions you have not learned are left out and upgraded ones fall
  back to the version you have. A choice made at an older level cap is remembered for that cap.
- **Results.** Accuracy, time and a per-step breakdown of every attempt of the session.
- **Editor.** Build your own openers: search, click to append, drag to reorder; filter by level
  and fit an existing opener to it. Includes an "any action" step and a potion step.
- **Looks like it belongs.** Dark, rounded, translucent surfaces with real background blur
  (Dalamud's backdrop blur) and the game's own font. Looks come as presets (Acrylic, Glass,
  Smoke, Solid, Ghost) with sliders underneath; the accent has a palette, a hex field and a
  picker, and defaults to Material UI's.
- **Few menus.** Settings are three pages, with related switches sharing a row and the
  set-once sliders folded away; the right-click menu only offers what belongs to the view you
  clicked.
- **A walkthrough.** The "Guide" tab explains all of this with the real thing drawn next to
  the words. It opens by itself the first time.

Openers you saved in OpenerCreator are imported on first start; the originals are left alone.

## Commands

| Command | |
| --- | --- |
| `/opener` | Open the opener library |
| `/opener guide` | Open the walkthrough |
| `/opener settings` | Open the settings |
| `/opener show` / `min` / `hide` | Show the timeline, minimize it to a small tab, or hide it |
| `/opener toggle` | Flip between shown and hidden |
| `/opener unlock` / `/opener lock` | Free the timeline to move and resize it, or pin it |
| `/opener view full` / `compact` / `auto` | Switch between the full panel and the compact strip |
| `/opener reset` | Abandon the current attempt |
| `/opener cd [seconds]` | Start a private practice countdown |

None of these are needed: the timeline has minimize and hide buttons, the plugin window has the
shown / minimized / hidden switch on every tab, and right-clicking the timeline (or its tab)
opens a menu with opener switching, the view, and the parts of that view. When it is locked,
hold Ctrl+Shift to reach it.

## About the numbers

The Balance publishes its openers as pictures (only a few jobs have the sequence as text), with
no data feed and no licence for reuse, so there is nothing a plugin could download and keep in
sync. The bundled sequences are the hand-transcribed ones inherited from OpenerCreator. Times
and GCD numbers are computed from the game's own data and your current GCD speed, assuming the
GCD keeps rolling: a plan, not a simulation. Guides disagree on whether a cast started before
the pull is "GCD 1"; it is counted by default and can be switched off.

Which action upgrades which (needed to fit an opener to a lower level) is not stored in any game
table, so it is read from the English trait descriptions ("Upgrades Jolt II to Jolt III.").
`UiPreview --data` prints everything derived this way for checking.

## Differences from OpenerCreator

- Attempts are tracked live, step by step, instead of being compared after a manual recording.
- Actions are matched by id, by what the game currently upgrades them to, or by exact name.
  Upstream accepted any action whose name merely *contained* the expected one, so Fire IV
  counted as Fire.
- Only real actions are counted: mounting or using an item can no longer be mistaken for an
  action that happens to share its number.
- Sprint and role actions that are not part of the opener are ignored (this replaces the single
  True North option).
- GCD versus oGCD is decided by the cooldown group rather than the action category, which gets
  cases such as Ninjutsu right.
- Highlighting the next action on the hotbar is off by default.
- Settings live in their own file, so both plugins can be installed side by side.

## Building

Needs the .NET 10 SDK and a Dalamud installation from XIVLauncher (the plugin is compiled
against `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```powershell
.\build.ps1                 # build the plugin and run the tests
.\build.ps1 -Preview        # also render the UI to artifacts\preview\*.png
```

To load the result, add the path of `OpenerTimeline\bin\Release\OpenerTimeline.dll` under
`/xlsettings` → Experimental → Dev Plugin Locations, then enable it in `/xlplugins` → Dev Tools.
With automatic reloading switched on there, every successful build is picked up by the running
game.

### Layout

| Path | |
| --- | --- |
| `OpenerTimeline/Data` | Action data, upgrade chains, level fitting, opener groups, the opener library |
| `OpenerTimeline/Training` | The attempt engine, action matching and timing estimates (no game dependencies) |
| `OpenerTimeline/UI` | Everything that draws: timeline renderer, theme, the window views |
| `OpenerTimeline/Game` | Hooks, hotbar and keybind lookup, attempt lifecycle, where-am-I detection |
| `OpenerTimeline/Windows` | The Dalamud windows hosting the UI |
| `OpenerTimeline.Tests` | Unit tests for everything that does not need the game |
| `tools/UiPreview` | Renders the real UI code to PNG without the game running |
| `Openers/openers.json` | The bundled openers |

### The UI preview tool

`Data`, `Training` and `UI` do not reference Dalamud itself, only its ImGui binding and Lumina.
`tools/UiPreview` links those sources, feeds them real action data and icons read from your game
install, runs them through Dalamud's ImGui build and rasterises the draw data in software. That
makes it possible to work on the look of the plugin, and to check it, without launching the game:

```powershell
.\build.ps1 -Preview -PreviewScale 1.25 -PreviewBackground C:\path\to\a\screenshot.png
```

The backdrop blur in the preview is a plain box blur standing in for Dalamud's.

## Licence

AGPL-3.0-or-later, the same as OpenerCreator. See [LICENSE.md](LICENSE.md).
