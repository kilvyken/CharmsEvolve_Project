using System;
using HarmonyLib;
using CharmsEvolve.Api;
using CharmsEvolve.Gameplay;
using CharmsEvolve.Icons;
using CharmsEvolve.Interop;
using CharmsEvolve.Save;
using CharmsEvolve.UI;

namespace CharmsEvolve.Core
{
    internal sealed class ModRuntime : IDisposable
    {
        private readonly Plugin _plugin;
        private readonly SaveRepository _saveRepository;
        private readonly CharmStateService _state;
        private readonly CharmTextureRegistry _textures;
        private readonly RuntimeModifierService _modifiers;
        private readonly ComboEngine _combos;
        private readonly EffectRuntime _effects;
        private readonly CharmPageController _ui;
        private readonly HarmonyBridge _bridge;

        private int _loadedSlot = int.MinValue;
        private float _slotCheckTimer;

        public ModRuntime(Plugin plugin)
        {
            _plugin = plugin;
            _saveRepository = new SaveRepository();
            _state = new CharmStateService(plugin, _saveRepository);
            _textures = new CharmTextureRegistry();
            _modifiers = new RuntimeModifierService(_state);
            _combos = new ComboEngine(_state);
            _effects = new EffectRuntime(_state, _modifiers, _combos);
            _ui = new CharmPageController(plugin, _state, _textures, _combos);
            _bridge = new HarmonyBridge(_effects, _state);
        }

        public void Initialize()
        {
            CharmsEvolveApi.Initialize(_state, _textures, _modifiers, _combos);
            TryLoadCurrentSlot(true);
        }

        public void InstallPatches(Harmony harmony)
        {
            _bridge.Install(harmony, _plugin.EnableExperimentalGameplayPatches.Value);
            _ui.InstallPatches(harmony);
        }

        public void Tick()
        {
            _slotCheckTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (_slotCheckTimer <= 0f)
            {
                _slotCheckTimer = 0.75f;
                TryLoadCurrentSlot(false);
            }

            _state.Tick();
            _modifiers.RebuildIfDirty();
            _effects.Tick();
            _ui.Tick();
        }

        public void LateTick()
        {
            _ui.LateTick();
        }

        public void FlushSave()
        {
            _state.FlushSave();
        }

        private void TryLoadCurrentSlot(bool force)
        {
            int slot = GameReflection.GetCurrentSaveSlot();
            if (!force && slot == _loadedSlot)
                return;

            _loadedSlot = slot;
            _state.LoadSlot(slot);
            _modifiers.MarkDirty();
        }

        public void Dispose()
        {
            FlushSave();
            _ui.Dispose();
            _textures.Dispose();
            CharmsEvolveApi.Shutdown();
        }
    }
}
