using System;
using UnityEngine;

namespace Fieldsmith
{
    // Time left until a plant grows, from the same values vanilla uses: Plant.SUpdate grows the plant once
    // TimeSincePlanted() (server time since planting) passes GetGrowTime() (fixed per plant by its seed) and the
    // plant is healthy. The check runs every 10 seconds, so the plant appears up to 10 seconds after zero.
    internal static class GrowTimer
    {
        internal static string Text(Plant plant)
        {
            if (!plant || GameAccess.TimeSincePlanted == null || GameAccess.GrowTime == null || !ZNet.instance) return null;
            ZNetView view = plant.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return null;
            double left = GameAccess.GrowTime(plant) - GameAccess.TimeSincePlanted(plant);
            if (double.IsNaN(left) || double.IsInfinity(left)) return null;
            bool healthy = plant.GetStatus() == Plant.Status.Healthy;
            string color = healthy ? "#9be07a" : "#ffb050";
            string time = left > 0 ? "Fully grown in " + Steps.Duration(left) : healthy ? "Fully grown within seconds" : "Ready to grow once healthy";
            if (!healthy && left > 0) time += " (if healthy)";
            return "<color=" + color + ">" + time + "</color>";
        }

        // A picked berry bush, mushroom, flower or debris that grows back: its name and the time left. Vanilla
        // shows nothing for a picked pickable. The owner's respawn check runs every 60 seconds.
        internal static string Text(Pickable pickable)
        {
            if (!pickable || !pickable.GetPicked() || pickable.GetEnabled == 0 || !(pickable.m_respawnTimeMinutes > 0) || !ZNet.instance) return null;
            ZNetView view = pickable.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return null;
            long picked = view.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L);
            double left = Respawn.SecondsLeft(picked, ZNet.instance.GetTime().Ticks, pickable.m_respawnTimeMinutes);
            if (double.IsNaN(left)) return null;
            string time = left > 0 ? "Grows back in " + Steps.Duration(left) : "Grows back within a minute";
            // The same name as Pickable.GetHoverName, without its exception for a prefab lacking an ItemDrop.
            string name = pickable.m_overrideName;
            if (string.IsNullOrEmpty(name))
            {
                ItemDrop item = pickable.m_itemPrefab ? pickable.m_itemPrefab.GetComponent<ItemDrop>() : null;
                name = item ? item.m_itemData.m_shared.m_name : null;
            }
            name = Plants.Localize(name);
            string line = "<color=#9be07a>" + time + "</color>";
            return string.IsNullOrEmpty(name) ? line : name + "\n" + line;
        }
    }
}
