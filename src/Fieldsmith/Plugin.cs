using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Fieldsmith
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(Flora.PlantEverythingGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private ConfigEntry<bool> modEnabled;
        private Harmony harmony;
        private bool failed;

        internal static Controller Controller { get; private set; }
        internal static ConfigEntry<float> RemoveRadius { get; private set; }

        private void Awake()
        {
            modEnabled = Config.Bind("General", "Enabled", true, "Enable Fieldsmith. Restart after changing the configuration file.");
            if (!modEnabled.Value)
            {
                Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} is disabled in its configuration file.");
                return;
            }
            if (UnityEngine.Application.isBatchMode)
            {
                Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} does nothing on a dedicated server; only players who plant need it.");
                return;
            }

            string access = GameAccess.Resolve();
            if (!GameAccess.Ready)
            {
                Logger.LogError($"{ModInfo.Name} {ModInfo.Version} is not compatible with this Valheim version (missing: {access}). Planting stays vanilla.");
                return;
            }
            if (access != null) Logger.LogWarning($"Some optional game members are missing ({access}): the tool may wear once per click, planting may raise the skill without the vanilla limit, or the grow timer is hidden.");

            instance = this;
            var settings = new Settings(Config);
            Flora.Initialize(Logger, settings.AddWildPlants.Value, settings.WildPlantCosts);
            RemoveRadius = settings.RemoveRadius;
            Growth.Initialize(settings.GrowTimeHours, settings.GrowTimeMinutes);
            Controller = new Controller(settings, Logger);
            harmony = new Harmony(ModInfo.Guid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception error)
            {
                // A partly applied set (for example placement without the stamina patch) must not stay active.
                harmony.UnpatchSelf();
                Controller.Destroy();
                Controller = null;
                Logger.LogError($"{ModInfo.Name} {ModInfo.Version} could not patch this Valheim version; planting stays vanilla. {error}");
                return;
            }
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded. Client-side grid planting; plants are ordinary objects.");
        }

        internal static void Fail(Exception error)
        {
            if (instance && !instance.failed) instance.Stop(error);
        }

        private void Stop(Exception error)
        {
            failed = true;
            Controller?.Halt();
            Logger.LogError($"Fieldsmith stopped for this session after an unexpected error: {error}");
        }

        private void Update()
        {
            if (Controller == null || failed) return;
            try
            {
                Controller.Update();
            }
            catch (Exception error)
            {
                Stop(error);
            }
        }

        private void OnGUI()
        {
            if (Controller == null || failed) return;
            try
            {
                Controller.OnGUI();
            }
            catch (Exception error)
            {
                Stop(error);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Controller?.Destroy();
            Controller = null;
            Plants.Clear();
            Flora.Clear();
            Growth.Clear();
            if (instance == this) instance = null;
        }
    }
}
