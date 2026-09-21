using System;

namespace TonyMods
{
    // Zero is deliberately the original horse for saves written before variants existed.
    public static class HorseVariant
    {
        public const int Original = 0, Extended = 1;
        public const float SeatSpacing = .68f, Extension = SeatSpacing * 3;
        public static bool Valid(int variant) { return variant == Original || variant == Extended; }
        public static int Seats(int variant)
        {
            if (!Valid(variant)) throw new ArgumentOutOfRangeException("variant");
            return variant == Extended ? 5 : 2;
        }
        public static float SeatZ(int seat) { return .20f - SeatSpacing * seat; }
        public static float RearExtension(int variant) { return variant == Extended ? Extension : 0; }
    }
}
