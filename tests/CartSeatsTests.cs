using System;
using TonyMods;
class CartSeatsTests
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    static int Main()
    {
        CartSeats seats = new CartSeats();
        Check(seats.Count == 0, "initially empty");
        for (ulong i = 0; i < 6; i++) Check(seats.Board(i) == (int)i, "sequential seat allocation");
        Check(seats.Count == 6, "six occupied");
        Check(seats.Board(6) == -1, "seventh denied");
        Check(seats.Board(3) == 3 && seats.Count == 6, "duplicate request is idempotent");
        seats.Remove(0);
        Check(seats.Find(1) == 1, "driver leaving does not reassign passengers");
        Check(seats.Board(99) == 0, "next rider claims empty driver seat");
        seats.Remove(4);
        Check(seats.Board(7) == 4, "disconnected seat reused");
        string snapshot = seats.Encode();
        CartSeats client = new CartSeats();
        Check(client.Decode(snapshot) && client.Encode() == snapshot, "late join snapshot round trip");
        foreach (string bad in new[] { null, "", "0,1,2", "0,0,2,3,4,5", "x,1,2,3,4,5", "-1,1,2,3,4,5", "0,1,2,3,4,18446744073709551616" })
        {
            Check(!client.Decode(bad), "invalid snapshot rejected");
            Check(client.Encode() == snapshot, "invalid snapshot does not partially mutate seats");
        }
        Check(seats.Board(CartSeats.Empty) == -1, "empty sentinel cannot board");
        seats.Clear();
        Check(seats.Count == 0 && client.Decode(seats.Encode()) && client.Count == 0, "reset synchronized");
        Console.WriteLine("PASS: six-seat allocation, capacity, duplicate requests, disconnect, late join and malformed snapshots");
        return 0;
    }
}
