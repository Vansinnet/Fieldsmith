# Fieldsmith

Version **0.2.0** for Valheim **1.0.20** with BepInEx 5.

Plant crops and saplings in perfect grids with the Cultivator: one at a time or a whole 3x3, 4x4 or larger block in one click, in straight rows that continue your existing field, with a live preview on the ground, undo and an adjustable stamina cost. Harvest a whole block of ripe crops with Shift + E, and replant it automatically if you like.

**Status: Runtime-pending.** The mod compiles against Valheim 1.0.20 and its grid and cost logic is covered by automated tests, but it has not yet been observed in a live game. Use it on a backed-up world.

**Download:** [GitHub releases](https://github.com/Vansinnet/Fieldsmith/releases). Take the latest release, download `Fieldsmith-<version>.zip` under *Assets* and extract it into `BepInEx/plugins/Fieldsmith/`.

## How It Works

Select any seed or sapling in the Cultivator's build menu. The plant ghost now snaps to a square grid, and a panel on the left shows the pattern, the spacing, the grid and the keys (the same panel as Landsmith).

- **Pattern.** Every click plants an N x N block around the aim: 1x1 (one plant, as in vanilla), 2x2, 3x3, 4x4 and so on up to 10x10. The aimed cell is always planted first.
- **Spacing.** Each plant gets its own distance: its grow radius plus the size of a neighbour, so every plant in the grid has room to grow. Ctrl + mouse wheel makes the grid wider or tighter; the panel warns when plants would block each other.
- **Rows that continue.** Near existing plants or grown crops the grid starts at the nearest one and takes the direction of its row, so new clicks line up with the field you already have, even one you planted by hand at an angle. Elsewhere the grid is aligned to the world, and Alt + mouse wheel turns it.
- **Preview.** Every cell of the block is marked on the ground: green will be planted, yellow lacks seeds or stamina, red is blocked. The outline turns blue while the grid is locked.
- **Only where it grows.** A cell is skipped when the plant could not grow there: ground not cultivated (or not dirt for saplings), too close to another plant or object, under a roof, in the wrong biome, too hot or too cold, under water, inside someone else's ward or in a no-build area. The message says how many were planted and why the rest were skipped.
- **Undo.** Backspace removes the plants of your last clicks and returns the seeds, as long as they have not grown yet.

## Wild Plants

Fieldsmith adds the game's wild plants to the Cultivator's build menu, so you do not need Plant Everything for them. By default (`[Plants] Costs = Seed`) a plant that you pick costs what one pick gives, like a seed: the first pick pays it back and every regrowth after that is a gain. `Costs = PlantEverything` uses Plant Everything's default costs instead:

| Plant | Cost (Seed) | Cost (PlantEverything) |
|---|---|---|
| Raspberry, blueberry, cloudberry and lingonberry bush | 1 of its berries | 5 |
| Mushrooms, yellow mushrooms, blue mushrooms, smoke puffs | 1 of them | 5 |
| Thistle, dandelion | 1 of them | 5 |
| Fiddlehead (gives 3 per pick) | 3 fiddleheads | 15 |
| Small beech | 1 wood (1 beech seed with `TreesCostWood` off) | same |
| Small fir, small dead fir | 1 wood (1 fir cone with `TreesCostWood` off) | same |
| Bush, heath bush, shrub, heath shrub, Ashlands fern | 2 wood | same |
| Plains bush | 3 wood | same |
| Small ygga shoot | 1 yggdrasil wood and 2 wood | same |
| Vines (snap to each other, wood returned when removed) | 2 wood | same |
| Branch, flint | 1 wood, 1 flint | 5 wood, 5 flint |
| Stone (grows back like flint) | 1 stone | 1 stone |

- A plant appears in the menu once you have found its cost item, like any other piece.
- Placed ones are the game's own bushes, mushrooms and stones, so other players (also without mods, and console players) and the server see them, and they stay if Fieldsmith is removed. Berries, mushrooms, flowers, branches, flint and stones grow back after picking, at the game's normal times (stones like flint, after 4 hours; wild stones still never grow back).
- **Removing:** hold the Cultivator and press the remove button (middle mouse) on a wild plant someone planted, or hold **Left Shift** and press it to remove every planted wild plant within 3 m of the aim (`[Plants] RemoveRadius`, nearest first, as long as your stamina lasts). It works on berry bushes, mushrooms, flowers, fiddleheads, branches, flint, stones, small trees, bushes, ferns and vines. A ripe one is picked first, so you get its berries or mushrooms; the cost is not returned, except the wood of vines. It costs stamina and tool wear like removing with the hammer, and works only where you could build (not inside someone else's ward or a no-build area). Crops, saplings, buildings and wild plants are never removed with the Cultivator. The hammer can also remove planted berry bushes and vines, but not the small plants. Undo (Backspace) still works right after a click.
- Plant Everything's own saplings (Ancient, Ygga, Autumn Birch, Ashwood) are not included: they are new objects that every player and the server would have to install.
- Turn it off with `[Plants] AddWildPlants`. While Plant Everything is installed, Fieldsmith leaves the wild plants to it.

## Plant Everything And Other Planting Mods

Mods such as Plant Everything add more to the Cultivator's build menu: berry bushes, mushrooms, thistle, dandelion, smoke puffs, fiddlehead, branches, stones, flint, small trees, bushes and the custom saplings. Fieldsmith plants all of them in the same grid as crops, with the same patterns, rows, preview, costs and undo, and then does not add its own wild plants. No setting is needed; Fieldsmith learns every placeable object in the menu of a planting tool (a tool whose menu has at least one seed or sapling).

- Saplings added by such mods grow like vanilla saplings and are handled exactly like them.
- Objects that do not grow (bushes, mushrooms, flowers, debris, small trees) keep their own size free instead of a grow radius: the automatic spacing is twice their size plus `SpacingMargin`, so neighbours never overlap. Each cell checks the object's own rules (biome, cultivated ground) and that nothing already stands there; sun, heat and cold do not matter for them.
- Rows continue from objects you placed, never from wild bushes, mushrooms or trees.
- Left Shift + E on a berry bush, mushroom or flower you placed picks every ripe one in the block around it; wild ones are left alone. They grow back by themselves, so they are not replanted.
- Undo returns the cost of an object only until someone picks it.

## Harvesting

Hold **Left Shift** (the game's "Alt place" key) and press **E** on a ripe crop: every ripe crop in the block around it is picked at once. The block follows the rows of the field and is as big as your planting pattern, at least 3x3 (or a fixed size with `[Harvest] Size`). Each crop is picked the vanilla way, so drops, Farming skill and bonus yield work as usual. Unripe plants and wild plants are never touched.

- Picking costs no stamina by default, like vanilla. `[Harvest] StaminaPerCrop` (0 to 25) adds a cost for each extra crop; the block then stops where your stamina ends.
- **Replant** (off by default, `[Harvest] Replant`): a new seed is planted where each crop stood, as long as you carry the seeds and the tool that plants them (the Cultivator). It uses seeds and `StaminaPerPlant` like normal planting. It also works for a single crop picked with Shift + E. It raises Farming once per seed, like planting by hand.

## Grow Timer

Look at a growing crop or sapling and the hover text shows how long until it is fully grown, in minutes and seconds (hours as well for long timers, such as trees): "Fully grown in 4:05". The time comes from the game itself: every plant gets a fixed grow time when it is planted, counted in server time. The plant appears within about 10 seconds after the timer ends, because the game checks growing plants every 10 seconds. An unhealthy plant (orange text) only grows once it is healthy again.

Picked berry bushes, mushrooms, flowers, fiddleheads, branches and flint show when they grow back: "Grows back in 3:59:30". They return within about a minute after the timer ends, because the game checks them once a minute. A picked mushroom, flower, fiddlehead, branch or flint is not gone: the game hides it until it grows back. The ones you planted show a small version of themselves while they grow back, and you can look at it to see the timer. Wild ones vanish completely, as in the game. The small version is only shown to players with Fieldsmith.

Turn the timers off with `[Interface] ShowGrowTimer`.

### Growth Time

`[Growth] GrowTimeHours` and `GrowTimeMinutes` set one grow time for everything you can plant, for example 1 and 55 for 1 h 55 min: every crop and sapling grows in that time, and every berry bush, mushroom, flower, fiddlehead, branch and flint you planted grows back in that time after picking (wild ones keep the game's times; a stone never grows back). 0 hours and 0 minutes (the default) keep the game's times: crops 1 h 7 min to 1 h 23 min, saplings 50 min to 2 h 13 min, berries and mushrooms 4 to 5 hours. A change applies at once, also to plants already in the ground, and the timers show the new time.

Plants grow on the computer of the player who owns them. In single player that is you; in multiplayer it is usually the first player who came near, so the setting applies while you are that player. Other players without this setting see the game's normal times for plants they own.

## Costs

| | Per plant |
|---|---|
| Seeds | As in vanilla (usually one seed). A click plants only as many as you have seeds for. |
| Trees | **1 wood** instead of a seed or cone for every tree sapling (beech, birch, oak, fir, pine and big fir) and for the small trees (`[Plants] TreesCostWood`, on by default; off gives the game's costs). |
| Stamina | `StaminaPerPlant`, default **1**, anything from **0** (free) to **25**. Like vanilla it is reduced by your Farming skill (up to half at level 100; can be turned off) and by the world's stamina modifier. A click plants only as many as your stamina pays for. |
| Tool wear | As in vanilla, once per plant (can be set to once per click). |
| Farming skill | Raised once per plant, as when planting one by one, also for wild plants and replanted seeds. Undoing plants works like removing pieces in vanilla: the next plants do not raise the skill again. Picking crops, berries, mushrooms, flowers and fiddleheads raises Farming exactly as in vanilla (once per crop, also in a block harvest; the game gives skill for the same bush or mushroom only once per game session, and none for branches, stones and flint). |

The stamina setting also applies with the grid turned off. Vanilla still requires the Cultivator's minimum stamina before any click, even when the setting is 0.

With trees costing wood, the saplings appear in the Cultivator as soon as you have found wood, without finding their seeds first. The game then keeps them as learned, also if you turn the setting off or remove Fieldsmith (they cost their seed or cone again then). The planted trees are the game's own, and the cost only applies to you: other players pay what their game or mods ask. Saplings added by other mods (such as Plant Everything's) keep their costs.

## Controls

| Input | Effect |
|---|---|
| Left click | Plant the pattern |
| Shift + mouse wheel | Pattern size |
| F8 | Next pattern size (1x1, 2x2 ... maximum, then 1x1 again) |
| Ctrl + mouse wheel | Spacing |
| Alt + mouse wheel | Turn the grid (a grid that follows plants is locked first) |
| F6 | Lock the grid where it is, so it stops following nearby plants; press again to unlock |
| Y | Grid on or off. Off plants one at a time exactly like vanilla |
| Backspace | Undo your last planting click and get the seeds back |
| Left Shift + E on a ripe crop | Harvest the block around it (any tool) |
| Middle mouse with the Cultivator | Remove the planted wild plant under the aim |
| Left Shift + middle mouse with the Cultivator | Remove every planted wild plant within `RemoveRadius` of the aim |

The keys only act while a plant is selected, and only when pressed without other Shift, Ctrl or Alt keys. The mouse wheel only zooms the camera when no modifier key is held. Ctrl is also the crouch key, so Ctrl + wheel makes your character crouch. The defaults are the same keys as Landsmith (they never act at the same time) and avoid the vanilla keys, SeneaL UI (F7, Z, X, C, H) and QuickStack (F3). All keys can be changed in the configuration file.

## Limits

- Keyboard and mouse only.
- Only aims at the ground itself are snapped. Aiming at a floor, a rock or a building places one plant the vanilla way. Inside dungeons and in the Deep North the grid is off.
- Vines that grow on walls are always planted the vanilla way.
- Fieldsmith skips cells by the vanilla growing rules even when another mod relaxes them (for example Plant Everything's options to grow saplings without sunlight or growth space).
- Undo only works for your own clicks in the current session, while the plants are still growing and you are near them.

## Multiplayer

Only the player who plants needs Fieldsmith. Plants are ordinary Valheim objects, so other players (including unmodded and console players) and the server see them without installing anything, and they remain if the mod is removed. All settings, including the stamina cost, are local to the player who uses the mod; a server cannot enforce them.

## Installation

Put `Fieldsmith.dll` in `BepInEx/plugins/Fieldsmith/`, or install the package with a mod manager. Install side: **client**.

If you also use another planting mod (for example FarmGrid, FarmGridRemake, MassFarming or PlantEasily), disable it: they also move the plant ghost or replace planting. Plant Everything is no longer needed for the wild plants. If you keep it, it adds them instead of Fieldsmith and works together with Fieldsmith; if its own plant timers (`EnablePlantTimers`) are on, growing plants show two timers, so turn off one of them (`[Interface] ShowGrowTimer` in Fieldsmith).

## Configuration

`BepInEx/config/se.junge.valheim.fieldsmith.cfg`. Changes apply immediately, except `[General] Enabled` and `[Plants] AddWildPlants` (restart) and the starting values `DefaultSize` and `SnapToGrid` (next game start).

- `[General] Enabled` turns the mod on or off.
- `[Plants] AddWildPlants` (default on, restart) adds the wild plants to the Cultivator; `Costs` (`Seed` or `PlantEverything`, applies at once) sets their price; `TreesCostWood` (default on, applies at once) makes tree saplings and small trees cost 1 wood; `RemoveRadius` (0.5 to 15 m, default 3) is the area of Left Shift + remove.
- `[Harvest]` `Enabled`, `Size` (0 = planting pattern size, at least 3), `StaminaPerCrop` (0 to 25, default 0), `Replant` (default off).
- `[Costs]` `StaminaPerPlant` (0 to 25, default 1), `StaminaSkillReduction`, `DurabilityPerPlant`.
- `[Pattern]` `DefaultSize`, `MaximumSize` (up to 10).
- `[Grid]` `SnapToGrid`, `FixedSpacing` (0 = each plant's own spacing), `SpacingMargin`, `SpacingStep`, `FollowNearbyCrops`, `FollowRadius`, `RotationStep`, `UndoSteps`.
- `[Growth]` `GrowTimeHours` (0 to 24) and `GrowTimeMinutes` (0 to 59); both 0 = the game's times.
- `[Interface]` `ShowGrowTimer`, `ShowPanel`, `ShowPreview`, `PanelScale`.
- `[Keys]` `NextPattern`, `ToggleSnap`, `LockGrid`, `Undo`.

The log (`BepInEx/LogOutput.log`) lists the spacing Fieldsmith calculated for each plant the first time the Cultivator is used.

## Compatibility

Harmony patches: `Player.TryPlacePiece` (only for plants with the grid on), `Player.UpdatePlacementGhost` (moves the plant ghost), `Player.GetBuildStamina` (only right after a planting click), `Piece.SetCreator` (records the plants of a click for undo), `Pickable.Interact` (block harvest, only with the Alt place key held), `Plant.GetHoverText` (grow timer), `Player.CanRotatePiece` (only while a Fieldsmith modifier key is held), `ZNetScene.Awake` and `ObjectDB.Awake` (add the wild plants), `Piece.Awake` (fiddlehead yield of placed ones) and `Player.CheckCanRemovePiece` (wild plants cannot be removed), `Plant.GetGrowTime` (only with a grow time set) and `Pickable.GetHoverText` (only for picked ones, regrow timer). Other mods that move the placement ghost or replace planting have not been tested together with Fieldsmith. Plant Everything 1.21.3 was checked against its code (no shared placement patches; both mods add to `Piece.SetCreator` and `Plant.GetHoverText` without interfering), but not yet in a game.

## License

MIT. See `LICENSE` in the repository. The icon is original artwork made for this mod. Fieldsmith is a clean-room design; no code from other planting mods was used. The choice of wild plants and their costs follows the default configuration of Plant Everything by Advize (GPL-3.0); only those values were taken, no code, text or images.
