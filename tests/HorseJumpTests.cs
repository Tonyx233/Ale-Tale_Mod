using System;
using System.Reflection;

partial class TavernHorse
{
    private static TavernHorse Active, Nearest;
    private sealed class Setting { public bool Value = true; }
    private Setting enabledSetting = new Setting();
    private int localSeat = -1, consumeFrame = -1;
    private Setting mountKey = new Setting();
    private static bool input = true;
    private static bool CanInput() { return input; }
    private bool NearCart() { return true; }
    private static class Time { public static int frameCount = 1; }
    private static class Input { public static bool GetKey(bool key) { return false; } }
    public static void GetJumpInputDown() { }
    public static void GetJumpInputHeld() { }
    public static void GetDashInputDown() { }
    public static void GetUseInputDown() { }
    private static int checks;
    private float fallSpeed;
    private string SceneName = "field";
    private static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "field" }; } }
    private struct Scene { public string name; }
    private struct Point { public float y; }
    private struct RaycastHit { public Point point; }
    private Point vehiclePosition;
    private int[] seats = { 0, 1 };
    private bool hasDriver;
    private bool Player(int seat) { return hasDriver; }
    private static bool Alive(bool player) { return player; }
    private static class Mathf
    {
        public static float Min(float a, float b) { return Math.Min(a,b); }
        public static float Clamp(float x, float a, float b) { return Math.Max(a, Math.Min(x,b)); }
    }
    private bool GroundBelow(float distance, out RaycastHit ground)
    {
        ground = new RaycastHit { point = new Point { y = 0 } };
        return vehiclePosition.y <= distance;
    }
    private static void Assert(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static void Check(string method, bool expected)
    {
        bool result = true;
        bool pass = FilterAction(typeof(TavernHorse).GetMethod(method), ref result);
        if (pass != expected || (!pass && result)) throw new Exception(method + " unexpected input policy");
        checks++;
    }
    public static void Main()
    {
        foreach (string jump in new[] { "GetJumpInputDown", "GetJumpInputHeld" })
        {
            Active = Nearest = null; Check(jump, true);
            Nearest = new TavernHorse(); Check(jump, true);
            Active = new TavernHorse { localSeat = 0 }; input = true; Check(jump, true);
            input = false; Check(jump, false);
            Active.localSeat = 1; input = true; Check(jump, false);
            input = false; Check(jump, false);
            Active.enabledSetting.Value = false; Check(jump, true);
        }
        Active = new TavernHorse { localSeat = 0 }; input = true;
        Check("GetDashInputDown", false); Check("GetUseInputDown", false);
        Active.localSeat = 1; Check("GetDashInputDown", false);
        Active.localSeat = -1; Check("GetDashInputDown", true);
        var horse = new TavernHorse { vehiclePosition = new Point { y = 2 } };
        horse.hasDriver = true; horse.SettleWithoutDriver(.1f);
        Assert(horse.vehiclePosition.y == 2, "driver keeps native vertical movement");
        horse.hasDriver = false; horse.SceneName = "other"; horse.SettleWithoutDriver(.1f);
        Assert(horse.vehiclePosition.y == 2, "other scene untouched");
        horse.SceneName = "field"; horse.SettleWithoutDriver(0);
        Assert(horse.vehiclePosition.y == 2, "paused gravity");
        for (int i=0;i<50;i++)
        {
            float previous=horse.vehiclePosition.y;
            horse.SettleWithoutDriver(.02f);
            Assert(horse.vehiclePosition.y <= previous && horse.vehiclePosition.y >= 0, "fall without upward snap or tunneling");
        }
        Assert(Math.Abs(horse.vehiclePosition.y-.06f)<.0001f, "lands at clearance");
        Assert(horse.fallSpeed==0, "landing resets speed");
        horse.vehiclePosition.y=10; horse.SettleWithoutDriver(5);
        Assert(horse.vehiclePosition.y>=9.8f, "long frame bounded");
        Console.WriteLine("PASS: " + checks + " production jump/input policy cases");
    }
}
