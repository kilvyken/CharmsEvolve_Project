using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using CharmsEvolve.Core;

namespace CharmsEvolve
{
    [BepInPlugin(
        "com.hollowknight.charmsevolve",
        "Charms Evolve",
        "0.3.1")]
    [BepInProcess("hollow_knight.exe")]
    [BepInProcess("hollow_knight.x86_64")]
    [BepInProcess("hollow_knight")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.hollowknight.charmsevolve";
        public const string PluginName = "Charms Evolve";
        public const string PluginVersion = "0.3.1";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        internal ConfigEntry<bool> AllowEquipAnywhere;
        internal ConfigEntry<bool> AllowCustomOvercharm;
        internal ConfigEntry<bool> UnlockAllCopies;
        internal ConfigEntry<bool> EnableExperimentalGameplayPatches;

        private Harmony _harmony;
        private ModRuntime _runtime;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            AllowEquipAnywhere = Config.Bind("Gameplay", "AllowEquipAnywhere", false,
                "允许不在长椅时装备复制护符。默认遵循原版长椅限制。");
            AllowCustomOvercharm = Config.Bind("Gameplay", "AllowCustomOvercharm", false,
                "允许复制护符使槽位超载。");
            UnlockAllCopies = Config.Bind("Gameplay", "UnlockAllCopies", true,
                "新存档默认拥有全部 120 个复制护符。");
            EnableExperimentalGameplayPatches = Config.Bind("Gameplay", "EnableExperimentalPatches", true,
                "启用通用伤害、吉欧、受伤回魂等运行时补丁。复杂表格联动仍通过公开接口扩展。");

            _runtime = new ModRuntime(this);
            _runtime.Initialize();

            _harmony = new Harmony(PluginGuid);
            _runtime.InstallPatches(_harmony);

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Update()
        {
            if (_runtime != null)
                _runtime.Tick();
        }

        private void LateUpdate()
        {
            if (_runtime != null)
                _runtime.LateTick();
        }

        private void OnApplicationQuit()
        {
            if (_runtime != null)
                _runtime.FlushSave();
        }

        private void OnDestroy()
        {
            if (_runtime != null)
                _runtime.Dispose();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            _harmony = null;
            _runtime = null;
            Instance = null;
        }
    }
}
