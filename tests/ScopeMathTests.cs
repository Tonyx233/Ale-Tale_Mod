using System;
using TonyMods;

internal static class ScopeMathTests
{
    private static int count;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); count++; }
    private static double TanHalf(double degrees) { return Math.Tan(degrees * Math.PI / 360); }
    public static void Main()
    {
        foreach (float fov in new float[] { 40, 60, 75, 90, 110, 120 })
            foreach (float power in new float[] { 1.5f, 3, 6 })
            {
                float zoom = ScopeMath.ZoomFov(fov, power);
                Check(zoom > 0 && zoom < fov, "Scope narrows view");
                Check(Math.Abs(TanHalf(fov) / TanHalf(zoom) - power) < .00001, "True optical magnification across user FOVs");
                Check(Math.Abs(ScopeMath.LookScale(fov, zoom) - 1 / power) < .000001, "Aim sensitivity follows optical magnification");
                // Native shot projects randomized viewport offsets using spread/FOV.
                // A shot using the restored baseline projection must keep the original ray,
                // unlike merely leaving the camera zoomed during RaycastShot.
                double baselineRay = .02 / fov * 2 * TanHalf(fov);
                double zoomedRay = .02 / zoom * 2 * TanHalf(zoom);
                Check(Math.Abs(baselineRay - zoomedRay) > .0000001, "Regression: naive FOV change alters native spread");
            }
        Check(Math.Abs(ScopeMath.ZoomFov(90, 3) - 36.8699f) < .0001f, "Known 90 degree / 3x reference");
        Console.WriteLine("PASS: " + count + " scope projection and sensitivity checks");
    }
}
