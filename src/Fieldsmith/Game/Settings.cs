using BepInEx.Configuration;
using UnityEngine;

namespace Fieldsmith
{
    // Local client settings. They only affect the player who plants; the plants themselves are ordinary
    // vanilla objects.
    internal sealed class Settings
    {
        internal const float MaxStaminaPerPlant = 25f;

        internal readonly ConfigEntry<float> StaminaPerPlant;
        internal readonly ConfigEntry<bool> StaminaSkillReduction;
        internal readonly ConfigEntry<bool> DurabilityPerPlant;
        internal readonly ConfigEntry<int> DefaultPatternSize;
        internal readonly ConfigEntry<int> MaxPatternSize;
        internal readonly ConfigEntry<bool> SnapToGrid;
        internal readonly ConfigEntry<float> FixedSpacing;
        internal readonly ConfigEntry<float> SpacingMargin;
        internal readonly ConfigEntry<float> SpacingStep;
        internal readonly ConfigEntry<bool> FollowNearbyCrops;
        internal readonly ConfigEntry<float> FollowRadius;
        internal readonly ConfigEntry<float> RotationStep;
        internal readonly ConfigEntry<int> UndoSteps;
        internal readonly ConfigEntry<bool> ShowGrowTimer;
        internal readonly ConfigEntry<bool> ShowPanel;
        internal readonly ConfigEntry<bool> ShowPreview;
        internal readonly ConfigEntry<float> PanelScale;
        internal readonly ConfigEntry<bool> HarvestBlocks;
        internal readonly ConfigEntry<int> HarvestSize;
        internal readonly ConfigEntry<float> HarvestStaminaPerCrop;
        internal readonly ConfigEntry<bool> Replant;
        internal readonly ConfigEntry<bool> AddWildPlants;
        internal readonly ConfigEntry<FloraCostMode> WildPlantCosts;
        internal readonly ConfigEntry<float> RemoveRadius;
        internal readonly ConfigEntry<int> GrowTimeHours;
        internal readonly ConfigEntry<int> GrowTimeMinutes;
        internal readonly ConfigEntry<KeyboardShortcut> NextPattern;
        internal readonly ConfigEntry<KeyboardShortcut> ToggleSnap;
        internal readonly ConfigEntry<KeyboardShortcut> LockGrid;
        internal readonly ConfigEntry<KeyboardShortcut> Undo;
        // Keys only act while a plant is selected. Defaults match Landsmith's layout (F8 shape, Y toggle,
        // F6 anchor, Backspace undo) and avoid vanilla keys, SeneaL UI (F7, Z, X, C, H) and QuickStack (F3).

