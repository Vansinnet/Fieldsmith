using System;
using System.Collections.Generic;
using System.Globalization;

namespace Fieldsmith
{
    internal static class Steps
    {
        internal static double Adjust(double value, int steps, double step, double min, double max)
        {
            if (!(step > 0) || !GridFrame.Finite(value)) return Clamp(value, min, max);
            double moved = Math.Round(Math.Round((value + steps * step) / step) * step, 6);
            return Clamp(moved, min, max);
        }

        internal static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            return Math.Min(max, Math.Max(min, value));
        }

        // Minutes and seconds, rounded up ("4:05"); hours are added for long timers ("1:02:03").
        internal static string Duration(double seconds)
        {
            if (double.IsNaN(seconds)) seconds = 0;
            long total = (long)Math.Ceiling(Math.Min(Math.Max(0, seconds), 1e9));
            long hours = total / 3600;
            long minutes = total / 60 % 60;
            long rest = total % 60;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, rest)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, rest);
        }

        // Mirrors the vanilla piece-rotation scroll handling: input accumulates until it passes the threshold,
        // then exactly one step is taken and the accumulator restarts.
        internal static int ConsumeScroll(ref double accumulated, double delta, double threshold)
        {
            if (!GridFrame.Finite(delta) || !(threshold > 0)) return 0;
            accumulated += delta;
            if (accumulated > threshold)
            {
                accumulated = 0;
                return 1;
            }
            if (accumulated < -threshold)
            {
                accumulated = 0;
                return -1;
            }
            return 0;
        }
    }

    internal static class Undoing
    {
        // Picks within this time of the click belong to the placement itself (objects placed already picked).
        internal const long PlacementTicks = TimeSpan.TicksPerSecond;

        // Whether an object was picked after the click that placed it, from its picked flag and the server time
        // of its last pick (0 when none was stored). A pick after the click counts even when the object has
        // grown back since; a picked object without a stored time counts as picked after the click.
        internal static bool PickedSince(bool picked, long pickedTicks, long clickTicks)
        {
            if (pickedTicks > 0) return pickedTicks > clickTicks + PlacementTicks;
            return picked;
        }
    }

    // Bounded last-in-first-out history. The oldest entry is dropped when the capacity is exceeded.
    internal sealed class UndoHistory<T>
    {
        private readonly LinkedList<T> entries = new LinkedList<T>();
        private int capacity;

        internal UndoHistory(int capacity)
        {
            this.capacity = Math.Max(0, capacity);
        }

        internal int Count => entries.Count;

        internal int Capacity
        {
            get => capacity;
            set
            {
                capacity = Math.Max(0, value);
                while (entries.Count > capacity) entries.RemoveFirst();
            }
        }

        internal void Push(T entry)
        {
            if (capacity == 0) return;
            entries.AddLast(entry);
            while (entries.Count > capacity) entries.RemoveFirst();
        }

        internal bool TryPeek(out T entry)
        {
            entry = entries.Count == 0 ? default : entries.Last.Value;
            return entries.Count > 0;
        }

        internal bool TryPop(out T entry)
        {
            if (!TryPeek(out entry)) return false;
            entries.RemoveLast();
            return true;
        }

        internal void Clear() => entries.Clear();
    }
}
