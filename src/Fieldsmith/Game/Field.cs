using UnityEngine;

namespace Fieldsmith
{
    internal enum CellStatus
    {
        Ok,
        NoGround,
        NoBuildZone,
        PrivateZone,
        WrongBiome,
        NeedCultivated,
        NeedDirt,
        Water,
        Covered,
        NoSpace,
        NoSun,
        TooHot,
        TooCold,
        Player,
    }

    // Checks whether a plant may go into one cell. Mirrors the vanilla placement rules that depend on the
    // position (Player.UpdatePlacementGhost) and the plant's own growing conditions (Plant.UpdateHealth,
    // Plant.HaveRoof, Plant.HaveGrowSpace), so a field is not planted where its plants could not grow.
    internal static class Field
    {
        private const float CoverHeight = 1.2f;
        private const float PlayerClearance = 0.5f;
        private const float MaxLift = 1f;

        private static readonly Collider[] hits = new Collider[64];
        private static int spaceMask;
        private static int roofMask;
        private static int objectMask;
        private static int placeMask;
        private static int terrainLayer;

        // Same layers as Plant.HaveGrowSpace.
        internal static int SpaceMask
        {
            get
            {
                if (spaceMask == 0) spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
                return spaceMask;
            }
        }

        // Same layers as Plant.HaveRoof.
        private static int RoofMask
        {
            get
            {
                if (roofMask == 0) roofMask = LayerMask.GetMask("Default", "static_solid", "piece");
                return roofMask;
            }
        }

