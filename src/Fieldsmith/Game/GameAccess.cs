using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Fieldsmith
{
    // Private game members, resolved once at startup and verified against docs/CONTRACTS.md.
    // A missing required member disables the mod instead of failing during play.
    internal static class GameAccess
    {
        internal static AccessTools.FieldRef<Player, GameObject> PlacementGhost;
        internal static AccessTools.FieldRef<Player, Player.PlacementStatus> PlacementStatus;
        internal static Action<Player, bool> UpdatePlacementGhost;
        internal static Action<Player, bool> SetPlacementGhostValid;

        // Optional: extra tool wear per plant depends on these two.
        internal static Func<Humanoid, ItemDrop.ItemData> RightItem;
        internal static Func<Player, ItemDrop.ItemData, float> PlaceDurability;
        // Optional: vanilla's guard against raising the build skill by placing and removing.
        internal static AccessTools.FieldRef<Player, int> BuildRemoveDebt;
        // Optional: the grow timer in the plant hover text depends on these two.
        internal static Func<Plant, double> TimeSincePlanted;
        internal static Func<Plant, float> GrowTime;
        // Optional: removing wild plants with the Cultivator charges the vanilla stamina and plays the swing.
        internal static Func<Player, float> BuildStamina;
        internal static AccessTools.FieldRef<Character, ZSyncAnimation> Animator;

        internal static bool Ready { get; private set; }
        internal static bool WearAvailable => RightItem != null && PlaceDurability != null;

        // Returns null on success, otherwise the missing members.
        internal static string Resolve()
        {
            var missing = new List<string>();
            PlacementGhost = Field<Player, GameObject>("m_placementGhost", missing);
            PlacementStatus = Field<Player, Player.PlacementStatus>("m_placementStatus", missing);
            UpdatePlacementGhost = Method<Action<Player, bool>>(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) }, missing);
            SetPlacementGhostValid = Method<Action<Player, bool>>(typeof(Player), "SetPlacementGhostValid", new[] { typeof(bool) }, missing);
            // Patched (GetBuildStaminaPatch); checked here so a renamed method disables the mod cleanly.
            BuildStamina = Method<Func<Player, float>>(typeof(Player), "GetBuildStamina", Type.EmptyTypes, missing);

            var optional = new List<string>();
            RightItem = Method<Func<Humanoid, ItemDrop.ItemData>>(typeof(Humanoid), "GetRightItem", Type.EmptyTypes, optional);
            PlaceDurability = Method<Func<Player, ItemDrop.ItemData, float>>(typeof(Player), "GetPlaceDurability", new[] { typeof(ItemDrop.ItemData) }, optional);
            BuildRemoveDebt = Field<Player, int>("m_buildRemoveDebt", optional);
            TimeSincePlanted = Method<Func<Plant, double>>(typeof(Plant), "TimeSincePlanted", Type.EmptyTypes, optional);
            GrowTime = Method<Func<Plant, float>>(typeof(Plant), "GetGrowTime", Type.EmptyTypes, optional);
            Animator = Field<Character, ZSyncAnimation>("m_zanim", optional);

            Ready = missing.Count == 0;
            if (!Ready) return string.Join(", ", missing.ToArray());
            return optional.Count == 0 ? null : "optional:" + string.Join(", ", optional.ToArray());
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name, List<string> missing) where T : class
        {
            FieldInfo field = AccessTools.Field(typeof(T), name);
            if (field == null || field.IsStatic || field.FieldType != typeof(F))
            {
                missing.Add(typeof(T).Name + "." + name);
                return null;
            }
            return AccessTools.FieldRefAccess<T, F>(field);
        }

        private static TDelegate Method<TDelegate>(Type type, string name, Type[] parameters, List<string> missing) where TDelegate : Delegate
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
            if (method == null || method.IsStatic || method.ReturnType != invoke.ReturnType)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
            try
            {
                return AccessTools.MethodDelegate<TDelegate>(method);
            }
            catch (Exception)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
        }
    }
}
