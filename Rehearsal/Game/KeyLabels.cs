using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace Rehearsal.Game;

/// <summary>Short, readable names for the keys a hotbar slot can be bound to.</summary>
internal static partial class KeyLabels
{
    private const uint MapVirtualKeyToChar = 2;

    public static string? For(SeVirtualKey key)
    {
        var value = (byte)key;
        switch (key)
        {
            case SeVirtualKey.NO_KEY:
                return null;
            case >= SeVirtualKey.KEY_0 and <= SeVirtualKey.KEY_9:
                return ((char)('0' + (value - (byte)SeVirtualKey.KEY_0))).ToString();
            case >= SeVirtualKey.A and <= SeVirtualKey.Z:
                return ((char)('A' + (value - (byte)SeVirtualKey.A))).ToString();
            case >= SeVirtualKey.NUMPAD0 and <= SeVirtualKey.NUMPAD9:
                return "N" + (value - (byte)SeVirtualKey.NUMPAD0);
            case >= SeVirtualKey.F1 and <= SeVirtualKey.F16:
                return "F" + (value - (byte)SeVirtualKey.F1 + 1);
            case >= SeVirtualKey.F19 and <= SeVirtualKey.F24:
                return "F" + (value - (byte)SeVirtualKey.F19 + 19);

            case SeVirtualKey.LBUTTON or SeVirtualKey.PAD_LMB:
                return "M1";
            case SeVirtualKey.RBUTTON or SeVirtualKey.PAD_RMB:
                return "M2";
            case SeVirtualKey.MBUTTON or SeVirtualKey.PAD_MMB:
                return "M3";
            case SeVirtualKey.XBUTTON1 or SeVirtualKey.PAD_MB4:
                return "M4";
            case SeVirtualKey.XBUTTON2 or SeVirtualKey.PAD_MB5:
                return "M5";
            case SeVirtualKey.PAD_MB6:
                return "M6";
            case SeVirtualKey.PAD_MB7:
                return "M7";

            case SeVirtualKey.SPACE:
                return "Space";
            case SeVirtualKey.TAB:
                return "Tab";
            case SeVirtualKey.RETURN:
                return "Enter";
            case SeVirtualKey.BACK:
                return "Bksp";
            case SeVirtualKey.ESCAPE:
                return "Esc";
            case SeVirtualKey.INSERT:
                return "Ins";
            case SeVirtualKey.DELETE:
                return "Del";
            case SeVirtualKey.HOME:
                return "Home";
            case SeVirtualKey.END:
                return "End";
            case SeVirtualKey.PRIOR:
                return "PgUp";
            case SeVirtualKey.NEXT:
                return "PgDn";
            case SeVirtualKey.LEFT:
                return "Left";
            case SeVirtualKey.UP:
                return "Up";
            case SeVirtualKey.RIGHT:
                return "Right";
            case SeVirtualKey.DOWN:
                return "Down";
            case SeVirtualKey.CAPITAL:
                return "Caps";
            case SeVirtualKey.PAUSE:
                return "Pause";
            case SeVirtualKey.SNAPSHOT:
                return "PrtSc";
            case SeVirtualKey.NUMLOCK:
                return "NumLk";
            case SeVirtualKey.SCROLL:
                return "ScrLk";
            case SeVirtualKey.APPS:
                return "Menu";
            case SeVirtualKey.MULTIPLY:
                return "N*";
            case SeVirtualKey.ADD:
                return "N+";
            case SeVirtualKey.SUBTRACT:
                return "N-";
            case SeVirtualKey.DECIMAL:
                return "N.";
            case SeVirtualKey.DIVIDE:
                return "N/";
        }

        // Punctuation keys keep their Windows virtual key codes, so the keyboard layout can tell us
        // which character is printed on them.
        var mapped = MapVirtualKeyW(value, MapVirtualKeyToChar) & 0x7FFF;
        if (mapped > 32 && mapped < 0xFFFF && !char.IsControl((char)mapped))
            return char.ToUpperInvariant((char)mapped).ToString();

        return "K" + value;
    }

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);
}
