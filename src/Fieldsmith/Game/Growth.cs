using System.Collections.Generic;
using BepInEx.Configuration;

namespace Fieldsmith
{
    // [Growth] GrowTimeHours + GrowTimeMinutes: a fixed grow time for every plant (Plant.GetGrowTime, patched)
    // and a fixed regrow time for the wild plants a player placed (their own Pickable.m_respawnTimeMinutes).
    // Growing and regrowing happen on the object's owner, so the setting applies while a player with this
    // setting owns the object.
    internal static class Growth
    {
        private static ConfigEntry<int> hours;
        private static ConfigEntry<int> minutes;
        // Placed wild plants and their game regrow time, so a changed setting applies to them at once.
        private static readonly Dictionary<Pickable, float> placed = new Dictionary<Pickable, float>();

        internal static void Initialize(ConfigEntry<int> hoursEntry, ConfigEntry<int> minutesEntry)
        {
            hours = hoursEntry;
            minutes = minutesEntry;
            hoursEntry.SettingChanged += (_, __) => ApplyAll();
            minutesEntry.SettingChanged += (_, __) => ApplyAll();
        }

        // The configured time in minutes; 0 keeps the game's times.
        private static double Minutes => hours == null || minutes == null ? 0 : GrowthTime.TotalMinutes(hours.Value, minutes.Value);

        // Grow time in seconds for every plant, or NaN for the plant's own.
        internal static double PlantSeconds => GrowthTime.PlantSeconds(Minutes);

        internal static void Track(Pickable pickable)
        {
            if (!pickable || minutes == null) return;
            if (!placed.TryGetValue(pickable, out float game))
            {
                Prune();
                game = pickable.m_respawnTimeMinutes;
                placed[pickable] = game;
            }
            pickable.m_respawnTimeMinutes = (float)GrowthTime.RegrowMinutes(game, Minutes);
        }

        private static void ApplyAll()
        {
            Prune();
            double configured = Minutes;
            foreach (KeyValuePair<Pickable, float> pair in placed)
                pair.Key.m_respawnTimeMinutes = (float)GrowthTime.RegrowMinutes(pair.Value, configured);
        }

        // Destroyed objects compare equal to null.
        private static void Prune()
        {
            List<Pickable> dead = null;
            foreach (Pickable pickable in placed.Keys)
                if (!pickable) (dead ??= new List<Pickable>()).Add(pickable);
            if (dead != null) foreach (Pickable pickable in dead) placed.Remove(pickable);
        }

        internal static void Clear() => placed.Clear();
    }
}
