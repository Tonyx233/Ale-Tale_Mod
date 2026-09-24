using System;

namespace TonyMods
{
    internal static class ScopeMath
    {
        public static float ZoomFov(float original, float magnification)
        {
            return (float)(2 * Math.Atan(Math.Tan(original * Math.PI / 360) / magnification) * 180 / Math.PI);
        }
        public static float LookScale(float original, float zoomed)
        {
            return (float)(Math.Tan(zoomed * Math.PI / 360) / Math.Tan(original * Math.PI / 360));
        }
    }
}
