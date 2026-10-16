using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Fieldsmith
{
    // A plantable piece: a build piece whose prefab carries a Plant (Cultivator seeds and saplings), or another
    // object in a planting tool's build menu that is placed as it is (the wild plants Fieldsmith adds itself, see
    // Flora, or those of mods like Plant Everything: berry bushes, mushrooms, flowers and small trees). Objects
    // have no Plant: they do not grow, so they only need their own footprint free.
    internal sealed class PlantInfo
    {
        internal readonly Piece Piece;
        // Null for objects.
        internal readonly Plant Plant;
        internal readonly string PrefabName;
        // The plant's grow radius; 0 for objects.
        internal readonly float GrowRadius;
        internal readonly double Reach;
        // Radius kept free around the new plant: the grow radius, or an object's own reach.
        internal readonly double Space;
        internal readonly double AutoSpacing;
        // Closest spacing at which neighbours of this kind still leave each other room to grow.
        internal readonly double TightSpacing;

        internal PlantInfo(Piece piece, Plant plant, double margin)
        {
            Piece = piece;
            Plant = plant;
            PrefabName = piece.gameObject.name;
            if (plant)
            {
                GrowRadius = Mathf.Max(0f, plant.m_growRadius);
                double reach = Plants.ColliderReach(piece.gameObject, false);
                if (plant.m_grownPrefabs != null)
                    foreach (GameObject grown in plant.m_grownPrefabs)
                        if (grown) reach = Math.Max(reach, Plants.ColliderReach(grown, false));
                Reach = reach;
                Space = GrowRadius;
            }
            else
            {
                // Pickables may only have trigger colliders (for interaction); they still take up that space.
                double reach = Plants.ColliderReach(piece.gameObject, false);
                if (reach <= 0) reach = Plants.ColliderReach(piece.gameObject, true);
                Reach = reach > 0 ? reach : Plants.DefaultObjectReach;
                Space = Reach;
            }
            AutoSpacing = GridMath.AutoSpacing(Space, Reach, margin);
            TightSpacing = GridMath.AutoSpacing(Space, Reach, 0);
        }

        internal bool IsObject => !Plant;

        internal string DisplayName => Plants.Localize(Piece.m_name);
    }

    internal static class Plants
    {
        private const double MaxReach = 3;
        // Footprint of an object whose prefab has no collider at all.
        internal const double DefaultObjectReach = 0.25;

        private static readonly Dictionary<Piece, PlantInfo> infos = new Dictionary<Piece, PlantInfo>();
        private static readonly HashSet<Piece> notPlants = new HashSet<Piece>();
        // Prefab names of what plants grow into when that is a harvestable crop (not a tree), with the plant that
        // grows it, and of objects that are pickable themselves: for following rows, harvesting blocks and
        // replanting.
        private static readonly Dictionary<string, PlantInfo> crops = new Dictionary<string, PlantInfo>(StringComparer.Ordinal);
        // Prefab names of objects, for following rows of objects that are not pickable (small trees, bushes).
        private static readonly Dictionary<string, PlantInfo> objects = new Dictionary<string, PlantInfo>(StringComparer.Ordinal);
        private static ZNetScene scannedScene;
        private static float largestGrowRadius;
        private static double margin = 0.05;
        private static ManualLogSource log;

        internal static float LargestGrowRadius => largestGrowRadius;

        internal static void Initialize(ManualLogSource logger, double spacingMargin)
        {
            log = logger;
            margin = spacingMargin;
        }

        // Spacing margin changed: every automatic spacing is recalculated on next use.
        internal static void SetMargin(double spacingMargin)
        {
            margin = spacingMargin;
            infos.Clear();
            crops.Clear();
            objects.Clear();
            scannedScene = null;
        }

        internal static void Clear()
        {
            infos.Clear();
            notPlants.Clear();
            crops.Clear();
            objects.Clear();
            scannedScene = null;
        }

        // Learns every plant prefab of the game and the objects of every planting tool once per scene, so crops
        // can be harvested and replanted even before the Cultivator has been used in this session.
        internal static void RegisterAll()
        {
            ZNetScene scene = ZNetScene.instance;
            if (!scene || scene == scannedScene) return;
            scannedScene = scene;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (!prefab || !prefab.GetComponent<Plant>()) continue;
                Piece piece = prefab.GetComponent<Piece>();
                if (piece) TryGet(piece, out _);
            }
            ObjectDB db = ObjectDB.instance;
            if (!db || db.m_items == null) return;
            foreach (GameObject item in db.m_items)
            {
                ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
                if (drop && drop.m_itemData?.m_shared != null) RegisterTable(drop.m_itemData.m_shared.m_buildPieces);
            }
        }

        // The plant a grown crop came from (its seed piece), or the piece of a pickable object a player placed;
        // false for anything else, including wild bushes and mushrooms of the same prefab.
        internal static bool TryGetCrop(Pickable pickable, out PlantInfo info)
        {
            info = null;
            if (!pickable || !crops.TryGetValue(Utils.GetPrefabName(pickable.gameObject), out info) || !info.Piece) return false;
            if (!info.IsObject) return true;
            Piece placed = pickable.GetComponent<Piece>();
            return placed && placed.IsPlacedByPlayer();
        }

        // Handled plants: ground plants with a Plant component, and the objects learned from a planting tool's
        // build menu (RegisterTable). Vines that attach to walls are left to vanilla.
        internal static bool TryGet(Piece piece, out PlantInfo info)
        {
            info = null;
            if (!piece) return false;
            if (infos.TryGetValue(piece, out info))
            {
                if (info.Piece) return true;
                infos.Remove(piece);
            }
            if (notPlants.Contains(piece)) return false;
            Plant plant = piece.GetComponent<Plant>();
            if (!plant || !Gridable(piece, plant))
            {
                notPlants.Add(piece);
                return false;
            }
            info = Add(piece, plant);
            return true;
        }

        // Learns every plant of the tool's piece table, so rows of any crop can be followed. In a planting tool
        // (a build menu with at least one plant, such as the Cultivator) the other placeable objects are learned
        // too: mods like Plant Everything add berry bushes, mushrooms and flowers there.
        internal static void RegisterTable(PieceTable table)
        {
            if (table == null || table.m_pieces == null) return;
            bool planting = false;
            foreach (GameObject prefab in table.m_pieces)
            {
                if (!prefab) continue;
                Piece piece = prefab.GetComponent<Piece>();
                if (piece && TryGet(piece, out _)) planting = true;
            }
            if (!planting) return;
            foreach (GameObject prefab in table.m_pieces)
            {
                if (!prefab) continue;
                Piece piece = prefab.GetComponent<Piece>();
                if (!piece || infos.ContainsKey(piece) || prefab.GetComponent<Plant>() || !Placeable(piece)) continue;
                notPlants.Remove(piece);
                Add(piece, null);
            }
        }

        private static PlantInfo Add(Piece piece, Plant plant)
        {
            var info = new PlantInfo(piece, plant, margin);
            infos[piece] = info;
            Register(info);
            if (log == null) return info;
            if (info.IsObject)
                log.LogInfo($"{info.PrefabName}: object without growth, reach {info.Reach:0.00} m, automatic spacing {info.AutoSpacing:0.00} m.");
            else
                log.LogInfo($"{info.PrefabName}: grow radius {info.GrowRadius:0.00} m, collider reach {info.Reach:0.00} m, automatic spacing {info.AutoSpacing:0.00} m.");
            return info;
        }

        // Ground plants without special placement rules. Wall vines and pieces with ceiling, water, snow or
        // surface rules are left to vanilla, whose placement checks Fieldsmith does not repeat per cell.
        private static bool Gridable(Piece piece, Plant plant) => plant.m_attachDistance <= 0f && !SpecialRules(piece);

        private static bool SpecialRules(Piece piece) =>
            piece.m_repairPiece || piece.m_removePiece || piece.m_waterPiece || piece.m_notOnTiltingSurface ||
            piece.m_inCeilingOnly || piece.m_notOnFloor || piece.m_requireDeepSnow || piece.m_mustConnectTo;

        // Objects placed as they are. Terrain tools, buildings, stations and wards are not, and neither are
        // pieces with vanilla rules that depend on more than the ground under them (teleport areas, clipping,
        // blocking pieces).
        private static bool Placeable(Piece piece)
        {
            if (SpecialRules(piece) || piece.m_onlyInTeleportArea || piece.m_noClipping) return false;
            if (piece.m_blockRadius > 0f && piece.m_blockingPieces != null && piece.m_blockingPieces.Count > 0) return false;
            GameObject prefab = piece.gameObject;
            return !prefab.GetComponent<TerrainOp>() && !prefab.GetComponent<TerrainModifier>() && !prefab.GetComponent<WearNTear>() &&
                !prefab.GetComponent<StationExtension>() && !prefab.GetComponentInChildren<CraftingStation>(true) &&
                !prefab.GetComponent<PrivateArea>();
        }

        private static void Register(PlantInfo info)
        {
            if (info.IsObject)
            {
                objects[info.PrefabName] = info;
                if (info.Piece.GetComponent<Pickable>()) crops[info.PrefabName] = info;
                return;
            }
            largestGrowRadius = Mathf.Max(largestGrowRadius, info.GrowRadius);
            if (info.Plant.m_grownPrefabs == null) return;
            foreach (GameObject grown in info.Plant.m_grownPrefabs)
                if (grown && grown.GetComponent<Pickable>() && !grown.GetComponent<TreeBase>()) crops[grown.name] = info;
        }

        // Whether a scene object is a planted (growing) plant, a grown crop or an object a player placed from a
        // planting tool, for following rows. Grown trees and wild plants are not followed. Returns the object's
        // root position.
        internal static bool IsCrop(Collider collider, out Vector3 position)
        {
            position = Vector3.zero;
            Plant plant = collider.GetComponentInParent<Plant>();
            if (plant)
            {
                position = plant.transform.position;
                return true;
            }
            Pickable pickable = collider.GetComponentInParent<Pickable>();
            if (TryGetCrop(pickable, out _))
            {
                position = pickable.transform.position;
                return true;
            }
            if (objects.Count == 0) return false;
            Piece piece = collider.GetComponentInParent<Piece>();
            if (!piece || !piece.IsPlacedByPlayer() || !objects.TryGetValue(Utils.GetPrefabName(piece.gameObject), out PlantInfo info) || !info.Piece)
                return false;
            position = piece.transform.position;
            return true;
        }

        // Largest horizontal distance from the prefab root that its solid colliders (or all colliders) reach.
        internal static double ColliderReach(GameObject prefab, bool includeTriggers)
        {
            double reach = 0;
            Transform root = prefab.transform;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!collider || (collider.isTrigger && !includeTriggers)) continue;
                Transform t = collider.transform;
                Vector3 scale = t.lossyScale;
                double horizontalScale = Math.Max(Math.Abs(scale.x), Math.Abs(scale.z));
                Vector3 center;
                double extent;
                switch (collider)
                {
                    case SphereCollider sphere:
                        center = sphere.center;
                        extent = sphere.radius * Math.Max(horizontalScale, Math.Abs(scale.y));
                        break;
                    case CapsuleCollider capsule:
                        center = capsule.center;
                        extent = capsule.direction == 1 ? capsule.radius * horizontalScale : Math.Max(capsule.radius, capsule.height * 0.5) * horizontalScale;
                        break;
                    case BoxCollider box:
                        center = box.center;
                        extent = 0.5 * Math.Sqrt(box.size.x * box.size.x * scale.x * scale.x + box.size.z * box.size.z * scale.z * scale.z);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh:
                        Bounds bounds = mesh.sharedMesh.bounds;
                        center = bounds.center;
                        extent = Math.Sqrt(bounds.extents.x * bounds.extents.x * scale.x * scale.x + bounds.extents.z * bounds.extents.z * scale.z * scale.z);
                        break;
                    default:
                        continue;
                }
                Vector3 local = root.InverseTransformPoint(t.TransformPoint(center));
                double offset = Math.Sqrt(local.x * local.x + local.z * local.z);
                reach = Math.Max(reach, offset + extent);
            }
            return GridFrame.Finite(reach) ? Math.Min(MaxReach, reach) : 0;
        }

        internal static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }
    }
}
