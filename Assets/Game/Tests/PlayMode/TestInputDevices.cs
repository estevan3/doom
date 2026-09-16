using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// Owns the synthetic Keyboard/Mouse that PlayMode tests use to drive the real
    /// New Input System bindings (move / look / fire / jump / sprint / weapon1-4).
    ///
    /// It guarantees exactly ONE synthetic pair exists per editor session.
    /// InputSystem.AddDevice devices survive domain reloads, so naive
    /// "add a device in [UnitySetUp]" leaks a new pair on every PlayMode test run:
    /// the game's actions keep binding to the first devices while later runs inject
    /// into later ones, silently killing every input-driven assertion from run two
    /// onwards. Removing the leaked duplicates (keeping the oldest pair) restores
    /// deterministic input across repeated runs.
    /// </summary>
    public static class TestInputDevices
    {
        static Keyboard s_Keyboard;
        static Mouse s_Mouse;

        public static Keyboard Keyboard => s_Keyboard;
        public static Mouse Mouse => s_Mouse;

        public static void EnsureDevices()
        {
            Keyboard kb = null;
            Mouse ms = null;
            var extras = new List<InputDevice>();

            foreach (InputDevice d in InputSystem.devices)
            {
                if (d is Keyboard && d.name.StartsWith("TestKeyboard"))
                {
                    if (kb == null) kb = (Keyboard)d;
                    else extras.Add(d);
                }
                else if (d is Mouse && d.name.StartsWith("TestMouse"))
                {
                    if (ms == null) ms = (Mouse)d;
                    else extras.Add(d);
                }
            }

            if (kb == null) kb = InputSystem.AddDevice<Keyboard>("TestKeyboard");
            if (ms == null) ms = InputSystem.AddDevice<Mouse>("TestMouse");

            foreach (InputDevice d in extras)
                InputSystem.RemoveDevice(d);

            s_Keyboard = kb;
            s_Mouse = ms;
        }

        public static void PressKeys(params Key[] keys)
        {
            EnsureDevices();
            var state = new KeyboardState();
            for (int i = 0; i < keys.Length; i++)
                state.Set(keys[i], true);
            InputSystem.QueueStateEvent(s_Keyboard, state);
        }

        public static void ReleaseAllKeys()
        {
            EnsureDevices();
            InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState());
        }

        public static void SetFireButton(bool pressed)
        {
            EnsureDevices();
            MouseState state = new MouseState().WithButton(MouseButton.Left, pressed);
            InputSystem.QueueStateEvent(s_Mouse, state);
        }

        public static void SetMouseDelta(Vector2 delta)
        {
            EnsureDevices();
            InputSystem.QueueStateEvent(s_Mouse, new MouseState { delta = delta });
        }
    }
}