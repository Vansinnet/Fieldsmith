using System;
using System.Collections.Generic;

namespace Fieldsmith
{
    // A square planting grid in the horizontal plane. Rotation is a yaw in degrees (clockwise seen from above,
    // like Unity's Y rotation): the V axis points along the yaw, the U axis 90 degrees to its right.
    // A square grid repeats every 90 degrees, so rotations are kept in [0, 90).
    internal readonly struct GridFrame
    {
        internal readonly double OriginX;
        internal readonly double OriginZ;
        internal readonly double RotationDegrees;
        internal readonly double Spacing;

        internal GridFrame(double originX, double originZ, double rotationDegrees, double spacing)
        {
            OriginX = Finite(originX) ? originX : 0;
            OriginZ = Finite(originZ) ? originZ : 0;
            RotationDegrees = GridMath.NormalizeQuarter(rotationDegrees);
            Spacing = Finite(spacing) && spacing > GridMath.MinSpacing ? spacing : GridMath.MinSpacing;
        }

        internal GridFrame WithSpacing(double spacing) => new GridFrame(OriginX, OriginZ, RotationDegrees, spacing);

        internal GridFrame WithRotation(double degrees) => new GridFrame(OriginX, OriginZ, degrees, Spacing);

        // Continuous grid coordinates of a world point, in cells.
        internal void ToGrid(double x, double z, out double u, out double v)
        {
            double radians = RotationDegrees * Math.PI / 180;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double dx = x - OriginX;
            double dz = z - OriginZ;
            u = (dx * cos - dz * sin) / Spacing;
            v = (dx * sin + dz * cos) / Spacing;
        }

        internal void ToWorld(double u, double v, out double x, out double z)
        {
            double radians = RotationDegrees * Math.PI / 180;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            x = OriginX + Spacing * (u * cos + v * sin);
            z = OriginZ + Spacing * (-u * sin + v * cos);
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal struct Cell
    {
        internal int I;
        internal int J;
        internal double X;
        internal double Z;
        internal double DistanceSquared;
    }

    internal static class GridMath
    {
        internal const double MinSpacing = 0.1;
        internal const int MaxPatternSize = 10;

        internal static double NormalizeQuarter(double degrees)
        {
            if (!GridFrame.Finite(degrees)) return 0;
            double result = degrees % 90;
            if (result < 0) result += 90;
            return Math.Round(result, 6) >= 90 ? 0 : Math.Round(result, 6);
        }

        // The cell whose centre is nearest to the point.
        internal static Cell Nearest(GridFrame frame, double x, double z)
        {
            frame.ToGrid(x, z, out double u, out double v);
            int i = (int)Math.Round(u, MidpointRounding.AwayFromZero);
            int j = (int)Math.Round(v, MidpointRounding.AwayFromZero);
            return MakeCell(frame, i, j, x, z);
        }

        // An n x n block of cells around the point, nearest cell first, then by distance to the point.
        // Odd sizes are centred on the nearest cell; even sizes on the grid corner nearest to the point, so the
        // block always lies around the aim and the nearest cell is always part of it.
        internal static void Pattern(GridFrame frame, double x, double z, int size, List<Cell> output)
        {
            output.Clear();
            size = Math.Max(1, Math.Min(MaxPatternSize, size));
            PatternStart(frame, x, z, size, out int startI, out int startJ);
            for (int a = 0; a < size; a++)
                for (int b = 0; b < size; b++)
                    output.Add(MakeCell(frame, startI + a, startJ + b, x, z));
            output.Sort(ByDistance);
        }

        // First cell (lowest I and J) of the n x n block around the point.
        internal static void PatternStart(GridFrame frame, double x, double z, int size, out int startI, out int startJ)
        {
            size = Math.Max(1, Math.Min(MaxPatternSize, size));
            frame.ToGrid(x, z, out double u, out double v);
            if (size % 2 == 1)
            {
                int half = (size - 1) / 2;
                startI = (int)Math.Round(u, MidpointRounding.AwayFromZero) - half;
                startJ = (int)Math.Round(v, MidpointRounding.AwayFromZero) - half;
            }
            else
            {
                int half = size / 2;
                startI = (int)Math.Floor(u) - half + 1;
                startJ = (int)Math.Floor(v) - half + 1;
            }
        }

        private static readonly Comparison<Cell> ByDistance = CompareDistance;

        private static int CompareDistance(Cell a, Cell b)
        {
            int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (byDistance != 0) return byDistance;
            int byI = a.I.CompareTo(b.I);
            return byI != 0 ? byI : a.J.CompareTo(b.J);
        }

        private static Cell MakeCell(GridFrame frame, int i, int j, double x, double z)
        {
            frame.ToWorld(i, j, out double cx, out double cz);
            double dx = cx - x;
            double dz = cz - z;
            return new Cell { I = i, J = j, X = cx, Z = cz, DistanceSquared = dx * dx + dz * dz };
        }

        // Grid rotation of an existing row: the yaw from one plant to its neighbour, folded into [0, 90).
        // Returns false unless the neighbour is about one spacing away (diagonal neighbours are ignored).
        internal static bool TryRowRotation(double ax, double az, double bx, double bz, double spacing, out double rotation)
        {
            rotation = 0;
            double dx = bx - ax;
            double dz = bz - az;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (!GridFrame.Finite(distance) || !(spacing > 0)) return false;
            if (distance < spacing * 0.75 || distance > spacing * 1.25) return false;
            double yaw = Math.Atan2(dx, dz) * 180 / Math.PI;
            rotation = NormalizeQuarter(Math.Round(yaw, 1));
            return true;
        }

        // Centre-to-centre spacing at which two plants of the same kind keep each other's grow space free:
        // a plant needs its grow radius free of other colliders, so the neighbour's collider (sapling or grown)
        // must stay outside it.
        internal static double AutoSpacing(double growRadius, double colliderReach, double margin)
        {
            if (!GridFrame.Finite(growRadius) || growRadius < 0) growRadius = 0;
            if (!GridFrame.Finite(colliderReach) || colliderReach < 0) colliderReach = 0;
            if (!GridFrame.Finite(margin) || margin < 0) margin = 0;
            return Math.Max(MinSpacing, Math.Round(growRadius + colliderReach + margin, 3));
        }

        internal static int NextSize(int size, int max)
        {
            max = Math.Max(1, Math.Min(MaxPatternSize, max));
            return size >= max ? 1 : Math.Max(1, size + 1);
        }

        internal static int ClampSize(int size, int max)
        {
            max = Math.Max(1, Math.Min(MaxPatternSize, max));
            return Math.Max(1, Math.Min(max, size));
        }
    }
}
