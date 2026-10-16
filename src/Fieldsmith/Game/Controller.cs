using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Fieldsmith
{
    // Per-frame state for the local player while a plant is selected: grid, pattern, input, preview, panel,
    // planting and undo.
    internal sealed partial class Controller
    {
        private const float RefreshSeconds = 0.5f;
        private const float FollowSeconds = 0.5f;
        private const float FollowMove = 0.25f;
        private const string Degree = "°";

        private readonly Settings settings;
        private readonly ManualLogSource log;
        private readonly Preview preview;
        private readonly UndoHistory<Batch> history;
        private readonly StringBuilder text = new StringBuilder(512);
        private readonly Collider[] found = new Collider[256];
        private readonly List<Cell> cells = new List<Cell>(GridMath.MaxPatternSize * GridMath.MaxPatternSize);
        private readonly List<Vector3> planPositions = new List<Vector3>();
        private readonly List<CellMark> planMarks = new List<CellMark>();
        private readonly List<(int Have, int PerPlant)> requirements = new List<(int, int)>();
        private readonly List<GameObject> captured = new List<GameObject>();
        private readonly int followMask;

        private PlantInfo active;
        private PieceTable registeredTable;
        private Piece unhandled;
        private int size;
        private bool snap;
        private double spacingOffset;
        private double manualRotation;
        private bool locked;
        private GridFrame lockedFrame;
        private GridFrame frame;
        private bool frameValid;
        private bool following;
        private Vector3 followPoint;
        private float followTime = -999f;
        private bool followFound;
        private GridFrame followFrame;
        private double followSpacing;
        private Vector3 rawAim;
        private bool aimValid;
        private float lift;
        private double scroll;
        private bool halted;
        private bool capturing;
        private int version;
        private int planVersion = -1;
        private float planTime = -999f;
        private PlanKey planKey;
        private int planFree;
        private int planReady;
        private int planCells;
        private int panelVersion = -1;
        private GUIContent panel = new GUIContent(string.Empty);
        private Vector2 panelSize;
        private GUIStyle panelStyle;
        private float panelScale;
        private ZNet session;

        // Plants placed by the click being processed; read once by the GetBuildStamina patch.
        internal int PendingPlants { get; set; }
        internal bool ConsumesScroll { get; private set; }
        internal bool Capturing => capturing;
        internal bool Halted => halted;
        internal bool ShowGrowTimer => settings.ShowGrowTimer.Value;

        // What the planned cells depend on besides the world: block position, size and grid.
        private readonly struct PlanKey : IEquatable<PlanKey>
        {
            private readonly int startI;
            private readonly int startJ;
            private readonly int size;
            private readonly GridFrame frame;
            private readonly bool locked;

            internal PlanKey(int startI, int startJ, int size, GridFrame frame, bool locked)
            {
                this.startI = startI;
                this.startJ = startJ;
                this.size = size;
                this.frame = frame;
                this.locked = locked;
            }

            public bool Equals(PlanKey other) =>
                startI == other.startI && startJ == other.startJ && size == other.size && locked == other.locked &&
                frame.OriginX == other.frame.OriginX && frame.OriginZ == other.frame.OriginZ &&
                frame.RotationDegrees == other.frame.RotationDegrees && frame.Spacing == other.frame.Spacing;

            public override bool Equals(object obj) => obj is PlanKey other && Equals(other);

            public override int GetHashCode() => (startI * 397) ^ startJ ^ (size << 20);
        }

        private sealed class Batch
        {
            internal readonly List<GameObject> Plants = new List<GameObject>();
            internal Piece Piece;
            internal bool Charged;
            internal bool Objects;
            // Server time (ticks) right after the click, for objects picked since.
            internal long Ticks;
        }

        internal Controller(Settings settings, ManualLogSource log)
        {
            this.settings = settings;
            this.log = log;
            preview = new Preview();
            if (!preview.Available) log.LogWarning("No line shader found; the ground markers are disabled. Planting still works.");
            history = new UndoHistory<Batch>(settings.UndoSteps.Value);
            size = GridMath.ClampSize(settings.DefaultPatternSize.Value, settings.MaxPatternSize.Value);
            snap = settings.SnapToGrid.Value;
            followMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item");
            Plants.Initialize(log, settings.SpacingMargin.Value);
            settings.UndoSteps.SettingChanged += (_, __) => history.Capacity = settings.UndoSteps.Value;
            settings.MaxPatternSize.SettingChanged += (_, __) =>
            {
                size = GridMath.ClampSize(size, settings.MaxPatternSize.Value);
                version++;
            };
            settings.SpacingMargin.SettingChanged += (_, __) =>
            {
                Plants.SetMargin(settings.SpacingMargin.Value);
                active = null;
                registeredTable = null;
                version++;
            };
            EventHandler changed = (_, __) => version++;
            settings.FixedSpacing.SettingChanged += changed;
            settings.FollowNearbyCrops.SettingChanged += changed;
            settings.FollowRadius.SettingChanged += changed;
            settings.StaminaPerPlant.SettingChanged += changed;
            settings.StaminaSkillReduction.SettingChanged += changed;
        }

        internal void Halt()
        {
            halted = true;
            ConsumesScroll = false;
            capturing = false;
            preview.Hide();
        }

        internal void Destroy()
        {
            preview.Destroy();
            history.Clear();
        }

        // Plants Fieldsmith places itself. With snapping off, plants go through vanilla (one at a time) and
        // only the stamina setting applies.
        internal bool Handles(Player player, Piece piece, out PlantInfo info)
        {
            info = null;
            if (halted || !snap || !player || player != Player.m_localPlayer || player.InInterior()) return false;
            if (!Plants.TryGet(piece, out info)) return false;
            // Deep snow rules are left to vanilla.
            if (!info.Piece.m_allowedInDeepSnow && player.GetCurrentBiome() == Heightmap.Biome.DeepNorth)
            {
                info = null;
                return false;
            }
            return true;
        }

        // Plants and planting-tool objects, including those Fieldsmith leaves to vanilla (wall vines).
        internal static bool IsPlant(Piece piece) => piece && (piece.GetComponent<Plant>() || Plants.TryGet(piece, out _));

        internal void Update()
        {
            PendingPlants = 0;
            ConsumesScroll = false;
            Player player = Player.m_localPlayer;
            if (session != ZNet.instance)
            {
                // A new world or connection: the plants of the previous session are gone.
                session = ZNet.instance;
                history.Clear();
                replants.Clear();
                locked = false;
                registeredTable = null;
            }
            UpdateReplants(player);
            if (!player || !GameAccess.Ready)
            {
                Deactivate();
                return;
            }
            Piece selected = player.InPlaceMode() ? player.GetSelectedPiece() : null;
            if (!Plants.TryGet(selected, out PlantInfo info) && selected && selected != unhandled)
            {
                // Other mods may add objects to the planting tool while playing (Plant Everything after a
                // configuration change); the menu is read again once per newly selected piece.
                unhandled = selected;
                Plants.RegisterTable(player.GetBuildTool());
                Plants.TryGet(selected, out info);
            }
            if (info == null)
            {
                Deactivate();
                return;
            }
            unhandled = null;
            if (info != active) Activate(player, info);
            if (InputAllowed(player)) HandleInput(player);
            UpdatePlan(player);
        }

        private void Activate(Player player, PlantInfo info)
        {
            active = info;
            scroll = 0;
            planVersion = -1;
            PieceTable table = player.GetBuildTool();
            if (table != registeredTable)
            {
                registeredTable = table;
                Plants.RegisterTable(table);
            }
            version++;
        }

        private void Deactivate()
        {
            if (active != null)
            {
                active = null;
                version++;
            }
            aimValid = false;
            frameValid = false;
            preview.Hide();
        }

        private double Spacing(PlantInfo info)
        {
            double fixedSpacing = settings.FixedSpacing.Value;
            double baseSpacing = fixedSpacing > 0 ? fixedSpacing : info.AutoSpacing;
            return Math.Max(MinimumSpacing(info), Math.Round(baseSpacing + spacingOffset, 3));
        }

        // Plants closer than their grow radius (objects: their reach) always block each other.
        private static double MinimumSpacing(PlantInfo info) => Math.Max(GridMath.MinSpacing, Math.Round(info.Space, 3));

        // Called after every vanilla UpdatePlacementGhost (LateUpdate and TryPlacePiece): moves the ghost to the
        // nearest grid cell and replaces the position-dependent status with the status of that cell.
        internal void OnGhostUpdated(Player player)
        {
            if (!Handles(player, player.GetSelectedPiece(), out PlantInfo info))
            {
                aimValid = false;
                return;
            }
            GameObject ghost = GameAccess.PlacementGhost(player);
            Player.PlacementStatus status = player.GetPlacementStatus();
            if (!ghost || !ghost.activeSelf || status == Player.PlacementStatus.NoRayHits)
            {
                aimValid = false;
                return;
            }
            Vector3 raw = ghost.transform.position;
            // Only aims at the terrain itself are moved to the grid; anything else keeps the vanilla result.
            if (!Field.IsPositional(status) || !Field.TryLift(raw, out float groundLift))
            {
                aimValid = false;
                return;
            }
            rawAim = raw;
            aimValid = true;
            lift = groundLift;
            frame = CurrentFrame(info, raw);
            frameValid = true;
            Cell cell = GridMath.Nearest(frame, raw.x, raw.z);
            CellStatus cellStatus = Field.Evaluate(info, cell.X, cell.Z, lift, raw.y, false, out Vector3 position);
            ghost.transform.position = position;
            GameAccess.PlacementStatus(player) = Field.ToPlacement(cellStatus);
            GameAccess.SetPlacementGhostValid(player, cellStatus == CellStatus.Ok);
        }

        private GridFrame CurrentFrame(PlantInfo info, Vector3 aim)
        {
            double spacing = Spacing(info);
            following = false;
            if (locked) return lockedFrame.WithSpacing(spacing);
            if (settings.FollowNearbyCrops.Value && Follow(aim, spacing, out GridFrame followed))
            {
                following = true;
                return followed;
            }
            return new GridFrame(0, 0, manualRotation, spacing);
        }

        // Continues the rows of the plant nearest to the aim: the grid starts at that plant and takes the
        // direction from it to a neighbour about one spacing away. Searched again only when the aim moved.
        private bool Follow(Vector3 aim, double spacing, out GridFrame result)
        {
            float now = Time.time;
            if ((aim - followPoint).sqrMagnitude < FollowMove * FollowMove && now - followTime < FollowSeconds &&
                Math.Abs(spacing - followSpacing) < 1e-6)
            {
                result = followFrame;
                return followFound;
            }
            followPoint = aim;
            followTime = now;
            followSpacing = spacing;
            followFound = false;
            result = default;

            int count = Physics.OverlapSphereNonAlloc(aim, settings.FollowRadius.Value, found, followMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Vector3 anchor = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                if (!found[i] || !Plants.IsCrop(found[i], out Vector3 position)) continue;
                float distance = Horizontal(position, aim);
                if (distance < best)
                {
                    best = distance;
                    anchor = position;
                }
            }
            if (best == float.MaxValue) return false;

            double rotation = manualRotation;
            count = Physics.OverlapSphereNonAlloc(anchor, (float)(spacing * 1.3), found, followMask, QueryTriggerInteraction.Collide);
            double bestError = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!found[i] || !Plants.IsCrop(found[i], out Vector3 position)) continue;
                if (!GridMath.TryRowRotation(anchor.x, anchor.z, position.x, position.z, spacing, out double candidate)) continue;
                double error = Math.Abs(Horizontal(position, anchor) - spacing);
                if (error < bestError)
                {
                    bestError = error;
                    rotation = candidate;
                }
            }
            followFrame = new GridFrame(anchor.x, anchor.z, rotation, spacing);
            followFound = true;
            result = followFrame;
            return true;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Replaces TryPlacePiece for a handled plant. Returns whether the original should run instead (a single
        // vanilla placement when the aim is not on the terrain itself, such as a plant on a wooden floor).
        internal bool TryPlace(Player player, PlantInfo info, ref bool placed)
        {
            placed = false;
            GameAccess.UpdatePlacementGhost(player, true);
            if (halted) return false;
            GameObject ghost = GameAccess.PlacementGhost(player);
            Player.PlacementStatus status = player.GetPlacementStatus();
            if (!ghost || !aimValid || !frameValid)
            {
                if (ghost && status == Player.PlacementStatus.Valid) return true;
                player.Message(MessageHud.MessageType.Center, Field.Message(status));
                return false;
            }

            Piece piece = info.Piece;
            GridMath.Pattern(frame, rawAim.x, rawAim.z, size, cells);
            var targets = new List<Cell>(cells.Count);
            CellStatus firstProblem = CellStatus.Ok;
            int blocked = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                CellStatus cellStatus = Field.Evaluate(info, cells[i].X, cells[i].Z, lift, rawAim.y, i == 0, out _);
                if (cellStatus == CellStatus.Ok)
                {
                    targets.Add(cells[i]);
                    continue;
                }
                blocked++;
                if (firstProblem == CellStatus.Ok) firstProblem = cellStatus;
            }
            if (targets.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, Field.Message(firstProblem));
                return false;
            }

            bool freeKey = ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
            int seeds = SeedBudget(player, piece, freeKey);
            int stamina = Budget.Stamina(player.GetStamina(), StaminaPerPlant(player) * Game.m_staminaRate);
            int limit = Budget.Plants(targets.Count, seeds, stamina);
            if (limit <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
                return false;
            }

            bool cheated = (player.GetInventory().ItemCheated(piece.m_resources) || player.NoCostCheat()) && !PlayerProfile.s_bypassCheatChecks;
            PlayerProfile profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
            var batch = new Batch { Piece = piece, Charged = !freeKey && !player.PlacementCostDisabled, Objects = info.IsObject };
            Quaternion rotation = ghost.transform.rotation;
            int count = 0;
            int lateBlocked = 0;
            captured.Clear();
            capturing = true;
            try
            {
                for (int i = 0; i < targets.Count && count < limit; i++)
                {
                    // Checked again right before placing, so plants of this click also see each other.
                    CellStatus cellStatus = Field.Evaluate(info, targets[i].X, targets[i].Z, lift, rawAim.y, false, out Vector3 position);
                    if (cellStatus != CellStatus.Ok)
                    {
                        lateBlocked++;
                        if (firstProblem == CellStatus.Ok) firstProblem = cellStatus;
                        continue;
                    }
                    Quaternion plantRotation = count == 0 || !piece.m_randomInitBuildRotation
                        ? rotation
                        : Quaternion.Euler(0f, UnityEngine.Random.Range(0, 16) * 22.5f, 0f);
                    if (Game.instance) Game.instance.IncrementPlayerStat(PlayerStatType.Builds, 1f, cheated);
                    if (profile != null) profile.IncrementStatBuildPiecePlaced(piece.m_name, 1f, cheated);
                    player.PlacePiece(piece, position, plantRotation, count == 0, cheated);
                    count++;
                    placed = true;
                }
            }
            finally
            {
                capturing = false;
                if (ZNet.instance) batch.Ticks = ZNet.instance.GetTime().Ticks;
                batch.Plants.AddRange(captured);
                captured.Clear();
                if (batch.Plants.Count > 0) history.Push(batch);
            }
            blocked += lateBlocked;
            if (count == 0)
            {
                player.Message(MessageHud.MessageType.Center, Field.Message(firstProblem));
                return false;
            }

            // Vanilla UpdatePlacement charges one plant's seeds, the stamina (GetBuildStamina, patched to
            // PendingPlants x the setting), one skill raise and one tool wear after this returns true.
            int extra = count - 1;
            if (extra > 0)
            {
                if (!freeKey) player.ConsumeResources(piece.m_resources, 0, -1, extra);
                RaiseSkill(player, extra);
                if (settings.DurabilityPerPlant.Value) WearTool(player, extra);
            }
            PendingPlants = count;
            planVersion = -1;
            version++;

            if (count < cells.Count)
            {
                var reasons = new List<string>(2);
                if (blocked > 0) reasons.Add(blocked + " " + Plants.Localize(Field.Message(firstProblem)));
                int lacking = targets.Count - lateBlocked - count;
                if (lacking > 0) reasons.Add(lacking + (seeds < stamina ? " not enough seeds" : " not enough stamina"));
                player.Message(MessageHud.MessageType.TopLeft, $"Planted {count} of {cells.Count} ({string.Join(", ", reasons.ToArray())})");
            }
            else if (count > 1)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Planted {count}");
            }
            return false;
        }

        // One skill raise per extra plant, honouring vanilla's place-and-remove debt.
        private static void RaiseSkill(Player player, int plants)
        {
            PieceTable table = player.GetBuildTool();
            if (table != null) RaiseSkill(player, table.m_skill, plants);
        }

        private static void RaiseSkill(Player player, Skills.SkillType skill, int plants)
        {
            if (skill == Skills.SkillType.None) return;
            for (int i = 0; i < plants; i++)
            {
                if (GameAccess.BuildRemoveDebt != null)
                {
                    ref int debt = ref GameAccess.BuildRemoveDebt(player);
                    if (debt > 0)
                    {
                        debt--;
                        continue;
                    }
                }
                player.RaiseSkill(skill);
            }
        }

        private static void WearTool(Player player, int plants)
        {
            if (!GameAccess.WearAvailable) return;
            ItemDrop.ItemData tool = GameAccess.RightItem(player);
            if (tool == null || !tool.m_shared.m_useDurability) return;
            float wear = GameAccess.PlaceDurability(player, tool) * Game.m_durabilityRate * plants;
            if (wear > 0f) tool.m_durability = Mathf.Max(0f, tool.m_durability - wear);
        }

        private int SeedBudget(Player player, Piece piece, bool freeKey)
        {
            requirements.Clear();
            Inventory inventory = player.GetInventory();
            foreach (Piece.Requirement requirement in piece.m_resources)
            {
                if (requirement == null || !requirement.m_resItem || requirement.m_amount <= 0) continue;
                requirements.Add((inventory.CountItems(requirement.m_resItem.m_itemData.m_shared.m_name), requirement.m_amount));
            }
            return Budget.Seeds(requirements, freeKey || player.PlacementCostDisabled);
        }

        // Base stamina per plant before the world's stamina rate (applied by vanilla UseStamina).
        private float StaminaPerPlant(Player player)
        {
            PieceTable table = player.GetBuildTool();
            double factor = table != null && table.m_skill != Skills.SkillType.None ? player.GetSkillFactor(table.m_skill) : 0;
            return (float)Budget.StaminaPerPlant(settings.StaminaPerPlant.Value, factor, settings.StaminaSkillReduction.Value);
        }

        // Called by the GetBuildStamina patch right after a successful planting.
        internal bool TryTakeStamina(Player player, out float stamina)
        {
            stamina = 0f;
            if (halted || PendingPlants <= 0 || player != Player.m_localPlayer) return false;
            stamina = StaminaPerPlant(player) * PendingPlants;
            PendingPlants = 0;
            return true;
        }

        internal void Capture(Piece piece)
        {
            if (capturing && piece) captured.Add(piece.gameObject);
        }

        private void Undo(Player player)
        {
            if (!history.TryPeek(out Batch batch))
            {
                player.Message(MessageHud.MessageType.Center, "Nothing to undo");
                return;
            }
            var alive = new List<GameObject>();
            foreach (GameObject plant in batch.Plants)
            {
                if (!plant) continue;
                ZNetView view = plant.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || !view.IsOwner()) continue;
                // A plant that has grown is a different object. An object stays the same, but once it has been
                // picked its seeds are not returned (the harvest was already taken).
                if (batch.Objects ? PickedSince(plant, view, batch.Ticks) : !plant.GetComponent<Plant>()) continue;
                alive.Add(plant);
            }
            if (alive.Count == 0)
            {
                history.TryPop(out _);
                player.Message(MessageHud.MessageType.Center, "Nothing left to undo: those plants have grown, been picked, been removed or are out of reach");
                version++;
                return;
            }
            history.TryPop(out _);
            foreach (GameObject plant in alive)
            {
                if (ZNetScene.instance) ZNetScene.instance.Destroy(plant);
                else UnityEngine.Object.Destroy(plant);
            }
            // Like removing a piece in vanilla: later placements do not raise the skill again for these.
            PieceTable table = player.GetBuildTool();
            if (GameAccess.BuildRemoveDebt != null && table != null && table.m_skill != Skills.SkillType.None)
            {
                ref int debt = ref GameAccess.BuildRemoveDebt(player);
                debt = Math.Min(20, debt + alive.Count);
            }
            if (batch.Charged && batch.Piece)
            {
                foreach (Piece.Requirement requirement in batch.Piece.m_resources)
                {
                    if (requirement == null || !requirement.m_resItem || requirement.m_amount <= 0) continue;
                    Refund(player, requirement.m_resItem, requirement.m_amount * alive.Count);
                }
            }
            planVersion = -1;
            version++;
            player.Message(MessageHud.MessageType.TopLeft, $"Undone: removed {alive.Count} plant{(alive.Count == 1 ? string.Empty : "s")}");
        }

        // Pickable.SetPicked stores the server time of a pick in the ZDO when the object respawns; objects that
        // were placed already picked (Plant Everything's ResourcesSpawnEmpty) carry the time of the click.
        private static bool PickedSince(GameObject placed, ZNetView view, long clickTicks)
        {
            Pickable pickable = placed.GetComponent<Pickable>();
            if (!pickable) return false;
            return Undoing.PickedSince(pickable.GetPicked(), view.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L), clickTicks);
        }

        // Returns seeds to the inventory; whatever does not fit is dropped at the player's feet.
        private static void Refund(Player player, ItemDrop item, int amount)
        {
            Inventory inventory = player.GetInventory();
            GameObject prefab = item.gameObject;
            int stack = Math.Max(1, item.m_itemData.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                int chunk = Math.Min(stack, amount);
                amount -= chunk;
                if (inventory.AddItem(prefab, chunk)) continue;
                ItemDrop.ItemData data = item.m_itemData.Clone();
                data.m_dropPrefab = prefab;
                data.m_worldLevel = (byte)Game.m_worldLevel;
                ItemDrop.DropItem(data, chunk, player.transform.position + Vector3.up, Quaternion.identity);
            }
        }

        private static bool InputAllowed(Player player) =>
            !player.IsDead() && !Hud.IsPieceSelectionVisible() && !Hud.InRadial() && !global::Console.IsVisible() &&
            !(Chat.instance && Chat.instance.HasFocus()) && !Menu.IsVisible() && !InventoryGui.IsVisible() &&
            !Minimap.IsOpen() && !TextInput.IsVisible() && !StoreGui.IsVisible() && !GameCamera.InFreeFly() &&
            !(Hud.instance && Hud.instance.m_buildUi && Hud.instance.m_buildUi.SearchFieldFocused);

        private void HandleInput(Player player)
        {
            bool shift = Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);
            bool ctrl = Key(KeyCode.LeftControl) || Key(KeyCode.RightControl);
            bool alt = Key(KeyCode.LeftAlt) || Key(KeyCode.RightAlt) || Key(KeyCode.AltGr);
            // GameCamera keeps zooming while the aim hits nothing, so the wheel is only taken while aiming.
            ConsumesScroll = aimValid && snap && (shift || ctrl || alt);
            if (ConsumesScroll)
            {
                int steps = Steps.ConsumeScroll(ref scroll, ZInput.GetMouseScrollWheel(), player.m_scrollAmountThreshold);
                if (steps != 0)
                {
                    if (shift && !ctrl && !alt) size = GridMath.ClampSize(size + steps, settings.MaxPatternSize.Value);
                    else if (ctrl && !shift && !alt) AdjustSpacing(steps);
                    else if (alt && !shift && !ctrl) Rotate(player, steps);
                    version++;
                }
            }
            else
            {
                scroll = 0;
            }

            if (Pressed(settings.NextPattern.Value))
            {
                if (!snap)
                {
                    snap = true;
                    player.Message(MessageHud.MessageType.TopLeft, "Grid on");
                }
                size = GridMath.NextSize(size, settings.MaxPatternSize.Value);
                player.Message(MessageHud.MessageType.TopLeft, $"Pattern {size} x {size}");
                version++;
            }
            if (Pressed(settings.ToggleSnap.Value))
            {
                snap = !snap;
                player.Message(MessageHud.MessageType.TopLeft, snap ? "Grid on" : "Grid off: one plant at a time");
                version++;
            }
            if (Pressed(settings.LockGrid.Value) && snap)
            {
                if (locked)
                {
                    locked = false;
                    player.Message(MessageHud.MessageType.TopLeft, "Grid unlocked");
                }
                else if (frameValid)
                {
                    locked = true;
                    lockedFrame = frame;
                    player.Message(MessageHud.MessageType.TopLeft, "Grid locked");
                }
                version++;
            }
            if (Pressed(settings.Undo.Value))
            {
                try
                {
                    if (halted) player.Message(MessageHud.MessageType.Center, "Fieldsmith stopped after an error.");
                    else Undo(player);
                }
                catch (Exception error)
                {
                    log.LogError($"Undo failed: {error}");
                    player.Message(MessageHud.MessageType.Center, "Undo failed. See BepInEx/LogOutput.log.");
                }
                version++;
            }
        }

        private void AdjustSpacing(int steps)
        {
            if (active == null) return;
            double fixedSpacing = settings.FixedSpacing.Value;
            double baseSpacing = fixedSpacing > 0 ? fixedSpacing : active.AutoSpacing;
            double target = Steps.Adjust(baseSpacing + spacingOffset, steps, settings.SpacingStep.Value, MinimumSpacing(active), 10);
            spacingOffset = Math.Round(target - baseSpacing, 4);
            if (Math.Abs(spacingOffset) < 1e-4) spacingOffset = 0;
        }

        // Without nearby plants the free grid turns. Turning a grid that follows plants or is locked locks it
        // at its current origin, so the turn sticks.
        private void Rotate(Player player, int steps)
        {
            double step = settings.RotationStep.Value;
            if (locked)
            {
                lockedFrame = lockedFrame.WithRotation(lockedFrame.RotationDegrees + steps * step);
                return;
            }
            if (following && frameValid)
            {
                locked = true;
                lockedFrame = frame.WithRotation(frame.RotationDegrees + steps * step);
                player.Message(MessageHud.MessageType.TopLeft, "Grid locked");
                return;
            }
            manualRotation = GridMath.NormalizeQuarter(manualRotation + steps * step);
        }

        // Cell states for the preview and panel: recalculated when the aim moves to another position, the
        // settings change, or periodically for changes by others.
        private void UpdatePlan(Player player)
        {
            if (!aimValid || !frameValid || !snap || active == null)
            {
                preview.Hide();
                if (planCells != 0)
                {
                    planCells = planFree = planReady = 0;
                    version++;
                }
                return;
            }
            float now = Time.time;
            GridMath.PatternStart(frame, rawAim.x, rawAim.z, size, out int startI, out int startJ);
            var key = new PlanKey(startI, startJ, size, frame, locked);
            if (planVersion == version && key.Equals(planKey) && now - planTime < RefreshSeconds) return;
            planVersion = version;
            planTime = now;
            planKey = key;

            GridMath.Pattern(frame, rawAim.x, rawAim.z, size, cells);
            planPositions.Clear();
            planMarks.Clear();
            int free = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                CellStatus status = Field.Evaluate(active, cells[i].X, cells[i].Z, lift, rawAim.y, false, out Vector3 position);
                planPositions.Add(position);
                planMarks.Add(status == CellStatus.Ok ? CellMark.Plant : CellMark.Blocked);
                if (status == CellStatus.Ok) free++;
            }
            bool freeKey = ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(active.Piece.FreeBuildKey());
            int seeds = SeedBudget(player, active.Piece, freeKey);
            int stamina = Budget.Stamina(player.GetStamina(), StaminaPerPlant(player) * Game.m_staminaRate);
            int ready = Budget.Plants(free, seeds, stamina);
            int marked = 0;
            for (int i = 0; i < planMarks.Count; i++)
            {
                if (planMarks[i] != CellMark.Plant) continue;
                if (marked++ >= ready) planMarks[i] = CellMark.Limited;
            }
            if (free != planFree || ready != planReady || cells.Count != planCells) panelVersion = -1;
            planFree = free;
            planReady = ready;
            planCells = cells.Count;
            if (settings.ShowPreview.Value && preview.Available) preview.Show(planPositions, planMarks, frame, size, rawAim, locked);
            else preview.Hide();
        }

        internal void OnGUI()
        {
            if (active == null || !settings.ShowPanel.Value || !Player.m_localPlayer || Hud.IsUserHidden() ||
                Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || Hud.IsPieceSelectionVisible()) return;
            float scale = settings.PanelScale.Value * Mathf.Max(1f, Screen.height / 1080f);
            if (panelStyle == null || !Mathf.Approximately(panelScale, scale))
            {
                panelScale = scale;
                panelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                    wordWrap = false,
                    fontSize = Mathf.RoundToInt(14 * scale),
                    padding = new RectOffset(Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(8 * scale), Mathf.RoundToInt(8 * scale))
                };
                panelStyle.normal.textColor = new Color(1f, 0.95f, 0.85f);
                panelVersion = -1;
            }
            if (panelVersion != version)
            {
                panelVersion = version;
                panel = new GUIContent(BuildPanel(Player.m_localPlayer));
                panelSize = panelStyle.CalcSize(panel);
            }
            var area = new Rect(Mathf.Round(20 * scale), Mathf.Round(Screen.height * 0.4f), panelSize.x, panelSize.y);
            GUI.Box(area, panel, panelStyle);
        }

        private string BuildPanel(Player player)
        {
            text.Length = 0;
            if (active == null) return string.Empty;
            text.Append("<b>Fieldsmith: ").Append(active.DisplayName).Append("</b>\n");
            if (!snap)
            {
                text.Append("Grid off: one plant at a time   ").Append(KeyName(settings.ToggleSnap.Value)).Append(" grid on\n");
            }
            else
            {
                text.Append("Pattern ").Append(size).Append(" x ").Append(size).Append("   Shift+Wheel, ").Append(KeyName(settings.NextPattern.Value)).Append('\n');
                double fixedSpacing = settings.FixedSpacing.Value;
                double spacing = Spacing(active);
                text.Append("Spacing ").Append(Metres(spacing))
                    .Append(spacingOffset != 0 ? " (adjusted)" : fixedSpacing > 0 ? " (fixed)" : " (auto)")
                    .Append("   Ctrl+Wheel\n");
                if (spacing < active.TightSpacing - 1e-6)
                    text.Append("<color=#ffb050>Tight: plants may block each other's growth</color>\n");
                if (locked)
                    text.Append("Grid locked at ").Append(Angle(lockedFrame.RotationDegrees)).Append("   Alt+Wheel, ").Append(KeyName(settings.LockGrid.Value)).Append(" unlock\n");
                else if (following)
                    text.Append("Following nearby rows at ").Append(Angle(frame.RotationDegrees)).Append("   ").Append(KeyName(settings.LockGrid.Value)).Append(" lock\n");
                else
                    text.Append("Free grid at ").Append(Angle(manualRotation)).Append("   Alt+Wheel, ").Append(KeyName(settings.LockGrid.Value)).Append(" lock\n");
                if (planCells > 0)
                {
                    text.Append("Plants here: ").Append(planReady).Append(" of ").Append(planCells);
                    if (planReady < planFree) text.Append(" (").Append(planFree - planReady).Append(" lack seeds or stamina)");
                    text.Append('\n');
                }
                text.Append(KeyName(settings.ToggleSnap.Value)).Append(" grid off\n");
            }
            float perPlant = StaminaPerPlant(player);
            text.Append("Stamina ").Append(perPlant.ToString("0.##", CultureInfo.InvariantCulture)).Append(" per plant\n");
            if (settings.HarvestBlocks.Value)
            {
                int side = HarvestSide();
                text.Append("Shift+E on a ripe crop: harvest ").Append(side).Append(" x ").Append(side)
                    .Append(settings.Replant.Value ? " and replant\n" : "\n");
            }
            int steps = history.Count;
            text.Append(KeyName(settings.Undo.Value)).Append(" undo").Append(steps > 0 ? " (" + steps + ")" : string.Empty);
            return text.ToString();
        }

        private static string Metres(double value) => value.ToString("0.00", CultureInfo.InvariantCulture) + " m";

        private static string Angle(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + Degree;

        private static string KeyName(KeyboardShortcut shortcut) =>
            shortcut.MainKey == KeyCode.None ? "(unbound)" : shortcut.ToString();

        // Keys go through the game's input layer. A key without a counterpart there counts as not pressed.
        private static bool Key(KeyCode key)
        {
            try
            {
                return ZInput.GetKey(key, false);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static readonly KeyCode[] ModifierKeys =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr
        };

        // Exact combination, like BepInEx KeyboardShortcut: the main key went down this frame, every listed
        // modifier is held and no other Shift, Ctrl or Alt key is.
        private static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            try
            {
                if (!ZInput.GetKeyDown(shortcut.MainKey, false)) return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!Key(modifier)) return false;
            foreach (KeyCode modifier in ModifierKeys)
            {
                if (modifier == shortcut.MainKey || !Key(modifier)) continue;
                bool listed = false;
                foreach (KeyCode wanted in shortcut.Modifiers) listed |= wanted == modifier;
                if (!listed) return false;
            }
            return true;
        }
    }
}