        // Terrain surface height (a ray onto the terrain layer, like vanilla ground placement).
        internal static bool GroundHeight(float x, float z, out float height)
        {
            height = 0f;
            return ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z), out height);
        }

        // Height of the ghost above the terrain under it, as vanilla placed it. Returns false unless the first
        // solid surface under the ghost is the terrain itself (not a floor, a rock or another object).
        internal static bool TryLift(Vector3 ghost, out float lift)
        {
            lift = 0f;
            if (placeMask == 0)
            {
                placeMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
                terrainLayer = LayerMask.NameToLayer("terrain");
            }
            if (!Physics.Raycast(ghost + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 0.5f + MaxLift + 0.5f, placeMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.gameObject.layer != terrainLayer) return false;
            float offset = ghost.y - hit.point.y;
            if (Mathf.Abs(offset) > MaxLift) return false;
            lift = offset;
            return true;
        }

        // position: where the plant goes (ground + lift); fallbackY is used when the cell has no loaded ground.
        internal static CellStatus Evaluate(PlantInfo info, double x, double z, float lift, float fallbackY, bool flash, out Vector3 position)
        {
            var probe = new Vector3((float)x, 0f, (float)z);
            position = new Vector3(probe.x, fallbackY, probe.z);
            Heightmap heightmap = Heightmap.FindHeightmap(probe);
            if (!heightmap || !GroundHeight(probe.x, probe.z, out float ground)) return CellStatus.NoGround;
            var point = new Vector3(probe.x, ground, probe.z);
            position = new Vector3(probe.x, ground + lift, probe.z);
            Piece piece = info.Piece;
            Plant plant = info.Plant;

            if (Location.IsInsideNoBuildLocation(point)) return CellStatus.NoBuildZone;
            if (!PrivateArea.CheckAccess(point, 0f, flash, false)) return CellStatus.PrivateZone;
            if (ZoneSystem.instance && ground < ZoneSystem.instance.m_waterLevel) return CellStatus.Water;
            if (piece.m_onlyInBiome != Heightmap.Biome.None && (Heightmap.FindBiome(point) & piece.m_onlyInBiome) == 0)
                return CellStatus.WrongBiome;
            Heightmap.Biome biome = heightmap.GetBiome(point);
            // Objects (no Plant) have only the piece's own rules: they do not grow, so biome, heat, cold and sun
            // of Plant.UpdateHealth do not apply.
            if (plant && (biome & plant.m_biome) == 0) return CellStatus.WrongBiome;
            if ((piece.m_cultivatedGroundOnly || (plant && plant.m_needCultivatedGround)) && !heightmap.IsCultivated(point))
                return CellStatus.NeedCultivated;
            if (piece.m_vegetationGroundOnly)
            {
                float mask = heightmap.GetVegetationMask(point);
                if (biome == Heightmap.Biome.AshLands ? mask > 0.1f : mask < 0.25f) return CellStatus.NeedDirt;
            }
            if (plant && !plant.m_tolerateHeat && biome == Heightmap.Biome.AshLands && !ShieldGenerator.IsInsideShield(point)) return CellStatus.TooHot;
            if (plant && !plant.m_tolerateCold && (biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.Mountain) && !ShieldGenerator.IsInsideShield(point))
                return CellStatus.TooCold;
            if (Covered(point)) return CellStatus.Covered;
            if (Occupied(info, position)) return CellStatus.NoSpace;
            if (plant && Physics.Raycast(position, Vector3.up, 100f, RoofMask, QueryTriggerInteraction.Ignore)) return CellStatus.NoSun;
            if (Player.IsPlayerInRange(point, PlayerClearance)) return CellStatus.Player;
            return CellStatus.Ok;
        }

        // Something solid right above the ground (a floor, a fence) would sit inside the plant.
        private static bool Covered(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * CoverHeight, Vector3.down, CoverHeight - 0.05f, SpaceMask, QueryTriggerInteraction.Ignore);

        // The new plant needs its grow radius free of other colliders (Plant.HaveGrowSpace), and must not
        // take the grow space of a neighbouring plant with a larger radius. An object needs its own reach free,
        // including of pickables and plants whose only colliders are triggers, so objects never stack.
        private static bool Occupied(PlantInfo info, Vector3 position)
        {
            if (info.IsObject)
            {
                if (ObjectOccupied(position, (float)info.Space)) return true;
            }
            else if (info.GrowRadius > 0f &&
                Physics.OverlapSphereNonAlloc(position, info.GrowRadius, hits, SpaceMask, QueryTriggerInteraction.Ignore) > 0)
            {
                return true;
            }
            float reach = Plants.LargestGrowRadius + (float)info.Reach;
            if (reach <= info.GrowRadius) return false;
            int count = Physics.OverlapSphereNonAlloc(position, reach, hits, SpaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Plant other = hits[i].GetComponent<Plant>();
                if (!other || other.m_growRadius <= info.GrowRadius) continue;
                Vector3 delta = other.transform.position - position;
                delta.y = 0f;
                if (delta.magnitude < other.m_growRadius + info.Reach) return true;
            }
            return false;
        }

        private static bool ObjectOccupied(Vector3 position, float radius)
        {
            if (objectMask == 0) objectMask = SpaceMask | LayerMask.GetMask("item");
            int count = Physics.OverlapSphereNonAlloc(position, radius, hits, objectMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i];
                if (!hit) continue;
                if (!hit.isTrigger || hit.GetComponentInParent<Pickable>() || hit.GetComponentInParent<Plant>()) return true;
            }
            return false;
        }

        // Status shown by the ghost for a cell (red or not, and the vanilla message on click).
        internal static Player.PlacementStatus ToPlacement(CellStatus status)
        {
            switch (status)
            {
                case CellStatus.Ok: return Player.PlacementStatus.Valid;
                case CellStatus.NoBuildZone: return Player.PlacementStatus.NoBuildZone;
                case CellStatus.PrivateZone: return Player.PlacementStatus.PrivateZone;
                case CellStatus.WrongBiome: return Player.PlacementStatus.WrongBiome;
                case CellStatus.NeedCultivated: return Player.PlacementStatus.NeedCultivated;
                case CellStatus.NeedDirt: return Player.PlacementStatus.NeedDirt;
                case CellStatus.Player: return Player.PlacementStatus.BlockedbyPlayer;
                case CellStatus.Covered:
                case CellStatus.NoSpace: return Player.PlacementStatus.MoreSpace;
                default: return Player.PlacementStatus.Invalid;
            }
        }

        // Localization tokens of the game: placement messages and the plant hover states.
        internal static string Message(CellStatus status)
        {
            switch (status)
            {
                case CellStatus.NoBuildZone: return "$msg_nobuildzone";
                case CellStatus.PrivateZone: return "$msg_privatezone";
                case CellStatus.WrongBiome: return "$msg_wrongbiome";
                case CellStatus.NeedCultivated: return "$msg_needcultivated";
                case CellStatus.NeedDirt: return "$msg_needdirt";
                case CellStatus.Player: return "$msg_blocked";
                case CellStatus.Covered:
                case CellStatus.NoSpace: return "$piece_plant_nospace";
                case CellStatus.NoSun: return "$piece_plant_nosun";
                case CellStatus.TooHot: return "$piece_plant_toohot";
                case CellStatus.TooCold: return "$piece_plant_toocold";
                default: return "$msg_invalidplacement";
            }
        }

        // The vanilla message for each placement status (Player.TryPlacePiece).
        internal static string Message(Player.PlacementStatus status)
        {
            switch (status)
            {
                case Player.PlacementStatus.NoBuildZone: return "$msg_nobuildzone";
                case Player.PlacementStatus.BlockedbyPlayer: return "$msg_blocked";
                case Player.PlacementStatus.PrivateZone: return "$msg_privatezone";
                case Player.PlacementStatus.MoreSpace: return "$msg_needspace";
                case Player.PlacementStatus.NoTeleportArea: return "$msg_noteleportarea";
                case Player.PlacementStatus.ExtensionMissingStation: return "$msg_extensionmissingstation";
                case Player.PlacementStatus.WrongBiome: return "$msg_wrongbiome";
                case Player.PlacementStatus.NeedCultivated: return "$msg_needcultivated";
                case Player.PlacementStatus.NeedDirt: return "$msg_needdirt";
                case Player.PlacementStatus.NotInDungeon: return "$msg_notindungeon";
                case Player.PlacementStatus.DeepSnow: return "$msg_snowtoodeep";
                case Player.PlacementStatus.NoSnow: return "$msg_nosnow";
                default: return "$msg_invalidplacement";
            }
        }

        // Statuses that only depend on where on the ground the plant goes; Fieldsmith re-evaluates them per cell
        // when the vanilla ray hit the terrain. Any other status refuses the whole click.
        internal static bool IsPositional(Player.PlacementStatus status)
        {
            switch (status)
            {
                case Player.PlacementStatus.Valid:
                case Player.PlacementStatus.NeedCultivated:
                case Player.PlacementStatus.NeedDirt:
                case Player.PlacementStatus.PrivateZone:
                case Player.PlacementStatus.NoBuildZone:
                case Player.PlacementStatus.WrongBiome:
                case Player.PlacementStatus.BlockedbyPlayer:
                case Player.PlacementStatus.MoreSpace:
                    return true;
                default:
                    return false;
            }
        }
    }
}
