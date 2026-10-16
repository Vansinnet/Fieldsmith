using System;
using System.Collections.Generic;
using Fieldsmith;

internal static class Program
{
    private static int Main()
    {
        var scenarios = new List<(string Name, Action Run)>
        {
            ("GridRoundTripsWorldPoints", GridRoundTripsWorldPoints),
            ("RotatedGridKeepsSpacing", RotatedGridKeepsSpacing),
            ("RotationRepeatsEveryQuarterTurn", RotationRepeatsEveryQuarterTurn),
            ("NearestCellSnapsToTheGrid", NearestCellSnapsToTheGrid),
            ("OddPatternIsCentredOnTheAimedCell", OddPatternIsCentredOnTheAimedCell),
            ("EvenPatternSurroundsTheAim", EvenPatternSurroundsTheAim),
            ("PatternStartsWithTheNearestCell", PatternStartsWithTheNearestCell),
            ("PatternCellsAreUniqueAndSpaced", PatternCellsAreUniqueAndSpaced),
            ("PatternSizeIsBounded", PatternSizeIsBounded),
            ("NeighbouringClicksContinueTheSameGrid", NeighbouringClicksContinueTheSameGrid),
            ("RowRotationFollowsANeighbour", RowRotationFollowsANeighbour),
            ("RowRotationIgnoresDiagonalsAndFarPlants", RowRotationIgnoresDiagonalsAndFarPlants),
            ("AutoSpacingLeavesGrowSpaceFree", AutoSpacingLeavesGrowSpaceFree),
            ("SizeCyclesAndClamps", SizeCyclesAndClamps),
            ("SeedsLimitThePlants", SeedsLimitThePlants),
            ("StaminaLimitsThePlants", StaminaLimitsThePlants),
            ("ZeroStaminaCostIsUnlimited", ZeroStaminaCostIsUnlimited),
            ("SkillHalvesStaminaAtMost", SkillHalvesStaminaAtMost),
            ("PlantCountTakesTheSmallestLimit", PlantCountTakesTheSmallestLimit),
            ("ScrollInputBecomesWholeSteps", ScrollInputBecomesWholeSteps),
            ("StepsSnapAndClamp", StepsSnapAndClamp),
            ("UndoHistoryIsBounded", UndoHistoryIsBounded),
            ("InvalidNumbersAreContained", InvalidNumbersAreContained),
            ("GrowTimerShowsMinutesAndSeconds", GrowTimerShowsMinutesAndSeconds),
            ("ObjectSpacingKeepsFootprintsApart", ObjectSpacingKeepsFootprintsApart),
            ("UndoRefusesObjectsPickedAfterTheClick", UndoRefusesObjectsPickedAfterTheClick),
            ("FloraCatalogIsWellFormed", FloraCatalogIsWellFormed),
            ("FloraCostsMatchPlantEverythingDefaults", FloraCostsMatchPlantEverythingDefaults),
            ("RegrowTimerFollowsVanillaRespawn", RegrowTimerFollowsVanillaRespawn),
            ("GrowTimeSettingReplacesGameTimes", GrowTimeSettingReplacesGameTimes),
            ("SeedCostsPayBackOnTheFirstPick", SeedCostsPayBackOnTheFirstPick),
        };
        int failed = 0;
        foreach (var (name, run) in scenarios)
        {
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        Console.WriteLine(scenarios.Count + " Fieldsmith scenario(s), " + failed + " failed. No game runtime was exercised.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;

    private static void GridRoundTripsWorldPoints()
    {
        var frame = new GridFrame(123.4, -56.7, 33, 0.7);
        foreach (var (x, z) in new[] { (0.0, 0.0), (130.2, -50.1), (-4000.5, 3999.25) })
        {
            frame.ToGrid(x, z, out double u, out double v);
            frame.ToWorld(u, v, out double bx, out double bz);
            Check(Near(bx, x, 1e-6) && Near(bz, z, 1e-6), $"round trip of ({x}, {z}) gave ({bx}, {bz})");
        }
    }

    private static void RotatedGridKeepsSpacing()
    {
        var frame = new GridFrame(10, 20, 30, 0.8);
        frame.ToWorld(0, 0, out double x0, out double z0);
        frame.ToWorld(1, 0, out double x1, out double z1);
        frame.ToWorld(0, 1, out double x2, out double z2);
        Check(Near(Distance(x0, z0, x1, z1), 0.8) && Near(Distance(x0, z0, x2, z2), 0.8), "neighbour cells must be one spacing apart");
        double dot = (x1 - x0) * (x2 - x0) + (z1 - z0) * (z2 - z0);
        Check(Near(dot, 0, 1e-9), "grid axes must be perpendicular");
        // V follows the yaw (Unity convention: 0 = +Z, 90 = +X).
        var turned = new GridFrame(0, 0, 0, 1);
        turned.ToWorld(0, 1, out double vx, out double vz);
        Check(Near(vx, 0) && Near(vz, 1), "yaw 0 must point V along +Z");
        turned = new GridFrame(0, 0, 45, 1);
        turned.ToWorld(0, 1, out vx, out vz);
        Check(Near(vx, Math.Sqrt(0.5)) && Near(vz, Math.Sqrt(0.5)), "yaw 45 must point V between +X and +Z");
    }

    private static void RotationRepeatsEveryQuarterTurn()
    {
        Check(Near(GridMath.NormalizeQuarter(0), 0), "0");
        Check(Near(GridMath.NormalizeQuarter(90), 0), "90 -> 0");
        Check(Near(GridMath.NormalizeQuarter(105), 15), "105 -> 15");
        Check(Near(GridMath.NormalizeQuarter(-15), 75), "-15 -> 75");
        Check(Near(GridMath.NormalizeQuarter(359.9999999), 0), "almost 360 -> 0");
        Check(Near(new GridFrame(0, 0, 200, 1).RotationDegrees, 20), "frames fold rotation");
    }

    private static void NearestCellSnapsToTheGrid()
    {
        var frame = new GridFrame(0, 0, 0, 0.5);
        Cell cell = GridMath.Nearest(frame, 1.26, -0.74);
        Check(cell.I == 3 && cell.J == -1, $"expected cell (3,-1), got ({cell.I},{cell.J})");
        Check(Near(cell.X, 1.5) && Near(cell.Z, -0.5), $"cell centre ({cell.X}, {cell.Z})");
        var offset = new GridFrame(0.3, 0.3, 0, 1);
        cell = GridMath.Nearest(offset, 2.2, 0.2);
        Check(Near(cell.X, 2.3) && Near(cell.Z, 0.3), "grid starting at a plant snaps relative to it");
    }

    private static void OddPatternIsCentredOnTheAimedCell()
    {
        var frame = new GridFrame(0, 0, 0, 1);
        var cells = new List<Cell>();
        GridMath.Pattern(frame, 5.2, 7.9, 3, cells);
        Check(cells.Count == 9, "3x3 has 9 cells");
        int minI = int.MaxValue, maxI = int.MinValue, minJ = int.MaxValue, maxJ = int.MinValue;
        foreach (Cell c in cells)
        {
            minI = Math.Min(minI, c.I); maxI = Math.Max(maxI, c.I);
            minJ = Math.Min(minJ, c.J); maxJ = Math.Max(maxJ, c.J);
        }
        Check(minI == 4 && maxI == 6 && minJ == 7 && maxJ == 9, $"3x3 around cell (5,8), got I {minI}..{maxI} J {minJ}..{maxJ}");
    }

    private static void EvenPatternSurroundsTheAim()
    {
        var frame = new GridFrame(0, 0, 0, 1);
        var cells = new List<Cell>();
        GridMath.Pattern(frame, 5.2, 7.9, 4, cells);
        Check(cells.Count == 16, "4x4 has 16 cells");
        double cx = 0, cz = 0;
        foreach (Cell c in cells) { cx += c.X; cz += c.Z; }
        cx /= cells.Count;
        cz /= cells.Count;
        Check(Math.Abs(cx - 5.2) <= 0.5 && Math.Abs(cz - 7.9) <= 0.5, $"block centre ({cx}, {cz}) must be within half a cell of the aim");
        GridMath.Pattern(frame, 5.2, 7.9, 2, cells);
        Check(cells.Count == 4, "2x2 has 4 cells");
    }

    private static void PatternStartsWithTheNearestCell()
    {
        var frame = new GridFrame(3, 4, 27, 0.65);
        var cells = new List<Cell>();
        foreach (int size in new[] { 1, 2, 3, 4, 5, 7 })
        {
            GridMath.Pattern(frame, 10.13, -2.71, size, cells);
            Cell nearest = GridMath.Nearest(frame, 10.13, -2.71);
            Check(cells[0].I == nearest.I && cells[0].J == nearest.J, $"size {size}: first cell must be the aimed cell");
            for (int i = 1; i < cells.Count; i++)
                Check(cells[i].DistanceSquared >= cells[i - 1].DistanceSquared, $"size {size}: cells must be ordered by distance");
        }
    }

    private static void PatternCellsAreUniqueAndSpaced()
    {
        var frame = new GridFrame(-1, 2, 15, 0.75);
        var cells = new List<Cell>();
        GridMath.Pattern(frame, 0, 0, 5, cells);
        var seen = new HashSet<(int, int)>();
        foreach (Cell c in cells) Check(seen.Add((c.I, c.J)), "duplicate cell");
        for (int a = 0; a < cells.Count; a++)
            for (int b = a + 1; b < cells.Count; b++)
                Check(Distance(cells[a].X, cells[a].Z, cells[b].X, cells[b].Z) >= 0.75 - 1e-9, "cells closer than the spacing");
    }

    private static void PatternSizeIsBounded()
    {
        var frame = new GridFrame(0, 0, 0, 1);
        var cells = new List<Cell>();
        GridMath.Pattern(frame, 0, 0, 0, cells);
        Check(cells.Count == 1, "size 0 becomes 1x1");
        GridMath.Pattern(frame, 0, 0, 99, cells);
        Check(cells.Count == GridMath.MaxPatternSize * GridMath.MaxPatternSize, "size is capped");
    }

    private static void NeighbouringClicksContinueTheSameGrid()
    {
        // Two 3x3 clicks three cells apart tile without gaps or overlaps.
        var frame = new GridFrame(0.4, 0.4, 0, 0.7);
        var first = new List<Cell>();
        var second = new List<Cell>();
        GridMath.Pattern(frame, 0.4, 0.4, 3, first);
        GridMath.Pattern(frame, 0.4 + 3 * 0.7, 0.4, 3, second);
        var all = new HashSet<(int, int)>();
        foreach (Cell c in first) all.Add((c.I, c.J));
        foreach (Cell c in second) Check(all.Add((c.I, c.J)), "clicks overlap");
        Check(all.Count == 18, "two 3x3 clicks give 18 cells");
        for (int i = -1; i <= 4; i++)
            Check(all.Contains((i, 0)), $"row has a gap at {i}");
    }

    private static void RowRotationFollowsANeighbour()
    {
        Check(GridMath.TryRowRotation(0, 0, 0, 0.7, 0.7, out double r) && Near(r, 0), "neighbour along +Z gives 0");
        Check(GridMath.TryRowRotation(0, 0, 0.7, 0, 0.7, out r) && Near(r, 0), "neighbour along +X also gives 0 (square grid)");
        double a = 20 * Math.PI / 180;
        Check(GridMath.TryRowRotation(5, 5, 5 + Math.Sin(a) * 0.7, 5 + Math.Cos(a) * 0.7, 0.7, out r) && Near(r, 20, 0.11), $"20 degree row, got {r}");
        Check(GridMath.TryRowRotation(5, 5, 5 - Math.Sin(a) * 0.7, 5 - Math.Cos(a) * 0.7, 0.7, out r) && Near(r, 20, 0.11), "opposite neighbour gives the same rotation");
        // A grid built from the detected rotation contains the neighbour.
        var frame = new GridFrame(5, 5, r, 0.7);
        Cell cell = GridMath.Nearest(frame, 5 + Math.Sin(a) * 0.7, 5 + Math.Cos(a) * 0.7);
        Check(Distance(cell.X, cell.Z, 5 + Math.Sin(a) * 0.7, 5 + Math.Cos(a) * 0.7) < 0.01, "neighbour must lie on the followed grid");
    }

    private static void RowRotationIgnoresDiagonalsAndFarPlants()
    {
        Check(!GridMath.TryRowRotation(0, 0, 0.7, 0.7, 0.7, out _), "diagonal neighbour is ignored");
        Check(!GridMath.TryRowRotation(0, 0, 0, 2, 0.7, out _), "far plant is ignored");
        Check(!GridMath.TryRowRotation(0, 0, 0, 0.1, 0.7, out _), "overlapping plant is ignored");
        Check(!GridMath.TryRowRotation(0, 0, double.NaN, 0, 0.7, out _), "NaN is ignored");
    }

    private static void AutoSpacingLeavesGrowSpaceFree()
    {
        double spacing = GridMath.AutoSpacing(0.5, 0.2, 0.05);
        Check(Near(spacing, 0.75), $"0.5 + 0.2 + 0.05, got {spacing}");
        // The neighbour's collider (reach 0.2) ends at spacing - 0.2 = 0.55 > grow radius 0.5.
        Check(spacing - 0.2 > 0.5, "neighbour collider must stay outside the grow radius");
        Check(GridMath.AutoSpacing(0, 0, 0) >= GridMath.MinSpacing, "minimum spacing");
        Check(GridMath.AutoSpacing(-1, double.NaN, -3) >= GridMath.MinSpacing, "invalid input is contained");
    }

    private static void SizeCyclesAndClamps()
    {
        Check(GridMath.NextSize(1, 7) == 2 && GridMath.NextSize(6, 7) == 7 && GridMath.NextSize(7, 7) == 1, "cycle 1..7");
        Check(GridMath.NextSize(9, 5) == 1, "a size above the maximum wraps to 1");
        Check(GridMath.ClampSize(0, 7) == 1 && GridMath.ClampSize(8, 7) == 7 && GridMath.ClampSize(4, 99) == 4, "clamp");
    }

    private static void SeedsLimitThePlants()
    {
        Check(Budget.Seeds(new List<(int, int)> { (23, 1) }, false) == 23, "one seed per plant");
        Check(Budget.Seeds(new List<(int, int)> { (23, 2), (100, 1) }, false) == 11, "the scarcest requirement limits");
        Check(Budget.Seeds(new List<(int, int)> { (0, 1) }, false) == 0, "no seeds");
        Check(Budget.Seeds(new List<(int, int)> { (0, 1) }, true) == Budget.Unlimited, "free building");
        Check(Budget.Seeds(new List<(int, int)> { (5, 0) }, false) == Budget.Unlimited, "requirements of 0 are ignored");
    }

    private static void StaminaLimitsThePlants()
    {
        Check(Budget.Stamina(10, 3) == 3, "10 stamina at 3 per plant pays 3 plants");
        Check(Budget.Stamina(9, 3) == 3, "exactly enough");
        Check(Budget.Stamina(2, 3) == 1, "the first plant is always allowed");
        Check(Budget.Stamina(0, 3) == 1, "vanilla already checked the minimum");
        Check(Budget.Stamina(100, 1) == 100, "1 per plant");
    }

    private static void ZeroStaminaCostIsUnlimited()
    {
        Check(Budget.Stamina(0, 0) == Budget.Unlimited, "0 per plant");
        Check(Budget.StaminaPerPlant(0, 1, true) == 0, "0 stays 0 with skill");
        Check(Budget.StaminaPerPlant(-5, 0, false) == 0, "negative becomes 0");
    }

    private static void SkillHalvesStaminaAtMost()
    {
        Check(Near(Budget.StaminaPerPlant(4, 0, true), 4), "skill 0");
        Check(Near(Budget.StaminaPerPlant(4, 0.5, true), 3), "skill 50");
        Check(Near(Budget.StaminaPerPlant(4, 1, true), 2), "skill 100 halves");
        Check(Near(Budget.StaminaPerPlant(4, 3, true), 2), "factor is clamped");
        Check(Near(Budget.StaminaPerPlant(4, 1, false), 4), "reduction off");
    }

    private static void PlantCountTakesTheSmallestLimit()
    {
        Check(Budget.Plants(9, 23, 100) == 9, "cells limit");
        Check(Budget.Plants(9, 4, 100) == 4, "seeds limit");
        Check(Budget.Plants(9, 23, 2) == 2, "stamina limits");
        Check(Budget.Plants(0, 23, 2) == 0, "no free cell");
        Check(Budget.Plants(9, Budget.Unlimited, Budget.Unlimited) == 9, "unlimited");
    }

    private static void ScrollInputBecomesWholeSteps()
    {
        double accumulated = 0;
        Check(Steps.ConsumeScroll(ref accumulated, 0.05, 0.1) == 0, "below the threshold");
        Check(Steps.ConsumeScroll(ref accumulated, 0.06, 0.1) == 1, "crossing the threshold");
        Check(accumulated == 0, "accumulator restarts");
        Check(Steps.ConsumeScroll(ref accumulated, -0.5, 0.1) == -1, "one step per crossing");
        Check(Steps.ConsumeScroll(ref accumulated, double.NaN, 0.1) == 0, "NaN");
    }

    private static void StepsSnapAndClamp()
    {
        Check(Near(Steps.Adjust(0.72, 1, 0.05, 0.1, 10), 0.75), "moves onto the step grid");
        Check(Near(Steps.Adjust(0.12, -1, 0.05, 0.1, 10), 0.1), "clamped at the minimum");
        Check(Near(Steps.Clamp(double.NaN, 0.1, 1), 0.1), "NaN becomes the minimum");
    }

    private static void UndoHistoryIsBounded()
    {
        var history = new UndoHistory<int>(2);
        history.Push(1);
        history.Push(2);
        history.Push(3);
        Check(history.Count == 2, "capacity");
        Check(history.TryPeek(out int top) && top == 3, "peek newest");
        Check(history.TryPop(out int a) && a == 3 && history.TryPop(out int b) && b == 2 && !history.TryPop(out _), "newest first, oldest dropped");
        history.Capacity = 0;
        history.Push(4);
        Check(history.Count == 0, "capacity 0 disables undo");
    }

    private static void InvalidNumbersAreContained()
    {
        var frame = new GridFrame(double.NaN, double.PositiveInfinity, double.NaN, double.NaN);
        Check(frame.OriginX == 0 && frame.OriginZ == 0 && frame.RotationDegrees == 0 && frame.Spacing >= GridMath.MinSpacing, "invalid frame values");
        Check(new GridFrame(0, 0, 0, 0.01).Spacing >= GridMath.MinSpacing, "spacing has a minimum");
    }

    private static void GrowTimerShowsMinutesAndSeconds()
    {
        Check(Steps.Duration(0) == "0:00", "zero");
        Check(Steps.Duration(4.2) == "0:05", "seconds round up");
        Check(Steps.Duration(245) == "4:05", "minutes and seconds");
        Check(Steps.Duration(3599.5) == "1:00:00", "rounds up into an hour");
        Check(Steps.Duration(3723) == "1:02:03", "hours for long timers");
        Check(Steps.Duration(-5) == "0:00" && Steps.Duration(double.NaN) == "0:00", "invalid values");
    }

    private static void ObjectSpacingKeepsFootprintsApart()
    {
        // An object keeps its own reach free (space = reach), so a neighbour's footprint ends outside it.
        double reach = 0.6;
        double spacing = GridMath.AutoSpacing(reach, reach, 0.05);
        Check(Near(spacing, 1.25), $"0.6 + 0.6 + 0.05, got {spacing}");
        Check(spacing - reach > reach, "neighbour footprint must stay outside the object's space");
        Check(Near(GridMath.AutoSpacing(reach, reach, 0), 2 * reach), "tight spacing is two reaches");
    }

    private static void UndoRefusesObjectsPickedAfterTheClick()
    {
        long click = 1_000_000_000L;
        long second = TimeSpan.TicksPerSecond;
        Check(!Undoing.PickedSince(false, 0, click), "never picked");
        Check(!Undoing.PickedSince(true, click - second / 2, click), "placed already picked (pick stored during the click)");
        Check(!Undoing.PickedSince(true, click + second / 2, click), "pick stored a moment after the click belongs to the placement");
        Check(Undoing.PickedSince(true, click + 10 * second, click), "picked after the click");
        Check(Undoing.PickedSince(false, click + 10 * second, click), "picked after the click and grown back");
        Check(Undoing.PickedSince(true, 0, click), "picked without a stored time");
    }

    private static void FloraCatalogIsWellFormed()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FloraEntry entry in FloraCatalog.All)
        {
            Check(!string.IsNullOrWhiteSpace(entry.Prefab) && seen.Add(entry.Prefab), "unique prefab " + entry.Prefab);
            Check(!string.IsNullOrWhiteSpace(entry.Name) && !entry.Name.StartsWith("$"), "plain name for " + entry.Prefab);
            Check(entry.Costs != null && entry.Costs.Length > 0, "cost for " + entry.Prefab);
            foreach (FloraCost cost in entry.Costs) Check(cost.Amount > 0, "positive cost for " + entry.Prefab);
            Check(!entry.Recover || entry.Removable, "only removable plants return their cost: " + entry.Prefab);
            Check(entry.Yield >= 0, "yield for " + entry.Prefab);
            if (entry.SnapPoints != null)
                foreach (double[] point in entry.SnapPoints) Check(point != null && point.Length == 3, "snap point of " + entry.Prefab);
            Check(FloraCatalog.TryGet(entry.Prefab, out FloraEntry found) && ReferenceEquals(found, entry), "lookup of " + entry.Prefab);
        }
        Check(seen.Count == 25, $"25 wild plants, got {seen.Count}");
        Check(!FloraCatalog.TryGet("Ancient_Sapling", out _), "Plant Everything's own saplings are not included");
        Check(!FloraCatalog.TryGet(null, out _) && !FloraCatalog.TryGet("raspberrybush", out _), "lookup is exact");
    }

    private static void FloraCostsMatchPlantEverythingDefaults()
    {
        // Plant Everything 1.21.3 default configuration: (item, amount); null is the plant's own pick item.
        var expected = new Dictionary<string, (string Item, int Amount)[]>
        {
            ["RaspberryBush"] = new[] { ((string)null, 5) },
            ["BlueberryBush"] = new[] { ((string)null, 5) },
            ["CloudberryBush"] = new[] { ((string)null, 5) },
            ["LingonberryBush"] = new[] { ((string)null, 5) },
            ["Pickable_Mushroom"] = new[] { ((string)null, 5) },
            ["Pickable_Mushroom_yellow"] = new[] { ((string)null, 5) },
            ["Pickable_Mushroom_blue"] = new[] { ((string)null, 5) },
            ["Pickable_Thistle"] = new[] { ((string)null, 5) },
            ["Pickable_Dandelion"] = new[] { ((string)null, 5) },
            ["Pickable_SmokePuff"] = new[] { ((string)null, 5) },
            ["Pickable_Fiddlehead"] = new[] { ((string)null, 15) },
            ["Beech_small1"] = new[] { ("BeechSeeds", 1) },
            ["FirTree_small"] = new[] { ("FirCone", 1) },
            ["FirTree_small_dead"] = new[] { ("FirCone", 1) },
            ["Bush01"] = new[] { ("Wood", 2) },
            ["Bush01_heath"] = new[] { ("Wood", 2) },
            ["Bush02_en"] = new[] { ("Wood", 3) },
            ["shrub_2"] = new[] { ("Wood", 2) },
            ["shrub_2_heath"] = new[] { ("Wood", 2) },
            ["YggaShoot_small1"] = new[] { ("YggdrasilWood", 1), ("Wood", 2) },
            ["vines"] = new[] { ("Wood", 2) },
            ["FernAshlands"] = new[] { ("Wood", 2) },
            ["Pickable_Branch"] = new[] { ((string)null, 5) },
            ["Pickable_Stone"] = new[] { ((string)null, 1) },
            ["Pickable_Flint"] = new[] { ((string)null, 5) },
        };
        Check(expected.Count == FloraCatalog.All.Count, "every plant has an expected cost");
        foreach (FloraEntry entry in FloraCatalog.All)
        {
            Check(expected.TryGetValue(entry.Prefab, out var costs), "expected cost for " + entry.Prefab);
            Check(costs.Length == entry.Costs.Length, "cost count of " + entry.Prefab);
            for (int i = 0; i < costs.Length; i++)
                Check(costs[i].Item == entry.Costs[i].Item && costs[i].Amount == entry.Costs[i].Amount, $"cost {i} of {entry.Prefab}");
        }
        Check(FloraCatalog.TryGet("Pickable_Fiddlehead", out FloraEntry fiddlehead) && fiddlehead.Yield == 3 && fiddlehead.NoExtraDrops, "fiddlehead gives three");
        Check(FloraCatalog.TryGet("vines", out FloraEntry vines) && vines.Recover && !vines.Grounded && vines.SnapPoints.Length == 4, "vines");
        Check(FloraCatalog.TryGet("Bush01", out FloraEntry bush) && !bush.Removable, "decorative plants cannot be removed");
        Check(FloraCatalog.TryGet("RaspberryBush", out FloraEntry berry) && berry.Removable && !berry.Recover, "berry bushes are removable without refund");
    }

    private static void RegrowTimerFollowsVanillaRespawn()
    {
        long second = TimeSpan.TicksPerSecond;
        long picked = 638_000_000_000_000_000L;
        Check(Near(Respawn.SecondsLeft(picked, picked, 240), 240 * 60), "full time right after picking");
        Check(Near(Respawn.SecondsLeft(picked, picked + 3600 * second, 240), 180 * 60), "three hours left after one");
        Check(Respawn.SecondsLeft(picked, picked + 241 * 60 * second, 240) == 0, "due: respawns at the next check");
        Check(Respawn.SecondsLeft(0, picked, 240) == 0 && Respawn.SecondsLeft(1, picked, 240) == 0, "no stored time respawns at once, as vanilla");
        Check(double.IsNaN(Respawn.SecondsLeft(picked, picked, 0)), "no respawn time: never grows back");
        Check(double.IsNaN(Respawn.SecondsLeft(picked, picked, double.NaN)), "invalid respawn time");
        Check(Steps.Duration(Respawn.SecondsLeft(picked, picked + 30 * second, 300)) == "4:59:30", "display of a berry bush timer");
    }

    private static void SeedCostsPayBackOnTheFirstPick()
    {
        // Game yield per pick is 1 for every wild pickable in 1.0.20 (docs/evidence/flora.json).
        foreach (FloraEntry entry in FloraCatalog.All)
        {
            int perPick = entry.Yield > 0 ? entry.Yield : 1;
            foreach (FloraCost cost in entry.Costs)
            {
                int seed = FloraCatalog.CostAmount(entry, cost, 1, FloraCostMode.Seed);
                int pe = FloraCatalog.CostAmount(entry, cost, 1, FloraCostMode.PlantEverything);
                Check(pe == cost.Amount, "Plant Everything mode keeps the catalog cost of " + entry.Prefab);
                if (cost.Item == null)
                    Check(seed == perPick, $"{entry.Prefab} costs one pick ({perPick}), got {seed}");
                else
                    Check(seed == cost.Amount, "other costs stay the same for " + entry.Prefab);
            }
        }
        Check(FloraCatalog.TryGet("Pickable_Mushroom", out FloraEntry mushroom) && FloraCatalog.CostAmount(mushroom, mushroom.Costs[0], 1, FloraCostMode.Seed) == 1, "one mushroom");
        Check(FloraCatalog.TryGet("Pickable_Fiddlehead", out FloraEntry fern) && FloraCatalog.CostAmount(fern, fern.Costs[0], 1, FloraCostMode.Seed) == 3, "three fiddleheads");
        Check(FloraCatalog.TryGet("Bush02_en", out FloraEntry bush) && FloraCatalog.CostAmount(bush, bush.Costs[0], 1, FloraCostMode.Seed) == 3, "plains bush keeps 3 wood");
        Check(FloraCatalog.CostAmount(mushroom, mushroom.Costs[0], 0, FloraCostMode.Seed) == 1, "never free");
        Check(FloraCatalog.TryGet("Pickable_Stone", out FloraEntry stone) && Near(stone.RegrowMinutes, FloraCatalog.FlintRegrowMinutes) && Near(stone.RegrowMinutes, 240), "stone grows back like flint");
        foreach (FloraEntry entry in FloraCatalog.All)
            if (entry.Prefab != "Pickable_Stone") Check(entry.RegrowMinutes == 0, "only the stone gets a regrowth: " + entry.Prefab);
    }

    private static void GrowTimeSettingReplacesGameTimes()
    {
        Check(Near(GrowthTime.TotalMinutes(1, 55), 115), "1 h 55 min");
        Check(GrowthTime.TotalMinutes(0, 0) == 0 && double.IsNaN(GrowthTime.PlantSeconds(GrowthTime.TotalMinutes(0, 0))), "0 h 0 min keeps the game's times");
        Check(Near(GrowthTime.TotalMinutes(0, 99), 59) && Near(GrowthTime.TotalMinutes(-3, -2), 0) && Near(GrowthTime.TotalMinutes(30, 59), GrowthTime.MaxMinutes), "parts stay in range");
        Check(Near(GrowthTime.PlantSeconds(GrowthTime.TotalMinutes(1, 55)), 6900), "1 h 55 min in seconds");
        Check(double.IsNaN(GrowthTime.PlantSeconds(0)), "0 keeps the plant's own time");
        Check(Near(GrowthTime.PlantSeconds(30), 1800), "30 minutes for every plant");
        Check(Near(GrowthTime.PlantSeconds(0.5), 30), "half a minute");
        Check(double.IsNaN(GrowthTime.PlantSeconds(-5)) && double.IsNaN(GrowthTime.PlantSeconds(double.NaN)) &&
            double.IsNaN(GrowthTime.PlantSeconds(double.PositiveInfinity)) && double.IsNaN(GrowthTime.PlantSeconds(GrowthTime.MaxMinutes + 1)), "invalid values keep the game's time");
        Check(Near(GrowthTime.RegrowMinutes(300, 0), 300), "0 keeps the game's regrow time");
        Check(Near(GrowthTime.RegrowMinutes(300, 30), 30) && Near(GrowthTime.RegrowMinutes(240, 30), 30), "same regrow time for every wild plant");
        Check(GrowthTime.RegrowMinutes(0, 30) == 0, "a stone never grows back");
        Check(Near(GrowthTime.RegrowMinutes(240, double.NaN), 240), "invalid setting keeps the game's time");
    }

    private static double Distance(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