        internal Settings(ConfigFile config)
        {
            StaminaPerPlant = config.Bind("Costs", "StaminaPerPlant", 1f,
                new ConfigDescription("Stamina used for each plant. 0 makes planting free of stamina. A 3x3 click with 1 uses 9 stamina. World modifiers for stamina still apply. This is a local setting of the player who plants.", new AcceptableValueRange<float>(0f, MaxStaminaPerPlant)));
            StaminaSkillReduction = config.Bind("Costs", "StaminaSkillReduction", true,
                "Reduce the stamina per plant with the tool's skill (Farming for the Cultivator) like vanilla: up to half at skill level 100.");
            DurabilityPerPlant = config.Bind("Costs", "DurabilityPerPlant", true,
                "Wear the tool once per plant, like planting them one by one. Off: once per click.");
            DefaultPatternSize = config.Bind("Pattern", "DefaultSize", 1,
                new ConfigDescription("Pattern size when the game starts: 1 plants one at a time, 3 plants 3x3 and so on.", new AcceptableValueRange<int>(1, GridMath.MaxPatternSize)));
            MaxPatternSize = config.Bind("Pattern", "MaximumSize", 7,
                new ConfigDescription("Largest pattern. The size key and Shift + mouse wheel cycle from 1x1 up to this size.", new AcceptableValueRange<int>(1, GridMath.MaxPatternSize)));
            SnapToGrid = config.Bind("Grid", "SnapToGrid", true, "Snap plants to the grid when the game starts. The toggle key switches it in game.");
            FixedSpacing = config.Bind("Grid", "FixedSpacing", 0f,
                new ConfigDescription("Distance between plants in metres for every plant. 0 uses each plant's own spacing (its grow radius plus the size of a neighbour), so every plant has room to grow.", new AcceptableValueRange<float>(0f, 10f)));
            SpacingMargin = config.Bind("Grid", "SpacingMargin", 0.05f,
                new ConfigDescription("Extra metres added to the automatic spacing.", new AcceptableValueRange<float>(0f, 2f)));
            SpacingStep = config.Bind("Grid", "SpacingStep", 0.05f,
                new ConfigDescription("Metres per Ctrl + mouse wheel step for the spacing.", new AcceptableValueRange<float>(0.01f, 1f)));
            FollowNearbyCrops = config.Bind("Grid", "FollowNearbyCrops", true,
                "Continue the rows of plants near the aim: the grid starts at the nearest plant and takes the direction of its row.");
            FollowRadius = config.Bind("Grid", "FollowRadius", 3f,
                new ConfigDescription("How far from the aim, in metres, plants are looked for to continue their rows.", new AcceptableValueRange<float>(0.5f, 10f)));
            RotationStep = config.Bind("Grid", "RotationStep", 15f,
                new ConfigDescription("Degrees per Alt + mouse wheel step for the grid direction.", new AcceptableValueRange<float>(1f, 45f)));
            UndoSteps = config.Bind("Grid", "UndoSteps", 5,
                new ConfigDescription("How many of your own planting clicks can be undone (seeds are returned). 0 disables undo.", new AcceptableValueRange<int>(0, 20)));
            HarvestBlocks = config.Bind("Harvest", "Enabled", true,
                "Hold the Alt place key (Left Shift by default) while picking a grown crop to pick every ripe crop in the block around it.");
            HarvestSize = config.Bind("Harvest", "Size", 0,
                new ConfigDescription("Side of the harvest block in plants. 0 uses the current planting pattern size, at least 3x3.", new AcceptableValueRange<int>(0, GridMath.MaxPatternSize)));
            HarvestStaminaPerCrop = config.Bind("Harvest", "StaminaPerCrop", 0f,
                new ConfigDescription("Stamina used for each extra crop picked by a block harvest. 0 is free, like vanilla picking. World modifiers for stamina still apply.", new AcceptableValueRange<float>(0f, MaxStaminaPerPlant)));
            Replant = config.Bind("Harvest", "Replant", false,
                "Plant a new seed in the same spot after a block harvest, when you carry the seeds and the tool that plants them. Uses StaminaPerPlant and the seeds like normal planting.");
            AddWildPlants = config.Bind("Plants", "AddWildPlants", true,
                "Add the game's wild plants to the Cultivator: berry bushes, mushrooms, thistle, dandelion, smoke puffs, fiddlehead, small trees, bushes, shrubs, vines, Ashlands fern, branches, stones and flint, at Plant Everything's default costs. Placed ones are ordinary objects that every player sees. Ignored while Plant Everything is installed. Restart after changing.");
            WildPlantCosts = config.Bind("Plants", "Costs", FloraCostMode.Seed,
                "Seed: a berry bush, mushroom, flower, fiddlehead, branch, flint or stone costs what one pick gives (1 raspberry, 1 mushroom, 3 fiddleheads), so the first pick pays it back and every regrowth is a gain. PlantEverything: Plant Everything's default costs (5 berries, 5 mushrooms, 15 fiddleheads, 5 wood for a branch, 5 flint). Bushes and small trees cost the same in both. Applies at once.");
            RemoveRadius = config.Bind("Plants", "RemoveRadius", 3f,
                new ConfigDescription("Metres around the aim in which Left Shift (Alt place) + remove button with the Cultivator removes every wild plant someone planted. The remove button alone removes one.", new AcceptableValueRange<float>(0.5f, 15f)));
            GrowTimeHours = config.Bind("Growth", "GrowTimeHours", 0,
                new ConfigDescription("Hours of the grow time, together with GrowTimeMinutes (1 and 55 = 1 h 55 min). The time is how long every crop and sapling takes to grow, and every berry bush, mushroom, flower, fiddlehead, branch and flint you planted takes to grow back after picking. 0 hours and 0 minutes keep the game's own times (crops 1 h 7 min to 1 h 23 min, saplings 50 min to 2 h 13 min, berries and mushrooms grow back in 4 to 5 h). Plants grow on the computer of the player who owns them, so in multiplayer this applies while you are the owner (usually the first player nearby). Applies at once, also to plants already in the ground.", new AcceptableValueRange<int>(0, GrowthTime.MaxHours)));
            GrowTimeMinutes = config.Bind("Growth", "GrowTimeMinutes", 0,
                new ConfigDescription("Minutes of the grow time, added to GrowTimeHours (see there).", new AcceptableValueRange<int>(0, 59)));
            ShowGrowTimer = config.Bind("Interface", "ShowGrowTimer", true,
                "Show the time left until a growing crop or sapling is fully grown, or until a picked berry bush, mushroom, flower or debris grows back, when you look at it.");
            ShowPanel = config.Bind("Interface", "ShowPanel", true, "Show the panel with the pattern, grid and keys.");
            ShowPreview = config.Bind("Interface", "ShowPreview", true, "Mark every cell of the pattern on the ground: green plants, yellow lacks seeds or stamina, red is blocked.");
            PanelScale = config.Bind("Interface", "PanelScale", 1f,
                new ConfigDescription("Size of the panel.", new AcceptableValueRange<float>(0.75f, 2f)));
            NextPattern = config.Bind("Keys", "NextPattern", new KeyboardShortcut(KeyCode.F8), "Next pattern size (1x1, 2x2, 3x3 ... back to 1x1).");
            ToggleSnap = config.Bind("Keys", "ToggleSnap", new KeyboardShortcut(KeyCode.Y), "Grid snapping on or off. Off plants one at a time like vanilla.");
            LockGrid = config.Bind("Keys", "LockGrid", new KeyboardShortcut(KeyCode.F6), "Lock the current grid so it stops following nearby plants; press again to unlock.");
            Undo = config.Bind("Keys", "Undo", new KeyboardShortcut(KeyCode.Backspace), "Undo your last planting click and get the seeds back.");
        }
    }
}
