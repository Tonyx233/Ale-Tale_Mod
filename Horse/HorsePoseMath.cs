using System;

namespace TonyMods
{
    public struct HorseLegSolution
    {
        public float Distance, Along, Height;
    }
    public static class HorsePoseMath
    {
        // Triangle solution independent of the imported skeleton's bone axes.
        public static bool Solve(float upper, float lower, float targetDistance, out HorseLegSolution result)
        {
            result = new HorseLegSolution();
            if (!Finite(upper) || !Finite(lower) || !Finite(targetDistance) ||
                upper < .001f || lower < .001f || targetDistance < .0001f) return false;
            double a = upper, b = lower;
            double distance = Math.Max(Math.Abs(a - b) + .0001, Math.Min(a + b - .0001, targetDistance));
            double along = (a * a - b * b + distance * distance) / (2 * distance);
            result.Distance = (float)distance;
            result.Along = (float)along;
            result.Height = (float)Math.Sqrt(Math.Max(0, a * a - along * along));
            return true;
        }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
}
