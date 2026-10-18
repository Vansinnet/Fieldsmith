using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Fieldsmith
{
    // [Plants] TreesCostWood: the game's tree saplings (TreeCatalog) cost wood instead of their seed or cone. Only
    // the Piece.m_resources of the sapling prefabs on this client change; a placed sapling is the vanilla object.
    // The original requirements come back when the setting is turned off or the mod unloads. Fieldsmith's small
    // trees (FloraEntry.Tree) are priced by Flora with the same setting.
    internal static class Trees
    {
        private sealed class Change
        {
            internal Piece Piece;
            internal Piece.Requirement[] Original;
            internal Piece.Requirement[] Wood;
        }

        private static readonly Dictionary<string, Change> changes = new Dictionary<string, Change>(StringComparer.Ordinal);
        private static ManualLogSource log;
        private static ConfigEntry<bool> setting;
        // Set once the game hooks have run, so a setting change never touches prefabs before the patches apply.
        private static bool hooked;
        private static bool missingLogged;

        internal static bool CostWood => setting != null && setting.Value;

        internal static void Initialize(ManualLogSource logger, ConfigEntry<bool> costWood)
        {
            log = logger;
            setting = costWood;
            costWood.SettingChanged += (_, __) =>
            {
                if (hooked) Apply();
                Flora.Reprice();
            };
        }

        // Called after ZNetScene and ObjectDB wake up; completes once both exist.
        internal static void Install()
        {
            hooked = true;
            Apply();
        }

        // The wood cost of one tree; null when the item is missing. Not recovered, like the game's sapling costs, so
        // a destroyed sapling returns nothing.
        internal static Piece.Requirement[] WoodCost(ObjectDB db)
        {
            GameObject item = db ? db.GetItemPrefab(TreeCatalog.WoodItem) : null;
            ItemDrop wood = item ? item.GetComponent<ItemDrop>() : null;
            if (!wood) return null;
            return new[] { new Piece.Requirement { m_resItem = wood, m_amount = TreeCatalog.WoodAmount, m_recover = false } };
        }

        // Idempotent: a sapling that still carries Fieldsmith's wood cost is left alone. Another mod's later cost
        // becomes the original that is restored.
        private static void Apply()
        {
            if (!CostWood)
            {
                Clear();
                return;
            }
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (!scene || !db || db.m_items == null || db.m_items.Count == 0) return;
            int count = 0;
            foreach (string name in TreeCatalog.Saplings)
            {
                GameObject prefab = scene.GetPrefab(name);
                Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
                if (!piece)
                {
                    if (!missingLogged) log?.LogWarning($"{name} was not found in this game version; its cost is not changed.");
                    continue;
                }
                if (changes.TryGetValue(name, out Change change) && change.Piece == piece && ReferenceEquals(piece.m_resources, change.Wood)) continue;
                Piece.Requirement[] wood = WoodCost(db);
                if (wood == null)
                {
                    log?.LogWarning($"The item {TreeCatalog.WoodItem} was not found; tree saplings keep their cost.");
                    return;
                }
                changes[name] = new Change { Piece = piece, Original = piece.m_resources, Wood = wood };
                piece.m_resources = wood;
                count++;
            }
            missingLogged = true;
            if (count > 0) log?.LogInfo($"{count} tree saplings cost {TreeCatalog.WoodAmount} wood instead of a seed or cone ([Plants] TreesCostWood).");
        }

        // Puts back the requirements of every sapling that still carries Fieldsmith's wood cost.
        internal static void Clear()
        {
            int count = 0;
            foreach (Change change in changes.Values)
            {
                if (!change.Piece || !ReferenceEquals(change.Piece.m_resources, change.Wood)) continue;
                change.Piece.m_resources = change.Original;
                count++;
            }
            changes.Clear();
            if (count > 0) log?.LogInfo($"{count} tree saplings cost their seed or cone again.");
        }
    }
}
