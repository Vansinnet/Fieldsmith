using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fieldsmith
{
    // Block harvest: picking a grown crop with the Alt place key held picks every ripe crop in the block around
    // it, each through vanilla Pickable.Interact. Optional replanting puts a new seed where each crop stood.
    internal sealed partial class Controller
    {
        private const float ReplantDelay = 0.25f;
        private const float ReplantTimeout = 3f;
        private const int MaxReplants = GridMath.MaxPatternSize * GridMath.MaxPatternSize * 2;

        private readonly Collider[] harvestHits = new Collider[512];
        private readonly List<Pickable> harvestFound = new List<Pickable>();
        private readonly List<Replanting> replants = new List<Replanting>();
        private bool harvesting;

        private sealed class Replanting
        {
            internal Pickable Crop;
            internal PlantInfo Info;
            internal Vector3 Position;
            internal float Time;
        }

        private int HarvestSide()
        {
            int configured = settings.HarvestSize.Value;
            if (configured > 0) return Math.Min(GridMath.MaxPatternSize, configured);
            return Math.Max(3, size);
        }

        // Called after vanilla Pickable.Interact. Only a fresh press (not holding the key) with Alt place held,
        // by the local player, on a crop grown from a plant, starts a block harvest.
        internal void OnPicked(Pickable picked, Humanoid character, bool repeat, bool alt)
        {
            if (harvesting || halted || repeat || !alt || !settings.HarvestBlocks.Value) return;
            Player player = character as Player;
            if (!player || player != Player.m_localPlayer || !picked) return;
            Plants.RegisterAll();
            if (!Plants.TryGetCrop(picked, out PlantInfo info)) return;

            double spacing = Spacing(info);
            int side = HarvestSide();
            Vector3 center = picked.transform.position;
            GridFrame block = HarvestFrame(center, spacing);
            GridMath.PatternStart(block, center.x, center.z, side, out int startI, out int startJ);
            float radius = (float)(spacing * side * 0.75 + 0.5);
            int count = Physics.OverlapSphereNonAlloc(center, radius, harvestHits, ~0, QueryTriggerInteraction.Collide);
            harvestFound.Clear();
            for (int i = 0; i < count; i++)
            {
                Pickable pickable = harvestHits[i] ? harvestHits[i].GetComponentInParent<Pickable>() : null;
                if (!pickable || pickable == picked || harvestFound.Contains(pickable)) continue;
                if (!pickable.CanBePicked() || !Plants.TryGetCrop(pickable, out _)) continue;
                Vector3 p = pickable.transform.position;
                block.ToGrid(p.x, p.z, out double u, out double v);
                if (u < startI - 0.5 || u > startI + side - 0.5 || v < startJ - 0.5 || v > startJ + side - 0.5) continue;
                harvestFound.Add(pickable);
            }
            // Nearest first, so a stamina limit leaves the far edge standing.
            harvestFound.Sort((a, b) => (a.transform.position - center).sqrMagnitude.CompareTo((b.transform.position - center).sqrMagnitude));

            int targets = harvestFound.Count;
            float perCrop = settings.HarvestStaminaPerCrop.Value;
            int allowed = Budget.Stamina(player.GetStamina(), perCrop * Game.m_staminaRate);
            if (perCrop <= 0f) allowed = Budget.Unlimited;
            int picks = 0;
            harvesting = true;
            try
            {
                QueueReplant(picked, info);
                foreach (Pickable pickable in harvestFound)
                {
                    if (picks >= allowed) break;
                    if (!pickable || !pickable.CanBePicked()) continue;
                    Plants.TryGetCrop(pickable, out PlantInfo cropInfo);
                    pickable.Interact(player, false, false);
                    picks++;
                    QueueReplant(pickable, cropInfo);
                }
            }
            finally
            {
                harvesting = false;
                harvestFound.Clear();
            }
            if (picks > 0 && perCrop > 0f) player.UseStamina(perCrop * picks);
            if (targets > 0)
                player.Message(MessageHud.MessageType.TopLeft, $"Harvested {picks + 1}" + (picks < targets ? " (not enough stamina for the rest)" : string.Empty));
        }

        // The block follows the rows of the field the picked crop stands in.
        private GridFrame HarvestFrame(Vector3 center, double spacing)
        {
            double rotation = manualRotation;
            int count = Physics.OverlapSphereNonAlloc(center, (float)(spacing * 1.3), found, followMask, QueryTriggerInteraction.Collide);
            double bestError = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!found[i] || !Plants.IsCrop(found[i], out Vector3 position)) continue;
                if (!GridMath.TryRowRotation(center.x, center.z, position.x, position.z, spacing, out double candidate)) continue;
                double error = Math.Abs(Horizontal(position, center) - spacing);
                if (error < bestError)
                {
                    bestError = error;
                    rotation = candidate;
                }
            }
            return new GridFrame(center.x, center.z, rotation, spacing);
        }

        private void QueueReplant(Pickable crop, PlantInfo info)
        {
            // Objects such as berry bushes stay where they are after picking; only crops grown from a plant are replanted.
            if (!settings.Replant.Value || info == null || info.IsObject || !crop || replants.Count >= MaxReplants) return;
            replants.Add(new Replanting { Crop = crop, Info = info, Position = crop.transform.position, Time = Time.time });
        }

        // Replants once the picked crop is gone (the owner destroys it after the pick), checking the cell, the
        // seeds, the planting tool and the stamina like a normal planting.
        private void UpdateReplants(Player player)
        {
            if (replants.Count == 0) return;
            if (!player || halted)
            {
                replants.Clear();
                return;
            }
            float now = Time.time;
            int planted = 0;
            int missing = 0;
            for (int i = replants.Count - 1; i >= 0; i--)
            {
                Replanting entry = replants[i];
                if (now - entry.Time < ReplantDelay) continue;
                if (entry.Crop && now - entry.Time < ReplantTimeout) continue;
                replants.RemoveAt(i);
                if (entry.Crop || !entry.Info.Piece) continue;
                switch (TryReplant(player, entry))
                {
                    case 1: planted++; break;
                    case -1: missing++; break;
                }
            }
            if (planted > 0) player.Message(MessageHud.MessageType.TopLeft, $"Replanted {planted}");
            else if (missing > 0) player.Message(MessageHud.MessageType.TopLeft, "Not replanted: no seeds, stamina or planting tool");
        }

        // 1 planted, 0 cell not free, -1 lacking seeds, stamina or tool.
        private int TryReplant(Player player, Replanting entry)
        {
            Piece piece = entry.Info.Piece;
            if (Field.Evaluate(entry.Info, entry.Position.x, entry.Position.z, 0f, entry.Position.y, false, out Vector3 position) != CellStatus.Ok) return 0;
            PieceTable tool = PlantingTool(player, piece);
            if (tool == null) return -1;
            bool freeKey = ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
            if (SeedBudget(player, piece, freeKey) < 1) return -1;
            float stamina = StaminaPerPlant(player);
            if (stamina > 0f && !player.HaveStamina(stamina * Game.m_staminaRate)) return -1;

            bool cheated = (player.GetInventory().ItemCheated(piece.m_resources) || player.NoCostCheat()) && !PlayerProfile.s_bypassCheatChecks;
            if (Game.instance)
            {
                Game.instance.IncrementPlayerStat(PlayerStatType.Builds, 1f, cheated);
                Game.instance.GetPlayerProfile()?.IncrementStatBuildPiecePlaced(piece.m_name, 1f, cheated);
            }
            Quaternion rotation = piece.m_randomInitBuildRotation
                ? Quaternion.Euler(0f, UnityEngine.Random.Range(0, 16) * 22.5f, 0f)
                : Quaternion.identity;
            player.PlacePiece(piece, position, rotation, false, cheated);
            if (!freeKey) player.ConsumeResources(piece.m_resources, 0);
            if (stamina > 0f) player.UseStamina(stamina);
            // The skill a planting with that tool raises (Farming for the Cultivator), as vanilla UpdatePlacement does.
            RaiseSkill(player, tool.m_skill, 1);
            return 1;
        }

        // Vanilla plants only with a tool whose build menu has the seed; replanting asks the same. Returns that
        // tool's build menu, or null.
        private static PieceTable PlantingTool(Player player, Piece piece)
        {
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                PieceTable table = item?.m_shared?.m_buildPieces;
                if (table == null || table.m_pieces == null) continue;
                foreach (GameObject prefab in table.m_pieces)
                    if (prefab == piece.gameObject) return table;
            }
            return null;
        }
    }
}
