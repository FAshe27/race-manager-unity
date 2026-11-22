// DEBUG test-drive (Franklin 11-22: controller support to drive a kart, "debug tool"). Off by default; the HUD's
// "Drive" toggle hands the camera's kart to a gamepad or the keyboard. The kart runs the same physics as everyone
// (tires, axle, engine); only the driver's three inputs -- steering, gas, brake -- come from you. The AI still drives
// it under caution, on the pace laps and in staging, and hands it back at the green.
//   Pad: left stick steer, right trigger gas, left trigger brake.   Keys: Left/Right (or A/D) steer, Up/W gas, Down/S brake.
using UnityEngine;
using UnityEngine.InputSystem;

namespace OKT
{
    public static class HumanDriver
    {
        public static KartAI Driven;                  // null = nobody (the AI drives every kart)
        public static float SteerRateDegPerSec = 140f; // how fast the front wheels follow the stick [TEST VALUE]
        public static float StickDeadzone = 0.08f;
        public static string LastDevice = "";
        static float steerNow;

        /// <summary>Called by the driven kart's AI at the end of its step, racing only: replace its inputs with yours.</summary>
        public static void Apply(KartAI ai, KartPhysics k, float dt)
        {
            float st = 0f, gas = 0f, brk = 0f;
            var pad = Gamepad.current; var kb = Keyboard.current;
            if (pad != null)
            {
                float x = pad.leftStick.x.ReadValue();
                st = Mathf.Abs(x) < StickDeadzone ? 0f : Mathf.Sign(x) * (Mathf.Abs(x) - StickDeadzone) / (1f - StickDeadzone);
                gas = pad.rightTrigger.ReadValue(); brk = pad.leftTrigger.ReadValue();
                LastDevice = pad.displayName;
            }
            if (kb != null)
            {
                float ks = (kb.rightArrowKey.isPressed || kb.dKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed || kb.aKey.isPressed ? 1f : 0f);
                if (ks != 0f) { st = ks; LastDevice = "Keyboard"; }
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) { gas = 1f; LastDevice = "Keyboard"; }
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) { brk = 1f; LastDevice = "Keyboard"; }
            }
            // Full stick = full lock when slow, less at speed (a real kart takes a few degrees in a corner; the stick's
            // travel is short) -- squared stick for fine control near centre.
            float v = Mathf.Abs(k.ForwardSpeed);
            float maxDeg = Mathf.Lerp(k.setup.maxSteerDeg, 10f, Mathf.Clamp01(v / (30f * U.Mph)));
            float want = Mathf.Sign(st) * st * st * maxDeg;
            steerNow = Mathf.MoveTowards(steerNow, want, SteerRateDegPerSec * dt);
            k.steerDeg = steerNow;
            k.throttleCmd = Mathf.Clamp01(gas); k.brakeCmd = Mathf.Clamp01(brk);
            k.lockBrakes = false;
        }
    }
}
