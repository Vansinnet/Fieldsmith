using System;
using HarmonyLib;
using Splatform;

namespace Fieldsmith
{
    // Plants with the grid on: Fieldsmith places the whole pattern instead of the vanilla single placement and
    // returns true when at least one plant was placed. Vanilla UpdatePlacement then charges one plant's seeds,
    // the stamina, one skill raise and one tool wear; Fieldsmith charges the rest itself.
    // Other plants (grid off, wall vines) go through vanilla; only the stamina setting applies to them.
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece), new[] { typeof(Piece) })]
    internal static class TryPlacePiecePatch
    {
        // __state: true when Fieldsmith handled the click and the original was skipped.
        private static bool Prefix(Player __instance, Piece piece, ref bool __result, out bool __state)
        {
            __state = false;
            Controller controller = Plugin.Controller;
            if (controller == null || !controller.Handles(__instance, piece, out PlantInfo info)) return true;
            __state = true;
            try
            {
                bool runOriginal = controller.TryPlace(__instance, info, ref __result);
                __state = !runOriginal;
                return runOriginal;
            }
            catch (Exception error)
            {
                // __result is already true if plants were placed, so vanilla still charges the first one.
                Plugin.Fail(error);
                return false;
            }
        }

        private static void Postfix(Player __instance, Piece piece, bool __result, bool __state)
        {
            Controller controller = Plugin.Controller;
            if (__state || !__result || controller == null || controller.Halted || __instance != Player.m_localPlayer) return;
            if (Controller.IsPlant(piece)) controller.PendingPlants = 1;
        }
    }

    // Stamina for a planting click: the plants placed times the configured stamina per plant. Only replaces the
    // value right after a planting succeeded; removing and repairing keep the vanilla cost.
    [HarmonyPatch(typeof(Player), "GetBuildStamina")]
    internal static class GetBuildStaminaPatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            Controller controller = Plugin.Controller;
            if (controller != null && controller.TryTakeStamina(__instance, out float stamina)) __result = stamina;
        }
    }

    // Snaps the ghost to the grid after every vanilla ghost update.
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) })]
    internal static class UpdatePlacementGhostPatch
    {
        private static void Postfix(Player __instance)
        {
            Controller controller = Plugin.Controller;
            if (controller == null || controller.Halted || __instance != Player.m_localPlayer) return;
            try
            {
                controller.OnGhostUpdated(__instance);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // Collects the plants placed by one click (vanilla PlacePiece sets the creator on each new piece), so the
    // click can be undone.
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator), new[] { typeof(long), typeof(PlatformUserID) })]
    internal static class SetCreatorPatch
    {
        private static void Postfix(Piece __instance)
        {
            Controller controller = Plugin.Controller;
            if (controller != null && controller.Capturing) controller.Capture(__instance);
            try
            {
                Flora.ApplyPlaced(__instance);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // Wild plants (Flora): the Cultivator gets them once both the scene's prefabs and the item database exist;
    // either may wake up first.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix() => FloraHooks.Install();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        private static void Postfix() => FloraHooks.Install();
    }

    // Placed wild plants loaded from the world: yield, regrowth and marker are set before vanilla Pickable.Awake
    // reads them (it removes a picked pickable that never respawns, and starts the respawn check only for one
    // that does).
    [HarmonyPatch(typeof(Pickable), "Awake")]
    internal static class PickableAwakePatch
    {
        private static void Prefix(Pickable __instance)
        {
            try
            {
                Flora.ApplyLoaded(__instance);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // Remove button with the Cultivator (Fieldsmith turns on its m_canRemovePieces): only wild plants a player
    // placed are removed, by Flora.Remove; the original never runs for the Cultivator, so crops, saplings and
    // buildings cannot be removed with it. __result stays false: vanilla UpdatePlacement would otherwise read
    // its hovering piece, which is null for mushrooms and flowers (item layer), and Flora.Remove already charged
    // the removal.
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    internal static class RemovePiecePatch
    {
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (__instance != Player.m_localPlayer || !Flora.RemovesWith(__instance)) return true;
            __result = false;
            try
            {
                FloraRemoval.Single(__instance);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
            return false;
        }
    }

    // Alt place + remove button: vanilla UpdatePlacement calls CopyPiece (select the aimed piece) and blocks the
    // removal. With the Cultivator Fieldsmith removes every planted wild plant around the aim instead. CopyPiece
    // is also called for a gamepad Alt + place, which is left to vanilla.
    [HarmonyPatch(typeof(Player), "CopyPiece")]
    internal static class CopyPiecePatch
    {
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (__instance != Player.m_localPlayer || !Flora.RemovesWith(__instance)) return true;
            if (!ZInput.GetButtonDown("Remove") && !ZInput.GetButtonDown("JoyRemove")) return true;
            __result = false;
            try
            {
                FloraRemoval.Area(__instance, Plugin.RemoveRadius != null ? Plugin.RemoveRadius.Value : 3f);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
            return false;
        }
    }

    // Wild plants carry a Piece on this client, so the hammer would remove wild ones; only placed ones may be.
    [HarmonyPatch(typeof(Player), "CheckCanRemovePiece", new[] { typeof(Piece) })]
    internal static class CheckCanRemovePiecePatch
    {
        private static void Postfix(Piece piece, ref bool __result)
        {
            if (__result && piece && !piece.IsPlacedByPlayer() && Flora.IsAdded(piece)) __result = false;
        }
    }

    internal static class FloraHooks
    {
        internal static void Install()
        {
            try
            {
                Flora.Install();
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // Block harvest: after vanilla picks the crop, Fieldsmith picks the ripe crops around it when the Alt place
    // key was held (the game passes it as alt).
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact), new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    internal static class PickableInteractPatch
    {
        private static void Postfix(Pickable __instance, Humanoid character, bool repeat, bool alt)
        {
            Controller controller = Plugin.Controller;
            if (controller == null || controller.Halted || !alt || repeat) return;
            try
            {
                controller.OnPicked(__instance, character, repeat, alt);
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // Grow timer: appended to the vanilla plant hover text (name and health), which the HUD reads every frame.
    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    internal static class PlantHoverTextPatch
    {
        private static void Postfix(Plant __instance, ref string __result)
        {
            Controller controller = Plugin.Controller;
            if (controller == null || controller.Halted || !controller.ShowGrowTimer) return;
            try
            {
                string timer = GrowTimer.Text(__instance);
                if (timer != null) __result = string.IsNullOrEmpty(__result) ? timer : __result + "\n" + timer;
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // [Growth] GrowTimeMinutes: Plant.SUpdate grows a plant once TimeSincePlanted() passes GetGrowTime() (and
    // swaps to the half-grown model at half of it); the grow timer reads the same value.
    [HarmonyPatch(typeof(Plant), "GetGrowTime")]
    internal static class PlantGrowTimePatch
    {
        private static void Postfix(ref float __result)
        {
            double seconds = Growth.PlantSeconds;
            if (!double.IsNaN(seconds)) __result = (float)seconds;
        }
    }

    // Regrow timer: a picked pickable that grows back shows its name and the time left instead of nothing.
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
    internal static class PickableHoverTextPatch
    {
        private static void Postfix(Pickable __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result)) return;
            Controller controller = Plugin.Controller;
            if (controller == null || controller.Halted || !controller.ShowGrowTimer) return;
            try
            {
                string timer = GrowTimer.Text(__instance);
                if (timer != null) __result = timer;
            }
            catch (Exception error)
            {
                Plugin.Fail(error);
            }
        }
    }

    // GameCamera zooms with the mouse wheel unless the selected piece can rotate. While a Fieldsmith modifier
    // key is held the wheel changes the pattern instead, so zooming is suppressed for that frame.
    [HarmonyPatch(typeof(Player), nameof(Player.CanRotatePiece))]
    internal static class CanRotatePiecePatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!__result && __instance == Player.m_localPlayer && Plugin.Controller != null && Plugin.Controller.ConsumesScroll)
                __result = true;
        }
    }
}
