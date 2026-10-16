using System;

namespace Fieldsmith
{
    // The configured growth time ([Growth] GrowTimeHours + GrowTimeMinutes): one fixed time for every plant and
    // for the regrowth of the wild plants a player placed. 0 keeps the game's own times.
    internal static class GrowthTime
    {
        internal const int MaxHours = 24;
        internal const double MaxMinutes = MaxHours * 60;

        // Hours and minutes as one time in minutes (1 h 55 min = 115), each part kept in its range.
        internal static double TotalMinutes(int hours, int minutes) =>
            Math.Min(MaxMinutes, Math.Max(0, Math.Min(MaxHours, hours)) * 60.0 + Math.Max(0, Math.Min(59, minutes)));

        // Grow time in seconds for every plant, or NaN to keep the plant's own.
        internal static double PlantSeconds(double minutes) =>
            Valid(minutes) ? minutes * 60 : double.NaN;

        // Regrow time in minutes of a placed wild plant. Plants that never grow back in the game (a stone) keep
        // their 0: the game removes them when picked.
        internal static double RegrowMinutes(double gameMinutes, double minutes)
        {
            if (!(gameMinutes > 0) || double.IsInfinity(gameMinutes)) return gameMinutes;
            return Valid(minutes) ? minutes : gameMinutes;
        }

        private static bool Valid(double minutes) => minutes > 0 && !double.IsInfinity(minutes) && minutes <= MaxMinutes;
    }
}
