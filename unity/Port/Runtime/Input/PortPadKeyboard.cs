// Always-on keyboard aliases for the PS4 pad, so the game can be played from a laptop keyboard.
//
// The game reads the pad as KeyCode.Joystick1ButtonN / JoystickButtonN (PS4 numbering: 0 Cross,
// 1 Circle, 2 Square, 3 Triangle, 4 L1, 5 R1, 6 touch pad, 7 Options) and the named axes
// PS3DPadVert/PS3DPadHoriz/PS4LeftStickX/PS4LeftStickY (+1 is up / right).
//
// Buttons: the global Input shim (PortDebug/InputShim.cs) asks this class for the aliases of a
// pad button and treats the button and its alias keys as one combined button. Every unqualified
// Input.GetKey*(KeyCode) in Assembly-CSharp therefore sees Return as Cross, and so on. GetButton*
// and GetAxis*(name) are not aliased here, so uGUI Submit/Cancel keep reading the InputManager
// table and still fire once per press.
//
// Axes: the arrow keys are aliased only where MultiControllerInput.GetAxis asks for them
// (the Home menu, Credits). The shim does not apply them, because the game code that reads
// PS3DPad* through Input directly (ScenarioBrowser, QuestionBrowser, DpadHelper,
// GUIDebugMenuBase) already pairs the axis with Input.GetKey*(arrow); aliasing the axis there
// too would step twice per key press.
//
// Letter keys: H and P are not read by the game itself (only NGUI's key-name table lists them).
// K and L are the game's own L1/R1 keys on the home bar (Menu); aliasing them to buttons 4/5
// also makes them reach code that only reads the pad button (ScenarioBrowser, PlayMaker
// InputEventGeneratorPM). Where the game checks both, one press still triggers one action,
// because both checks see the same key-up frame. Full map: PortDebug/README.md.
//
// Combined button semantics follow the legacy input manager: held while any source is held,
// down on the frame the first source goes down, up on the frame the last source is released.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortPad
{
    public static class PortPadKeyboard
    {
        // Set false to turn the aliases off (they are on by default, with or without -portKeys).
        public static bool Enabled = true;

        const int ButtonCount = 20;

        static readonly KeyCode[][] s_ButtonKeys = new KeyCode[ButtonCount][];

        sealed class AxisKeys
        {
            public KeyCode[] positive;
            public KeyCode[] negative;
        }

        static readonly Dictionary<string, AxisKeys> s_AxisKeys = new Dictionary<string, AxisKeys>(StringComparer.Ordinal);

        static PortPadKeyboard()
        {
            s_ButtonKeys[0] = new[] { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space }; // Cross
            s_ButtonKeys[1] = new[] { KeyCode.Escape, KeyCode.Backspace };                  // Circle
            s_ButtonKeys[2] = new[] { KeyCode.H };                                          // Square (Help)
            s_ButtonKeys[3] = new[] { KeyCode.P };                                          // Triangle (Play Online)
            s_ButtonKeys[4] = new[] { KeyCode.K };                                          // L1 (Game Length)
            s_ButtonKeys[5] = new[] { KeyCode.L };                                          // R1 (Question Set)
            s_ButtonKeys[7] = new[] { KeyCode.O, KeyCode.Tab };                             // Options

            var vertical = new AxisKeys { positive = new[] { KeyCode.UpArrow }, negative = new[] { KeyCode.DownArrow } };
            var horizontal = new AxisKeys { positive = new[] { KeyCode.RightArrow }, negative = new[] { KeyCode.LeftArrow } };
            s_AxisKeys["PS3DPadVert"] = vertical;
            s_AxisKeys["PS4LeftStickY"] = vertical;
            s_AxisKeys["PS3DPadHoriz"] = horizontal;
            s_AxisKeys["PS4LeftStickX"] = horizontal;
        }

        // Pad button index for JoystickButtonN and Joystick1ButtonN, otherwise -1.
        public static int PadButton(KeyCode key)
        {
            int k = (int)key;
            if (k >= (int)KeyCode.JoystickButton0 && k <= (int)KeyCode.JoystickButton19)
                return k - (int)KeyCode.JoystickButton0;
            if (k >= (int)KeyCode.Joystick1Button0 && k <= (int)KeyCode.Joystick1Button19)
                return k - (int)KeyCode.Joystick1Button0;
            return -1;
        }

        // Keyboard keys that also count as this pad button, or null.
        public static KeyCode[] Aliases(KeyCode key)
        {
            if (!Enabled) return null;
            PortKeyHints.Ensure(); // the game's scene loads destroy it; the game polls input every frame
            int b = PadButton(key);
            return b < 0 ? null : s_ButtonKeys[b];
        }

        public static bool HasAliases(KeyCode key)
        {
            return Aliases(key) != null;
        }

        // ---- combined button state ----------------------------------------------------------
        // held/down/up report the raw state of a single key (the shim passes UnityEngine.Input
        // merged with scripted input).

        public static bool Held(KeyCode key, Func<KeyCode, bool> held)
        {
            if (held(key)) return true;
            KeyCode[] a = Aliases(key);
            if (a == null) return false;
            for (int i = 0; i < a.Length; i++)
                if (held(a[i])) return true;
            return false;
        }

        public static bool Down(KeyCode key, Func<KeyCode, bool> held, Func<KeyCode, bool> down, Func<KeyCode, bool> up)
        {
            KeyCode[] a = Aliases(key);
            if (a == null) return down(key);
            bool anyDown = false;
            for (int i = -1; i < a.Length; i++)
            {
                KeyCode k = i < 0 ? key : a[i];
                bool d = down(k);
                // a source that was already held before this frame (or was released this
                // frame) means the combined button was held last frame: no new press
                if ((held(k) && !d) || up(k)) return false;
                anyDown |= d;
            }
            return anyDown;
        }

        public static bool Up(KeyCode key, Func<KeyCode, bool> held, Func<KeyCode, bool> up)
        {
            KeyCode[] a = Aliases(key);
            if (a == null) return up(key);
            bool anyUp = false;
            for (int i = -1; i < a.Length; i++)
            {
                KeyCode k = i < 0 ? key : a[i];
                if (held(k)) return false;
                anyUp |= up(k);
            }
            return anyUp;
        }

        // ---- axis keys (consulted by MultiControllerInput.GetAxis) --------------------------

        // true when a key aliased to the positive (or negative) end of this axis is held
        public static bool AxisHeld(string axis, bool positive, Func<KeyCode, bool> held)
        {
            if (!Enabled || axis == null) return false;
            AxisKeys ak;
            if (!s_AxisKeys.TryGetValue(axis, out ak)) return false;
            KeyCode[] keys = positive ? ak.positive : ak.negative;
            for (int i = 0; i < keys.Length; i++)
                if (held(keys[i])) return true;
            return false;
        }
    }
}
