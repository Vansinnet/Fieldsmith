using System;
using System.Collections.Generic;

namespace Fieldsmith
{
    // How many plants one click may place, given the free cells, seeds and stamina.
    internal static class Budget
    {
        internal const int Unlimited = int.MaxValue;

        // Plants the seeds pay for. Each entry is (items in the inventory, items per plant); entries needing
        // nothing are ignored. Free building means unlimited.
        internal static int Seeds(IList<(int Have, int PerPlant)> requirements, bool free)
        {
            if (free || requirements == null) return Unlimited;
            int result = Unlimited;
            foreach (var (have, perPlant) in requirements)
            {
                if (perPlant <= 0) continue;
                result = Math.Min(result, Math.Max(0, have) / perPlant);
            }
            return result;
        }

        // Plants the current stamina pays for in full. The first plant is always allowed: vanilla has already
        // checked the tool's minimum stamina before the click, and a single vanilla placement may drain to zero.
        internal static int Stamina(double current, double perPlant)
        {
            if (!(perPlant > 0) || double.IsInfinity(perPlant)) return Unlimited;
            if (double.IsNaN(current) || current <= 0) return 1;
            double count = Math.Floor(current / perPlant + 1e-9);
            return (int)Math.Max(1, Math.Min(Unlimited, count));
        }

        internal static int Plants(int freeCells, int seeds, int stamina) =>
            Math.Max(0, Math.Min(freeCells, Math.Min(seeds, stamina)));

        // Stamina per plant after the vanilla build skill reduction (up to half at skill level 100).
        internal static double StaminaPerPlant(double configured, double skillFactor, bool applySkill)
        {
            if (!(configured > 0) || double.IsInfinity(configured)) return 0;
            if (!applySkill || double.IsNaN(skillFactor)) return configured;
            double factor = Math.Max(0, Math.Min(1, skillFactor));
            return configured - configured * 0.5 * factor;
        }
    }
}
