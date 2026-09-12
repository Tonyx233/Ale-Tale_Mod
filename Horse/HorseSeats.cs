using System;
using System.Globalization;

namespace TonyMods
{
    // Host-owned seat table. Empty uses a value that cannot be a connected client ID.
    public sealed class HorseSeats
    {
        public const int Capacity = 2;
        public const ulong Empty = ulong.MaxValue;
        private readonly ulong[] occupants = new ulong[Capacity];
        public HorseSeats() { Clear(); }
        public ulong this[int index] { get { return occupants[index]; } }
        public int Count { get { int count = 0; foreach (ulong id in occupants) if (id != Empty) count++; return count; } }
        public int Find(ulong id) { if (id == Empty) return -1; return Array.IndexOf(occupants, id); }
        public int Board(ulong id)
        {
            if (id == Empty) return -1;
            int seat = Find(id);
            if (seat >= 0) return seat;
            seat = Array.IndexOf(occupants, Empty);
            if (seat >= 0) occupants[seat] = id;
            return seat;
        }
        public bool TrySwitch(ulong id, int target)
        {
            int current = Find(id);
            if (current < 0 || target < 0 || target >= Capacity) return false;
            if (current == target) return true;
            if (occupants[target] != Empty) return false;
            occupants[current] = Empty; occupants[target] = id; return true;
        }
        public void Remove(ulong id) { int seat = Find(id); if (seat >= 0) occupants[seat] = Empty; }
        public void Clear() { for (int i = 0; i < Capacity; i++) occupants[i] = Empty; }
        public string Encode() { return String.Join(",", Array.ConvertAll(occupants, x => x.ToString(CultureInfo.InvariantCulture))); }
        public bool Decode(string text)
        {
            if (text == null || text.Length > 130) return false;
            string[] parts = text.Split(',');
            if (parts.Length != Capacity) return false;
            ulong[] parsed = new ulong[Capacity];
            for (int i = 0; i < Capacity; i++)
            {
                if (!UInt64.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i])) return false;
                for (int j = 0; j < i; j++) if (parsed[i] != Empty && parsed[i] == parsed[j]) return false;
            }
            Array.Copy(parsed, occupants, Capacity);
            return true;
        }
    }
}
