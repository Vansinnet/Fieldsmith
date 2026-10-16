using System;

namespace Fieldsmith
{
    // When a picked pickable grows back, from the values vanilla uses: Pickable.ShouldRespawn lets it respawn
    // once more than m_respawnTimeMinutes have passed since the pick time stored in the ZDO (a time of 0 or 1
    // means at once), and the owner checks that every 60 seconds.
    internal static class Respawn
    {
        internal const double CheckIntervalSeconds = 60;

        // Seconds left until the pickable may respawn; 0 when it respawns at the owner's next check, and
        // NaN when it never respawns.
        internal static double SecondsLeft(long pickedTicks, long nowTicks, double respawnMinutes)
        {
            if (!(respawnMinutes > 0) || double.IsInfinity(respawnMinutes)) return double.NaN;
            if (pickedTicks <= 1) return 0;
            double elapsed = (double)(nowTicks - pickedTicks) / TimeSpan.TicksPerSecond;
            return Math.Max(0, respawnMinutes * 60 - elapsed);
        }
    }
}
