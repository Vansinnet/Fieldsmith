# Changelog

## 0.1.0 - 2026-10-16

- Grid planting with the Cultivator: the plant ghost snaps to a square grid with each plant's own spacing.
- Patterns from 1x1 to 10x10 planted in one click (Shift + mouse wheel or F8).
- The grid continues the rows of nearby plants and crops; free grids can be turned (Alt + mouse wheel) and locked (F6).
- Cells where a plant could not grow are skipped; ground markers show what will be planted.
- Configurable stamina per plant (0 to 25), reduced by the Farming skill like vanilla.
- Undo of the last planting clicks with the seeds returned (Backspace).
- Information panel in the style of Landsmith.
- Block harvest: Left Shift + E on a ripe crop picks every ripe crop in the block around it, with optional stamina per crop and optional replanting (replanting raises Farming like planting by hand).
- Grow timer: the hover text of growing crops and saplings shows the time until they are fully grown.
- Growth time setting: `[Growth] GrowTimeHours` and `GrowTimeMinutes` give every crop and sapling, and the regrowth of planted wild plants, one fixed time in minutes.
- Removing planted wild plants with the Cultivator (remove button, middle mouse; with Left Shift every one within `[Plants] RemoveRadius`): a ripe one is picked first; crops, saplings, buildings and wild plants are never removed.
- Regrow timer: picked berry bushes, mushrooms, flowers, fiddleheads, branches and flint show the time until they grow back; planted mushrooms, flowers, fiddleheads, branches and flint show a small version of themselves while they grow back, instead of vanishing.
- Wild plants without other mods: berry bushes, mushrooms, thistle, dandelion, smoke puffs, fiddlehead, small trees, bushes, shrubs, vines, Ashlands fern, branches, stones and flint can be planted with the Cultivator, costing what one pick gives (`[Plants] Costs = Seed`, so planting always pays off) or Plant Everything's default costs (`PlantEverything`). Planted stones grow back like flint. Placed ones are ordinary game objects that every player sees. Turned off automatically while Plant Everything is installed (`[Plants] AddWildPlants`).
- Plant Everything and similar mods: every placeable object in the Cultivator's menu (berry bushes, mushrooms, flowers, debris, small trees, extra saplings) is planted in the grid, with rows, block harvest of the ones you placed, and undo until they are picked.
