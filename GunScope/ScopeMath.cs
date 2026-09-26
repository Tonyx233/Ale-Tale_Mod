using System;

namespace TonyMods
{
    // Aim opens the first stage, each press advances, and a press on the last stage closes.
    internal sealed class ScopeCycle
    {
        private readonly int[] stages;
        private int index = -1;
        public ScopeCycle() : this(3, 6) { }
        public ScopeCycle(params int[] stages)
        {
            if (stages == null || stages.Length == 0) throw new ArgumentException("Scope needs at least one stage");
            this.stages = (int[])stages.Clone();
        }
        public int Magnification { get { return index < 0 ? 1 : stages[index]; } }
        public bool IsActive { get { return index >= 0; } }
        public bool IsLastStage { get { return index == stages.Length - 1; } }
        public void Advance() { index = index + 1 >= stages.Length ? -1 : index + 1; }
        public void Reset() { index = -1; }
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
