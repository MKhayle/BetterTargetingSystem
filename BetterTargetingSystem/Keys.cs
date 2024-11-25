using Dalamud.Game.ClientState.Keys;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace BetterTargetingSystem.Keybinds;

public unsafe class Keybind
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] KeyStates);
    private static byte[] KeyStates = new byte[256];

    public VirtualKey? Key;
    public bool ControlModifier;
    public bool ShiftModifier;
    public bool AltModifier;

    public static void GetKeyboardState() => GetKeyboardState(KeyStates);

    public static bool IsKeyDown(int key)
    {
        return KeyStates[key] > 1;
    }

    public Keybind(VirtualKey? key = null, bool ctrl = false, bool shift = false, bool alt = false)
    {
        this.Key = key;
        this.ControlModifier = ctrl;
        this.ShiftModifier = shift;
        this.AltModifier = alt;
    }

    private MouseKey? GetMouseKey()
    {
        switch (this.Key)
        {
            case VirtualKey.MBUTTON:
                return MouseKey.MBUTTON;
            case VirtualKey.XBUTTON1:
                return MouseKey.XBUTTON1;
            case VirtualKey.XBUTTON2:
                return MouseKey.XBUTTON2;
            default:
                return null;
        }
    }

    public bool IsPressed()
    {
        if (this.Key == null)
            return false;

        if (this.ControlModifier != Plugin.KeyState[(int) VirtualKey.CONTROL]
            || this.ShiftModifier != Plugin.KeyState[(int) VirtualKey.SHIFT]
            || this.AltModifier != Plugin.KeyState[(int) VirtualKey.MENU])
            return false;

        MouseKey? mouseKey;
        if ((mouseKey = this.GetMouseKey()) != null)
            return ((int)CSFramework.Instance()->CursorInputs.MouseButtonPressedFlags & (int)mouseKey) != 0;
        else
            return Plugin.KeyState[(int)this.Key];
    }

    public override string ToString()
    {
        var keys = new string[]
        {
            this.ControlModifier ? "Ctrl" : "",
            this.AltModifier ? "Alt" : "",
            this.ShiftModifier ? "Shift" : "",
            this.Key != null ? this.Key.ToString()! : "",
        };

        return String.Join("+", keys.Where(s => s != ""));
    }

    public static List<VirtualKey> SupportedKeys = new List<VirtualKey>()
    {
        VirtualKey.MBUTTON,
        VirtualKey.XBUTTON1,
        VirtualKey.XBUTTON2,
        VirtualKey.CAPITAL,
        VirtualKey.SPACE,
        VirtualKey.PRIOR,
        VirtualKey.NEXT,
        VirtualKey.END,
        VirtualKey.HOME,
        VirtualKey.LEFT,
        VirtualKey.UP,
        VirtualKey.RIGHT,
        VirtualKey.DOWN,
        VirtualKey.INSERT,
        VirtualKey.DELETE,
        VirtualKey.KEY_0,
        VirtualKey.KEY_1,
        VirtualKey.KEY_2,
        VirtualKey.KEY_3,
        VirtualKey.KEY_4,
        VirtualKey.KEY_5,
        VirtualKey.KEY_6,
        VirtualKey.KEY_7,
        VirtualKey.KEY_8,
        VirtualKey.KEY_9,
        VirtualKey.A,
        VirtualKey.B,
        VirtualKey.C,
        VirtualKey.D,
        VirtualKey.E,
        VirtualKey.F,
        VirtualKey.G,
        VirtualKey.H,
        VirtualKey.I,
        VirtualKey.J,
        VirtualKey.K,
        VirtualKey.L,
        VirtualKey.M,
        VirtualKey.N,
        VirtualKey.O,
        VirtualKey.P,
        VirtualKey.Q,
        VirtualKey.R,
        VirtualKey.S,
        VirtualKey.T,
        VirtualKey.U,
        VirtualKey.V,
        VirtualKey.W,
        VirtualKey.X,
        VirtualKey.Y,
        VirtualKey.Z,
        VirtualKey.NUMPAD0,
        VirtualKey.NUMPAD1,
        VirtualKey.NUMPAD2,
        VirtualKey.NUMPAD3,
        VirtualKey.NUMPAD4,
        VirtualKey.NUMPAD5,
        VirtualKey.NUMPAD6,
        VirtualKey.NUMPAD7,
        VirtualKey.NUMPAD8,
        VirtualKey.NUMPAD9,
        VirtualKey.MULTIPLY,
        VirtualKey.ADD,
        VirtualKey.SUBTRACT,
        VirtualKey.DECIMAL,
        VirtualKey.DIVIDE,
        VirtualKey.F1,
        VirtualKey.F2,
        VirtualKey.F3,
        VirtualKey.F4,
        VirtualKey.F5,
        VirtualKey.F6,
        VirtualKey.F7,
        VirtualKey.F8,
        VirtualKey.F9,
        VirtualKey.F10,
        VirtualKey.F11,
        VirtualKey.F12,
        VirtualKey.OEM_1,
        VirtualKey.OEM_PLUS,
        VirtualKey.OEM_COMMA,
        VirtualKey.OEM_MINUS,
        VirtualKey.OEM_PERIOD,
        VirtualKey.OEM_2,
        VirtualKey.OEM_3,
        VirtualKey.OEM_4,
        VirtualKey.OEM_5,
        VirtualKey.OEM_6,
        VirtualKey.OEM_7,
        VirtualKey.OEM_8,
        VirtualKey.OEM_102,
    };

    public enum MouseKey
    {
        // LBUTTON = 1,
        MBUTTON = 2,
        // RBUTTON = 4,
        XBUTTON1 = 8,
        XBUTTON2 = 16
    }
}
