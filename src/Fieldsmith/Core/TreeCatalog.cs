using System.Collections.Generic;

namespace Fieldsmith
{
    // The game's tree saplings in the Cultivator's build menu (1.0.20 bundle, docs/evidence/saplings.json). Each
    // costs one seed or cone in the game; with [Plants] TreesCostWood they, and the small trees of FloraCatalog
    // (FloraEntry.Tree), cost WoodAmount of WoodItem instead.
    internal static class TreeCatalog
    {
        internal const string WoodItem = "Wood";
        internal const int WoodAmount = 1;

        internal static readonly IReadOnlyList<string> Saplings = new[]
        {
            "Beech_Sapling", "Birch_Sapling", "Oak_Sapling", "FirTree_Sapling", "PineTree_Sapling", "FirTree_big_Sapling",
        };
    }
}
