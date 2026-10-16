using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Fieldsmith
{
    // Makes the game's wild plants (FloraCatalog) plantable with the Cultivator: each vanilla prefab gets a
    // Piece with the catalog's cost and is added to the Cultivator's build menu. Placed ones are ordinary
    // objects of that prefab, so other players and the server need nothing. Plant Everything adds the same
    // plants itself; while it is loaded Fieldsmith leaves them to it.
    internal static class Flora
    {
        internal const string PlantEverythingGuid = "advize.PlantEverything";
        private const string CultivatorItem = "Cultivator";
        private const string PlaceEffect = "vfx_Place_wood_pole";
        private const string PlaceSound = "sfx_build_cultivator";
        private const string SnapPointName = "_snappoint";
        private const string SnapPointTag = "snappoint";
        private const string HoverTargetName = "Fieldsmith_hover";
        private const float HoverRadiusMin = 0.2f;
        private const float HoverRadiusMax = 0.5f;
        private const string MarkerName = "Fieldsmith_picked";
        // Size of the picked marker relative to the full model.
        private const float MarkerScale = 0.35f;
        // Vanilla Pickable.Awake: InvokeRepeating("UpdateRespawn", Random.Range(1f, 5f), 60f).
        private const string UpdateRespawnMethod = "UpdateRespawn";
        private const float RespawnCheckSeconds = 60f;

        private static readonly Dictionary<string, FloraEntry> byPrefab = new Dictionary<string, FloraEntry>(StringComparer.Ordinal);
        // The Piece components Fieldsmith added, by prefab name. A prefab that already has another Piece is
        // left alone (another mod made it placeable).
        private static readonly Dictionary<string, Piece> added = new Dictionary<string, Piece>(StringComparer.Ordinal);
        private static ManualLogSource log;
        private static ConfigEntry<FloraCostMode> costs;
        private static bool enabled;
        private static bool deferredLogged;
        private static ZNetScene installedScene;
        private static PieceTable installedTable;
        // The Cultivator table whose m_canRemovePieces Fieldsmith turned on, and its original value.
        private static PieceTable removalTable;
        private static bool removalOriginal;

        internal static void Initialize(ManualLogSource logger, bool on, ConfigEntry<FloraCostMode> costMode)
        {
            log = logger;
            enabled = on;
            costs = costMode;
            costMode.SettingChanged += (_, __) => Reprice();
            byPrefab.Clear();
            foreach (FloraEntry entry in FloraCatalog.All) byPrefab[entry.Prefab] = entry;
        }

        // Called after ZNetScene and ObjectDB wake up; completes once both exist. Idempotent per scene.
        internal static void Install()
        {
            if (!enabled) return;
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (!scene || !db || db.m_items == null || db.m_items.Count == 0) return;
            GameObject cultivator = db.GetItemPrefab(CultivatorItem);
            ItemDrop drop = cultivator ? cultivator.GetComponent<ItemDrop>() : null;
            PieceTable table = drop ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (scene == installedScene && table == installedTable) return;
            installedScene = scene;
            installedTable = table;
            if (Chainloader.PluginInfos.ContainsKey(PlantEverythingGuid))
            {
                if (!deferredLogged) log?.LogInfo("Plant Everything is installed; it adds the wild plants to the Cultivator, so Fieldsmith does not.");
                deferredLogged = true;
                return;
            }
            if (!table)
            {
                log?.LogWarning($"The {CultivatorItem} build menu was not found; wild plants cannot be planted.");
                return;
            }

            GameObject effect = scene.GetPrefab(PlaceEffect);
            GameObject sound = scene.GetPrefab(PlaceSound);
            int count = 0;
            foreach (FloraEntry entry in FloraCatalog.All)
            {
                GameObject prefab = scene.GetPrefab(entry.Prefab);
                Piece piece = Prepare(entry, prefab, db, effect, sound);
                if (!piece) continue;
                if (!table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
                count++;
            }
            if (count > 0 && removalTable != table)
            {
                RestoreRemoval();
                removalTable = table;
                removalOriginal = table.m_canRemovePieces;
                // Lets vanilla UpdatePlacement call RemovePiece with the Cultivator (Remove button, middle
                // mouse); RemovePiecePatch then removes only wild plants a player placed.
                table.m_canRemovePieces = true;
            }
            log?.LogInfo($"Added {count} of {FloraCatalog.All.Count} wild plants to the {CultivatorItem}.");
        }

        // Adds and fills the Piece of one prefab; returns null when it cannot be placed.
        private static Piece Prepare(FloraEntry entry, GameObject prefab, ObjectDB db, GameObject effect, GameObject sound)
        {
            if (!prefab || !prefab.GetComponent<ZNetView>())
            {
                log?.LogWarning($"{entry.Prefab} was not found in this game version; it is not added.");
                return null;
            }
            Piece piece = prefab.GetComponent<Piece>();
            if (piece && !(added.TryGetValue(entry.Prefab, out Piece own) && own == piece))
            {
                log?.LogInfo($"{entry.Prefab} is already a build piece (another mod); Fieldsmith leaves it as it is.");
                return null;
            }
            Piece.Requirement[] requirements = Requirements(entry, prefab, db);
            if (requirements == null) return null;
            if (!piece)
            {
                piece = prefab.AddComponent<Piece>();
                added[entry.Prefab] = piece;
            }
            piece.m_name = entry.Name;
            piece.m_description = entry.Description;
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_groundPiece = entry.Grounded;
            piece.m_groundOnly = entry.Grounded;
            piece.m_cultivatedGroundOnly = false;
            piece.m_onlyInBiome = Heightmap.Biome.None;
            piece.m_canBeRemoved = entry.Removable;
            // Enemies may attack placed ones, never wild ones.
            piece.m_targetNonPlayerBuilt = false;
            piece.m_resources = requirements;
            piece.m_icon = requirements[0].m_resItem.m_itemData.GetIcon();
            var effects = new List<EffectList.EffectData>(2);
            if (effect) effects.Add(new EffectList.EffectData { m_prefab = effect, m_enabled = true });
            if (sound) effects.Add(new EffectList.EffectData { m_prefab = sound, m_enabled = true });
            piece.m_placeEffect = new EffectList { m_effectPrefabs = effects.ToArray() };
            AddSnapPoints(entry, prefab);
            return piece;
        }

        private static Piece.Requirement[] Requirements(FloraEntry entry, GameObject prefab, ObjectDB db)
        {
            Pickable pickable = prefab.GetComponent<Pickable>();
            FloraCostMode mode = costs != null ? costs.Value : FloraCostMode.Seed;
            var result = new Piece.Requirement[entry.Costs.Length];
            for (int i = 0; i < entry.Costs.Length; i++)
            {
                FloraCost cost = entry.Costs[i];
                string itemName = cost.Item;
                if (itemName == null) itemName = pickable && pickable.m_itemPrefab ? pickable.m_itemPrefab.name : null;
                GameObject item = itemName != null ? db.GetItemPrefab(itemName) : null;
                ItemDrop resource = item ? item.GetComponent<ItemDrop>() : null;
                int amount = FloraCatalog.CostAmount(entry, cost, pickable ? pickable.m_amount : 1, mode);
                if (!resource || amount <= 0)
                {
                    log?.LogWarning($"{entry.Prefab}: its cost item {itemName ?? "(none)"} was not found; it is not added.");
                    return null;
                }
                result[i] = new Piece.Requirement { m_resItem = resource, m_amount = amount, m_recover = entry.Recover };
            }
            return result.Length > 0 ? result : null;
        }

        // [Plants] Costs changed: new requirements for every added piece. The build menu reads them live.
        private static void Reprice()
        {
            ObjectDB db = ObjectDB.instance;
            if (!db) return;
            foreach (KeyValuePair<string, Piece> pair in added)
            {
                if (!pair.Value || !byPrefab.TryGetValue(pair.Key, out FloraEntry entry)) continue;
                Piece.Requirement[] requirements = Requirements(entry, pair.Value.gameObject, db);
                if (requirements != null) pair.Value.m_resources = requirements;
            }
        }

        // Whether the player holds the Cultivator whose removal Fieldsmith turned on.
        internal static bool RemovesWith(Player player) =>
            enabled && removalTable && player && player.GetBuildTool() == removalTable;

        // Inactive "_snappoint" children, the way vanilla pieces carry their snap points.
        private static void AddSnapPoints(FloraEntry entry, GameObject prefab)
        {
            if (entry.SnapPoints == null || prefab.transform.Find(SnapPointName)) return;
            foreach (double[] point in entry.SnapPoints)
            {
                var snap = new GameObject(SnapPointName) { tag = SnapPointTag };
                snap.transform.SetParent(prefab.transform, false);
                snap.transform.localPosition = new Vector3((float)point[0], (float)point[1], (float)point[2]);
                snap.SetActive(false);
            }
        }

        internal static bool IsAdded(Piece piece) =>
            piece && added.TryGetValue(Utils.GetPrefabName(piece.gameObject), out Piece own) && own;

        // A placed one gets the catalog's yield and regrowth, the configured regrow time and, when picking hides
        // all of it, a marker and a hover target. Changed per object, so wild ones keep the game's values. Picking
        // and regrowing happen on the object's owner, so the values apply while a player with Fieldsmith owns it.
        // Loaded ones are prepared before Pickable.Awake, which reads the respawn time and removes a picked
        // pickable that never respawns; newly placed ones right after Piece.SetCreator.
        internal static void ApplyLoaded(Pickable pickable)
        {
            if (!enabled || !pickable) return;
            Piece piece = pickable.GetComponent<Piece>();
            if (!piece || !IsAdded(piece)) return;
            ZNetView view = pickable.GetComponent<ZNetView>();
            ZDO zdo = view ? view.GetZDO() : null;
            if (zdo == null || zdo.GetLong(ZDOVars.s_creator, 0L) == 0L) return;
            Apply(pickable, piece, beforeAwake: true);
        }

        internal static void ApplyPlaced(Piece piece)
        {
            if (!enabled || !piece || !piece.IsPlacedByPlayer() || !IsAdded(piece)) return;
            Pickable pickable = piece.GetComponent<Pickable>();
            if (pickable) Apply(pickable, piece, beforeAwake: false);
        }

        private static void Apply(Pickable pickable, Piece piece, bool beforeAwake)
        {
            if (!byPrefab.TryGetValue(Utils.GetPrefabName(piece.gameObject), out FloraEntry entry)) return;
            if (entry.Yield > 0) pickable.m_amount = entry.Yield;
            if (entry.NoExtraDrops) pickable.m_extraDrops = new DropTable();
            AddRegrowth(pickable, entry, beforeAwake);
            Growth.Track(pickable);
            AddPickedMarker(pickable);
        }

        // A planted stone grows back like flint: its model child is hidden while picked and it respawns after the
        // catalog time. Vanilla Pickable.Awake starts the respawn check (UpdateRespawn every 60 s) only when the
        // respawn time is above 0 at that moment, so a newly placed one (already awake) gets it started here.
        private static void AddRegrowth(Pickable pickable, FloraEntry entry, bool beforeAwake)
        {
            if (!(entry.RegrowMinutes > 0) || pickable.m_respawnTimeMinutes > 0) return;
            GameObject model = ModelChild(pickable.transform);
            if (!model) return;
            pickable.m_hideWhenPicked = model;
            pickable.m_respawnTimeMinutes = (float)entry.RegrowMinutes;
            if (beforeAwake) return;
            model.SetActive(!pickable.GetPicked());
            pickable.InvokeRepeating(UpdateRespawnMethod, UnityEngine.Random.Range(1f, 5f), RespawnCheckSeconds);
        }

        // The direct child that holds every collider (Pickable_Stone: "model (1)").
        private static GameObject ModelChild(Transform root)
        {
            Transform model = null;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                Transform child = collider.transform;
                while (child.parent && child.parent != root) child = child.parent;
                if (child == root || (model && child != model)) return null;
                model = child;
            }
            return model ? model.gameObject : null;
        }

        // Mushrooms, flowers, fiddleheads, branches and flint keep every renderer and collider under
        // m_hideWhenPicked, so a picked one vanishes until it grows back and cannot be looked at. A placed one gets
        // a small copy of its own model that shows while it is picked (PickedMarker follows the hidden object),
        // and a small collider on the same layer outside the hidden object, so Player.FindHoverObject finds the
        // Pickable (GetComponentInParent<Hoverable>) and the regrow timer shows. Wild ones are not changed.
        private static void AddPickedMarker(Pickable pickable)
        {
            GameObject hidden = pickable.m_hideWhenPicked;
            if (!hidden || !(pickable.m_respawnTimeMinutes > 0) || hidden == pickable.gameObject || pickable.transform.Find(HoverTargetName)) return;
            int layer = -1;
            foreach (Collider collider in pickable.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.transform.IsChildOf(hidden.transform)) return;
                if (layer < 0) layer = collider.gameObject.layer;
            }
            if (layer < 0) return;
            float radius = Mathf.Clamp((float)Plants.ColliderReach(pickable.gameObject, true), HoverRadiusMin, HoverRadiusMax);
            var target = new GameObject(HoverTargetName) { layer = layer };
            target.transform.SetParent(pickable.transform, false);
            target.transform.localPosition = new Vector3(0f, radius, 0f);
            target.AddComponent<SphereCollider>().radius = radius;

            GameObject marker = BuildMarker(pickable.transform, hidden);
            if (!marker) return;
            marker.SetActive(!hidden.activeSelf);
            hidden.AddComponent<PickedMarker>().Marker = marker;
        }

        // A copy of the hidden model's meshes (only the first level of detail), scaled down towards the ground
        // point. Only renderers: no colliders, particles or lights.
        private static GameObject BuildMarker(Transform root, GameObject hidden)
        {
            var renderers = new List<MeshRenderer>();
            LODGroup lods = hidden.GetComponentInChildren<LODGroup>(true);
            LOD[] levels = lods ? lods.GetLODs() : null;
            if (levels != null && levels.Length > 0)
            {
                foreach (Renderer renderer in levels[0].renderers)
                    if (renderer is MeshRenderer mesh) renderers.Add(mesh);
            }
            else
            {
                renderers.AddRange(hidden.GetComponentsInChildren<MeshRenderer>(true));
            }
            GameObject marker = null;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Vector3 rootScale = root.lossyScale;
            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
                if (!filter || !filter.sharedMesh) continue;
                if (!marker)
                {
                    marker = new GameObject(MarkerName);
                    marker.transform.SetParent(root, false);
                }
                Transform source = renderer.transform;
                var part = new GameObject(source.name) { layer = renderer.gameObject.layer };
                part.transform.SetParent(marker.transform, false);
                part.transform.localPosition = root.InverseTransformPoint(source.position);
                part.transform.localRotation = inverse * source.rotation;
                Vector3 scale = source.lossyScale;
                part.transform.localScale = new Vector3(Ratio(scale.x, rootScale.x), Ratio(scale.y, rootScale.y), Ratio(scale.z, rootScale.z));
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            if (marker) marker.transform.localScale = Vector3.one * MarkerScale;
            return marker;
        }

        private static float Ratio(float value, float of) => Mathf.Abs(of) > 1e-6f ? value / of : value;

        private static void RestoreRemoval()
        {
            if (removalTable) removalTable.m_canRemovePieces = removalOriginal;
            removalTable = null;
        }

        internal static void Clear()
        {
            RestoreRemoval();
            if (installedTable)
                foreach (KeyValuePair<string, Piece> pair in added)
                    if (pair.Value) installedTable.m_pieces.Remove(pair.Value.gameObject);
            foreach (Piece piece in added.Values)
                if (piece) UnityEngine.Object.Destroy(piece);
            added.Clear();
            installedScene = null;
            installedTable = null;
        }
    }
}
