// Franklin works in American imperial units (ruling 2026-11-14): every value he
// sees or tunes is in inches / feet / pounds / mph. Unity's physics runs in
// metres, kilograms and seconds, so conversion happens only at this boundary.
using UnityEngine;

namespace OKT
{
    public static class U
    {
        public const float In = 0.0254f;          // metres per inch
        public const float Ft = 0.3048f;          // metres per foot
        public const float Lb = 0.45359237f;      // kg per pound
        public const float Mph = 0.44704f;        // m/s per mph
        public const float G = 9.81f;             // m/s² per g
        public const float LbfPerIn = 4.4482216f / 0.0254f; // N/m per lbf/in

        public static string FtS(float m, int dp = 1) => (m / Ft).ToString("F" + dp) + " ft";
        public static string InS(float m, int dp = 1) => (m / In).ToString("F" + dp) + " in";
        public static string MphS(float ms, int dp = 1) => (ms / Mph).ToString("F" + dp) + " mph";
        public static string LbS(float kg, int dp = 0) => (kg / Lb).ToString("F" + dp) + " lb";
        public static string LbfS(float n, int dp = 0) => (n / 4.4482216f).ToString("F" + dp) + " lbf";
    }
}
