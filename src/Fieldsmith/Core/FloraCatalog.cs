using System;
using System.Collections.Generic;

namespace Fieldsmith
{
    // One cost of a wild plant: an item prefab name and the amount. A null item means the item the plant itself
    // gives when picked (raspberries for a raspberry bush).
    internal readonly struct FloraCost
    {
        internal readonly string Item;
        internal readonly int Amount;

        internal FloraCost(string item, int amount)
        {
            Item = item;
            Amount = amount;
        }
    }

    // How wild plants are priced ([Plants] Costs).
    internal enum FloraCostMode
    {
        // A pickable costs what one pick gives (one mushroom, three fiddleheads), like a seed: the first pick
        // pays it back.
        Seed,
        // Plant Everything's default costs (five mushrooms, fifteen fiddleheads).
        PlantEverything,
    }

    // A wild plant of the game that Fieldsmith makes plantable with the Cultivator. The game object is the
    // vanilla prefab itself, so players without the mod see it as an ordinary bush, mushroom or stone.
    internal sealed class FloraEntry
    {
        internal readonly string Prefab;
        internal readonly string Name;
        internal readonly string Description;
        internal readonly FloraCost[] Costs;
        // Whether the hammer may remove a placed one (never a wild one), and whether that returns the cost.
        internal readonly bool Removable;
        internal readonly bool Recover;
        // Ground only; false lets vanilla place it on any surface (vines).
        internal readonly bool Grounded;
        // Items per pick of a placed one; 0 keeps the game's value.
        internal readonly int Yield;
        // A placed one gives no extra drops when picked.
        internal readonly bool NoExtraDrops;
        // Local snap points (x, y, z) added to the prefab so neighbours line up; null for none.
        internal readonly double[][] SnapPoints;
        // Regrow time in minutes for a placed one that never grows back in the game (the stone); 0 keeps the
        // game's behaviour.
        internal readonly double RegrowMinutes;

        internal FloraEntry(string prefab, string name, string description, FloraCost[] costs, bool removable,
            bool recover = false, bool grounded = true, int yield = 0, bool noExtraDrops = false, double[][] snapPoints = null,
            double regrowMinutes = 0)
        {
            Prefab = prefab;
            Name = name;
            Description = description;
            Costs = costs;
            Removable = removable;
            Recover = recover;
            Grounded = grounded;
            Yield = yield;
            NoExtraDrops = noExtraDrops;
            SnapPoints = snapPoints;
            RegrowMinutes = regrowMinutes;
        }
    }

    // The wild plants and their costs, in build menu order. The selection and the costs follow the default
    // configuration of Plant Everything 1.21.3 (Advize), so players switching from it pay the same; Plant
    // Everything's own saplings are left out because they are new objects every player would need. Respawn
    // times and yields not listed here are already the game's values.
    internal static class FloraCatalog
    {
        private static FloraCost[] Own(int amount) => new[] { new FloraCost(null, amount) };

        private static FloraCost[] Of(string item, int amount) => new[] { new FloraCost(item, amount) };

        internal static readonly IReadOnlyList<FloraEntry> All = new[]
        {
            new FloraEntry("RaspberryBush", "Raspberry bush", "Grows raspberries again and again.", Own(5), true),
            new FloraEntry("BlueberryBush", "Blueberry bush", "Grows blueberries again and again.", Own(5), true),
            new FloraEntry("CloudberryBush", "Cloudberry bush", "Grows cloudberries again and again.", Own(5), true),
            new FloraEntry("LingonberryBush", "Lingonberry bush", "Grows lingonberries again and again.", Own(5), true),
            new FloraEntry("Pickable_Mushroom", "Mushrooms", "Mushrooms grow back after picking.", Own(5), true),
            new FloraEntry("Pickable_Mushroom_yellow", "Yellow mushrooms", "Yellow mushrooms grow back after picking.", Own(5), true),
            new FloraEntry("Pickable_Mushroom_blue", "Blue mushrooms", "Blue mushrooms grow back after picking.", Own(5), true),
            new FloraEntry("Pickable_Thistle", "Thistle", "Thistle grows back after picking.", Own(5), true),
            new FloraEntry("Pickable_Dandelion", "Dandelion", "Dandelions grow back after picking.", Own(5), true),
            new FloraEntry("Pickable_SmokePuff", "Smoke puffs", "Smoke puffs grow back after picking.", Own(5), true),
            new FloraEntry("Pickable_Fiddlehead", "Fiddlehead", "Fiddleheads grow back after picking, three at a time.", Own(15), true,
                yield: 3, noExtraDrops: true),
            new FloraEntry("Beech_small1", "Small beech", "A small beech tree.", Of("BeechSeeds", 1), false),
            new FloraEntry("FirTree_small", "Small fir", "A small fir tree.", Of("FirCone", 1), false),
            new FloraEntry("FirTree_small_dead", "Small dead fir", "A small dead fir tree.", Of("FirCone", 1), false),
            new FloraEntry("Bush01", "Bush", "A small bush.", Of("Wood", 2), false),
            new FloraEntry("Bush01_heath", "Heath bush", "A small heath bush.", Of("Wood", 2), false),
            new FloraEntry("Bush02_en", "Plains bush", "A bush of the Plains.", Of("Wood", 3), false),
            new FloraEntry("shrub_2", "Shrub", "A small shrub.", Of("Wood", 2), false),
            new FloraEntry("shrub_2_heath", "Heath shrub", "A small heath shrub.", Of("Wood", 2), false),
            new FloraEntry("YggaShoot_small1", "Small ygga shoot", "A small ygga shoot.",
                new[] { new FloraCost("YggdrasilWood", 1), new FloraCost("Wood", 2) }, false),
            new FloraEntry("vines", "Vines", "Decorative vines; they snap to each other.", Of("Wood", 2), true, recover: true, grounded: false,
                snapPoints: new[] { new[] { 1.0, 0.5, 0.0 }, new[] { -1.0, 0.5, 0.0 }, new[] { 1.0, -1.0, 0.0 }, new[] { -1.0, -1.0, 0.0 } }),
            new FloraEntry("FernAshlands", "Ashlands fern", "A fern of the Ashlands.", Of("Wood", 2), false),
            new FloraEntry("Pickable_Branch", "Branch", "A branch that comes back after picking.", Own(5), true),
            new FloraEntry("Pickable_Stone", "Stone", "A stone that comes back after picking.", Own(1), true, regrowMinutes: FlintRegrowMinutes),
            new FloraEntry("Pickable_Flint", "Flint", "Flint that comes back after picking.", Own(5), true),
        };

        // Pickable_Flint's respawn time in the game (docs/evidence/flora.json); a planted stone grows back like it.
        internal const double FlintRegrowMinutes = 240;

        // The amount of one cost. Costs in the plant's own pick item follow the mode: one pick's worth (the
        // catalog yield, else the game's) for Seed, the catalog amount for PlantEverything. Other costs (wood for
        // bushes, seeds for small trees) are the catalog amount in both modes.
        internal static int CostAmount(FloraEntry entry, FloraCost cost, int gameYield, FloraCostMode mode)
        {
            if (cost.Item != null || mode != FloraCostMode.Seed) return cost.Amount;
            return Math.Max(1, entry.Yield > 0 ? entry.Yield : gameYield);
        }

        internal static bool TryGet(string prefab, out FloraEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(prefab)) return false;
            foreach (FloraEntry candidate in All)
            {
                if (!string.Equals(candidate.Prefab, prefab, StringComparison.Ordinal)) continue;
                entry = candidate;
                return true;
            }
            return false;
        }
    }
}
