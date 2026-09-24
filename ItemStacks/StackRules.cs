using System;

namespace TonyMods
{
    // Capacity is deliberately separate from ItemData.maxStack: vanilla uses that
    // field for purchase quantities, loot generation, charging and price math.
    internal static class StackRules
    {
        internal const ushort Limit = 9999;

        internal static bool Compatible(Item a, Item b)
        {
            return a.dataId == b.dataId && a.rarity == b.rarity &&
                a.charge == b.charge && a.durability == b.durability &&
                a.repairCount == b.repairCount && a.metaInt == b.metaInt &&
                a.weaponDamage == b.weaponDamage &&
                a.weaponDamageDeviation == b.weaponDamageDeviation &&
                a.weaponChargedHitDamageMul == b.weaponChargedHitDamageMul;
        }

        internal static bool IsFood(ItemData.Type type)
        {
            return (int)type == 4 || (int)type == 5 || (int)type == 6;
        }

        internal static long SaleValue(Item item, ItemData data)
        {
            if (data == null || item.amount == 0) return 0;
            double price = data.price;
            if (data.hasRarity)
            {
                if (data.shopRarityPrices != null && data.shopRarityPrices.ContainsKey(item.rarity))
                    price = data.shopRarityPrices[item.rarity];
                else
                {
                    double[] multipliers = { 1, 1.2, 1.6, 2.2, 3 };
                    int rarity = (int)item.rarity;
                    if (rarity >= 0 && rarity < multipliers.Length) price *= multipliers[rarity];
                }
            }
            if ((int)data.type == 10 && data.durability > 0)
                price /= 1 + item.repairCount;
            else if (data.maxCharge > 0 && data.containerClean != null)
                price = data.containerClean.price + price * item.charge / data.maxCharge;
            else if (data.durability > 0 && item.durability < data.durability)
                price *= (double)item.durability / data.durability;
            else if (data.maxStack > 1 && !IsFood(data.type))
                price /= data.maxStack;
            // Food retains the old module's per-serving rounding.
            if (IsFood(data.type)) price = Math.Floor(price);
            return Math.Max(0L, (long)Math.Floor(price * item.amount));
        }

        internal static ushort SaleBatch(Item item, ItemData data)
        {
            int low = 0, high = item.amount;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                Item probe = item; probe.amount = (ushort)mid;
                if (SaleValue(probe, data) <= ushort.MaxValue) low = mid;
                else high = mid - 1;
            }
            return (ushort)low;
        }
    }
}
