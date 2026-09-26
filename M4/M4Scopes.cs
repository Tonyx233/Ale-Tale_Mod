using System;

namespace TonyMods
{
    // Interchangeable M4 optics. The fitted optic is stored in the rifle Item's metaInt (low byte):
    // native code only uses metaInt for lottery tickets, saves it (SavedCont / SavedColl) and syncs it
    // in Item.NetworkSerialize. 0 is the factory default (ACOG) so 0.14.x rifles keep their scope.
    internal enum M4ScopeKind : byte { Default = 0, Iron = 1, RedDot = 2, Holo = 3, Acog = 4, Brass = 5, Sniper = 6 }
    internal enum M4Reticle : byte { None, Dot, Holo, Chevron, Crosshair, MilDot }

    internal sealed class M4ScopeProfile
    {
        public M4ScopeKind Kind;
        public float[] Stages;
        public M4Reticle Reticle;
        // Magnified optics hide the gun behind a scope overlay; the others align the real sights (ADS).
        public bool Magnified;
        public float SpreadFactor;
        // ADS: sight point in M4 model metres and the eye distance in world units.
        public float SightY, SightZ, EyeDistance;
        public string Role;
        public bool FoldedIrons;
        public string Zh, En;
    }

    internal struct M4ScopeSwap
    {
        public bool Apply;
        public M4ScopeKind Returned;
        public int NewMeta;
        public bool Split;
    }

    internal static class M4Scopes
    {
        public const ushort FirstItemId = 47932;
        public const int KindMask = 0xFF;
        public const int Count = 6;
        public static readonly M4ScopeKind[] ItemKinds = { M4ScopeKind.RedDot, M4ScopeKind.Holo, M4ScopeKind.Acog, M4ScopeKind.Brass, M4ScopeKind.Sniper };
        private static readonly M4ScopeProfile[] profiles =
        {
            null,
            new M4ScopeProfile { Kind = M4ScopeKind.Iron, Stages = new[] { 1.3f }, Reticle = M4Reticle.None, SpreadFactor = .6f, SightY = .091f, SightZ = .021f, EyeDistance = .06f, Role = "iron-up", Zh = "機械瞄具", En = "Iron sights" },
            new M4ScopeProfile { Kind = M4ScopeKind.RedDot, Stages = new[] { 1.5f }, Reticle = M4Reticle.Dot, SpreadFactor = .45f, SightY = .086f, SightZ = .08f, EyeDistance = .1f, Role = "scope-reddot", FoldedIrons = true, Zh = "紅點瞄準鏡", En = "Red dot sight" },
            new M4ScopeProfile { Kind = M4ScopeKind.Holo, Stages = new[] { 1.5f }, Reticle = M4Reticle.Holo, SpreadFactor = .45f, SightY = .085f, SightZ = .084f, EyeDistance = .11f, Role = "scope-holo", FoldedIrons = true, Zh = "全像瞄準鏡", En = "Holographic sight" },
            new M4ScopeProfile { Kind = M4ScopeKind.Acog, Stages = new[] { 4f }, Reticle = M4Reticle.Chevron, Magnified = true, SpreadFactor = .35f, Role = "scope-acog", FoldedIrons = true, Zh = "ACOG 瞄準鏡", En = "ACOG" },
            new M4ScopeProfile { Kind = M4ScopeKind.Brass, Stages = new[] { 3f, 6f }, Reticle = M4Reticle.Crosshair, Magnified = true, SpreadFactor = .35f, Role = "scope-brass", FoldedIrons = true, Zh = "黃銅瞄準鏡", En = "Brass scope" },
            new M4ScopeProfile { Kind = M4ScopeKind.Sniper, Stages = new[] { 3f, 6f, 9f }, Reticle = M4Reticle.MilDot, Magnified = true, SpreadFactor = .25f, Role = "scope-sniper", Zh = "狙擊鏡", En = "Sniper scope" },
        };

        public static M4ScopeKind Effective(int metaInt)
        {
            int kind = metaInt & KindMask;
            return kind >= 1 && kind <= Count ? (M4ScopeKind)kind : M4ScopeKind.Acog;
        }

        public static int WithKind(int metaInt, M4ScopeKind kind) { return (metaInt & ~KindMask) | (int)kind; }

        public static M4ScopeProfile Profile(M4ScopeKind kind) { return profiles[(int)Effective((int)kind)]; }

        // ACOG magnification comes from [M4] ScopeMagnification.
        public static float[] Stages(M4ScopeKind kind, int acogPower)
        {
            M4ScopeProfile p = Profile(kind);
            return p.Kind == M4ScopeKind.Acog ? new[] { (float)acogPower } : (float[])p.Stages.Clone();
        }

        public static bool HasItem(M4ScopeKind kind) { return kind >= M4ScopeKind.RedDot && kind <= M4ScopeKind.Sniper; }
        public static ushort ItemId(M4ScopeKind kind) { return HasItem(kind) ? (ushort)(FirstItemId + (kind - M4ScopeKind.RedDot)) : (ushort)0; }

        public static bool FromItem(ushort dataId, out M4ScopeKind kind)
        {
            kind = M4ScopeKind.Default;
            if (dataId < FirstItemId || dataId >= FirstItemId + ItemKinds.Length) return false;
            kind = (M4ScopeKind)(dataId - FirstItemId + (int)M4ScopeKind.RedDot);
            return true;
        }

        // Host-side decisions. Split means the rifle sits in a stack (ItemStacks): only one gets the change.
        public static M4ScopeSwap PlanAttach(int gunMeta, int gunAmount, M4ScopeKind incoming)
        {
            var swap = new M4ScopeSwap();
            M4ScopeKind current = Effective(gunMeta);
            if (!HasItem(incoming) || incoming == current || gunAmount < 1) return swap;
            swap.Apply = true; swap.Returned = HasItem(current) ? current : M4ScopeKind.Iron;
            swap.NewMeta = WithKind(gunMeta, incoming); swap.Split = gunAmount > 1;
            return swap;
        }

        public static M4ScopeSwap PlanDetach(int gunMeta, int gunAmount)
        {
            var swap = new M4ScopeSwap();
            M4ScopeKind current = Effective(gunMeta);
            if (!HasItem(current) || gunAmount < 1) return swap;
            swap.Apply = true; swap.Returned = current;
            swap.NewMeta = WithKind(gunMeta, M4ScopeKind.Iron); swap.Split = gunAmount > 1;
            return swap;
        }

        // Reticle sizes in degrees of the real sight picture (ADS draws them at screen centre).
        public const float DotMoa = 2, HoloRingMoa = 65, HoloDotMoa = 1;
        public static float MoaToPixels(float moa, float verticalFov, float screenHeight)
        {
            double radians = moa / 60.0 * Math.PI / 180;
            return (float)(Math.Tan(radians) / Math.Tan(verticalFov * Math.PI / 360) * screenHeight / 2);
        }
    }
}
