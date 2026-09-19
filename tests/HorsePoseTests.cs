using System;
using TonyMods;

internal static class HorsePoseTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    public static void Main()
    {
        HorseLegSolution s;
        Check(!HorsePoseMath.Solve(0, .4f, .6f, out s), "zero bone");
        Check(!HorsePoseMath.Solve(.4f, .4f, 0, out s), "zero target");
        Check(!HorsePoseMath.Solve(float.NaN, .4f, .6f, out s), "NaN bone");
        Check(!HorsePoseMath.Solve(.4f, .4f, float.PositiveInfinity, out s), "infinite target");
        // Male and female imported limb lengths, plus near/far unreachable targets.
        foreach (float a in new[] { .481f, .461f, .2f, .6f })
        foreach (float b in new[] { .422f, .453f, .3f, .6f })
        for (int i = 1; i <= 200; i++)
        {
            Check(HorsePoseMath.Solve(a, b, i * .01f, out s), "valid target");
            double upper = Math.Sqrt(s.Along * s.Along + s.Height * s.Height);
            double lower = Math.Sqrt((s.Distance - s.Along) * (s.Distance - s.Along) + s.Height * s.Height);
            Check(Math.Abs(upper - a) < .00001, "upper length preserved");
            Check(Math.Abs(lower - b) < .00001, "lower length preserved");
            Check(s.Height > 0 && s.Distance < a + b, "no knee singularity");
        }
        Console.WriteLine("Horse pose: " + checks + " assertions passed.");
    }
}
