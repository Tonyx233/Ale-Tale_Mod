using System;
namespace TonyMods
{
    // Pure pose functions shared by runtime tests, with blended walk / gallop timing.
    public static class HorseGait
    {
        public static float Offset(int leg, float run)
        {
            double[] walk = { 0, Math.PI, Math.PI * 1.5, Math.PI * .5 };
            double[] gallop = { 0, .6, Math.PI, Math.PI + .6 };
            return (float)(walk[leg] * (1-run) + gallop[leg] * run);
        }
        public static float Offset(int leg, float run, int legCount)
        {
            if (legCount == 4) return Offset(leg, run);
            if (legCount != 10 || leg < 0 || leg >= legCount) throw new ArgumentOutOfRangeException("leg");
            // A travelling wave keeps adjacent pairs from swinging together.
            double pair = (leg / 2) * Math.PI * .4;
            double side = (leg % 2) * (Math.PI * (1-run) + .6 * run);
            return (float)(pair + side);
        }
        public static float Upper(double phase, float weight, float run)
        { return (float)Math.Sin(phase) * (23 + 15 * run) * weight; }
        public static float Lower(double phase, float weight, float run)
        { return (float)Math.Max(0, Math.Sin(phase + .65)) * (35 + 25 * run) * weight; }
    }
}
