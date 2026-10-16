using System.Collections.Generic;
using UnityEngine;

namespace Fieldsmith
{
    // Removing wild plants a player placed with the Cultivator: the remove button removes the one under the aim,
    // Alt place (Left Shift) + remove every one within [Plants] RemoveRadius of the aim. Crops, saplings,
    // buildings and wild plants are never removed. Mirrors vanilla Player.RemovePiece (no-build zone, ward,
    // ownership, DropResources, effects, destroy) with an aim that also hits the item layer, where mushrooms,
    // flowers and debris have their colliders, and picks a ripe one first so its contents are not lost. Then
    // charges what vanilla UpdatePlacement charges after a removal, once per plant: stamina, the remove debt of
    // the tool's skill and tool wear; noise, the swing and the tool's destroy effect once per press.
    internal static class FloraRemoval
    {
        // Vanilla RemovePiece raycast length and UpdatePlacement's cap on the remove debt.
        private const float RayDistance = 50f;
        private const int MaxRemoveDebt = 20;
        // Most plants one press removes, so a large radius cannot stall a frame.
        internal const int MaxPerPress = 100;
        private static int mask;
        private static readonly Collider[] overlaps = new Collider[512];
        private static readonly List<Piece> found = new List<Piece>();
        private static readonly List<float> distances = new List<float>();

        // Remove button: the plant under the aim.
        internal static void Single(Player player)
        {
            if (!Aim(player, out RaycastHit hit)) return;
            Piece piece = hit.collider.GetComponentInParent<Piece>();
            if (!Removable(piece)) return;
            if (!Allowed(player, piece.transform.position, true) || !TakeStamina(player, vanillaChecked: true)) return;
            if (RemoveOne(player, piece)) Finish(player, piece.transform.position, 1);
        }

        // Alt place + remove: every placed wild plant within the radius of the aim point, nearest first, as long
        // as the stamina lasts.
        internal static void Area(Player player, float radius)
        {
            if (!Aim(player, out RaycastHit hit)) return;
            Vector3 center = hit.point;
            found.Clear();
            distances.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, Mask(), QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Piece piece = overlaps[i] ? overlaps[i].GetComponentInParent<Piece>() : null;
                if (!Removable(piece) || found.Contains(piece)) continue;
                Vector3 offset = piece.transform.position - center;
                offset.y = 0f;
                if (offset.magnitude > radius) continue;
                found.Add(piece);
                distances.Add(offset.sqrMagnitude);
            }
            if (found.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "No planted wild plants here");
                return;
            }
            SortByDistance();
            int removed = 0;
            int protectedCount = 0;
            bool tired = false;
            foreach (Piece piece in found)
            {
                if (removed >= MaxPerPress) break;
                if (!piece) continue;
                if (!Allowed(player, piece.transform.position, removed == 0 && protectedCount == 0))
                {
                    protectedCount++;
                    continue;
                }
                if (!TakeStamina(player, vanillaChecked: false))
                {
                    tired = true;
                    break;
                }
                if (RemoveOne(player, piece)) removed++;
            }
            found.Clear();
            distances.Clear();
            if (removed > 0) Finish(player, center, removed);
            string text = $"Removed {removed} plant{(removed == 1 ? string.Empty : "s")}";
            if (protectedCount > 0) text += $", {protectedCount} protected";
            if (tired) text += ", not enough stamina";
            player.Message(MessageHud.MessageType.TopLeft, text);
        }

        private static bool Aim(Player player, out RaycastHit hit)
        {
            hit = default;
            GameCamera camera = GameCamera.instance;
            if (!camera) return false;
            Transform view = camera.transform;
            return Physics.Raycast(view.position, view.forward, out hit, RayDistance, Mask()) &&
                Vector3.Distance(hit.point, player.m_eye.position) < player.m_maxPlaceDistance;
        }

        private static bool Removable(Piece piece) => piece && Flora.IsAdded(piece) && piece.IsPlacedByPlayer();

        // Vanilla RemovePiece's checks; the ward flashes and the no-build message shows for the first plant only.
        private static bool Allowed(Player player, Vector3 position, bool feedback)
        {
            if (Location.IsInsideNoBuildLocation(position))
            {
                if (feedback) player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
                return false;
            }
            return PrivateArea.CheckAccess(position, 0f, feedback);
        }

        // Vanilla UpdatePlacement checks the tool's stamina before calling RemovePiece, not before CopyPiece, so
        // every plant of an area removal is checked here.
        private static bool TakeStamina(Player player, bool vanillaChecked)
        {
            if (GameAccess.BuildStamina == null) return true;
            float stamina = GameAccess.BuildStamina(player);
            if (!vanillaChecked && !player.HaveStamina(stamina)) return false;
            player.UseStamina(stamina);
            return true;
        }

        private static bool RemoveOne(Player player, Piece piece)
        {
            ZNetView view = piece.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return false;
            Vector3 position = piece.transform.position;
            view.ClaimOwnership();
            Pickable pickable = piece.GetComponent<Pickable>();
            // RPC_Pick goes to the owner, now this client, and runs at once (ZRoutedRpc handles its own id locally).
            if (pickable && pickable.CanBePicked()) pickable.Interact(player, false, false);
            piece.DropResources();
            piece.m_placeEffect.Create(position, piece.transform.rotation, null, 1f, -1, player.GetZDOID());
            player.m_removeEffects.Create(position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
            ZNetScene.instance.Destroy(piece.gameObject);

            PieceTable table = player.GetBuildTool();
            if (table != null && table.m_skill != Skills.SkillType.None && GameAccess.BuildRemoveDebt != null)
            {
                ref int debt = ref GameAccess.BuildRemoveDebt(player);
                if (debt < MaxRemoveDebt) debt++;
            }
            ItemDrop.ItemData tool = GameAccess.RightItem != null ? GameAccess.RightItem(player) : null;
            if (tool != null && tool.m_shared.m_useDurability && GameAccess.PlaceDurability != null)
                tool.m_durability -= GameAccess.PlaceDurability(player, tool) * Game.m_durabilityRate;
            return true;
        }

        private static void Finish(Player player, Vector3 position, int removed)
        {
            ItemDrop.ItemData tool = GameAccess.RightItem != null ? GameAccess.RightItem(player) : null;
            player.FaceLookDirection();
            ZSyncAnimation animator = GameAccess.Animator != null ? GameAccess.Animator(player) : null;
            if (tool != null && animator) animator.SetTrigger(tool.m_shared.m_attack.m_attackAnimation);
            player.AddNoise(50f);
            if (tool != null) tool.m_shared.m_destroyEffect.Create(position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        }

        private static void SortByDistance()
        {
            for (int i = 1; i < found.Count; i++)
            {
                Piece piece = found[i];
                float distance = distances[i];
                int j = i - 1;
                while (j >= 0 && distances[j] > distance)
                {
                    found[j + 1] = found[j];
                    distances[j + 1] = distances[j];
                    j--;
                }
                found[j + 1] = piece;
                distances[j + 1] = distance;
            }
        }

        // Vanilla Player.m_removeRayMask plus "item".
        private static int Mask()
        {
            if (mask == 0)
                mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle", "item");
            return mask;
        }
    }
}
