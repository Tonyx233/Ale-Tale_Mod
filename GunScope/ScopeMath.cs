using System;

namespace TonyMods
{
    internal sealed class ScopeCycle
    {
        public int Magnification { get; private set; }
        public bool IsActive { get { return Magnification > 1; } }
        public ScopeCycle() { Reset(); }
        public void Advance() { Magnification = Magnification == 1 ? 3 : Magnification == 3 ? 6 : 1; }
        public void Reset() { Magnification = 1; }
    }

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
