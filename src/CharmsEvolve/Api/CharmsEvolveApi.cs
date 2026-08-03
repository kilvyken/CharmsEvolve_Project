using System;
using System.Collections.Generic;
using UnityEngine;
using CharmsEvolve.Data;
using CharmsEvolve.Gameplay;
using CharmsEvolve.Icons;

namespace CharmsEvolve.Api
{
    /// <summary>
    /// 供其他 DLL 调用的稳定入口。
    /// </summary>
    public static class CharmsEvolveApi
    {
        private static CharmStateService _state;
        private static CharmTextureRegistry _textures;
        private static RuntimeModifierService _modifiers;
        private static ComboEngine _combos;

        public static event Action<GameplayDamageContext> BeforeOutgoingDamage;
        public static event Action<HeroDamagedContext> HeroDamaged;
        public static event Action<GeoGainContext> BeforeGeoGain;
        public static event Action EquipmentChanged;

        internal static void Initialize(
            CharmStateService state,
            CharmTextureRegistry textures,
            RuntimeModifierService modifiers,
            ComboEngine combos)
        {
            _state = state;
            _textures = textures;
            _modifiers = modifiers;
            _combos = combos;
            _state.EquipmentChanged += OnEquipmentChanged;
        }

        internal static void Shutdown()
        {
            if (_state != null)
                _state.EquipmentChanged -= OnEquipmentChanged;

            _state = null;
            _textures = null;
            _modifiers = null;
            _combos = null;
        }

        public static IList<CopyCharmDefinition> GetAllCharms()
        {
            return CharmDatabase.AllCopies;
        }

        public static CopyCharmDefinition GetCharm(string key)
        {
            return CharmDatabase.GetCopy(key);
        }

        public static bool IsOwned(string key)
        {
            return _state != null && _state.IsOwned(key);
        }

        public static bool IsEquipped(string key)
        {
            return _state != null && _state.IsEquipped(key);
        }

        public static int GetCopyCount(int originalCharmId)
        {
            return _state == null ? 0 : _state.GetCopyCount(originalCharmId);
        }

        public static bool SetOwned(string key, bool owned)
        {
            return _state != null && _state.SetOwned(key, owned);
        }

        public static bool SetEquipped(string key, bool equipped, out string reason)
        {
            if (_state == null)
            {
                reason = "CharmsEvolve 尚未初始化。";
                return false;
            }

            return _state.SetEquipped(key, equipped, out reason);
        }

        public static RuntimeModifierSnapshot GetRuntimeModifiers()
        {
            return _modifiers == null
                ? new RuntimeModifierSnapshot()
                : _modifiers.Snapshot;
        }

        public static IList<ActiveSynergy> GetActiveSynergies()
        {
            return _combos == null
                ? new List<ActiveSynergy>().AsReadOnly()
                : _combos.GetActiveSynergies();
        }

        /// <summary>
        /// 更改单个复制护符贴图。uv 使用 Unity 纹理坐标；整张图传 new Rect(0,0,1,1)。
        /// </summary>
        public static void RegisterTexture(string charmKey, Texture texture, Rect uv)
        {
            if (_textures == null)
                throw new InvalidOperationException("CharmsEvolve 尚未初始化。");
            _textures.Register(charmKey, texture, uv);
        }

        /// <summary>
        /// 从 PNG 文件加载整张贴图。
        /// </summary>
        public static bool RegisterTextureFromPng(string charmKey, string pngPath)
        {
            return _textures != null && _textures.RegisterPng(charmKey, pngPath);
        }

        public static bool ResetTexture(string charmKey)
        {
            return _textures != null && _textures.Unregister(charmKey);
        }

        internal static void RaiseBeforeOutgoingDamage(GameplayDamageContext context)
        {
            if (BeforeOutgoingDamage != null)
                BeforeOutgoingDamage(context);
        }

        internal static void RaiseHeroDamaged(HeroDamagedContext context)
        {
            if (HeroDamaged != null)
                HeroDamaged(context);
        }

        internal static void RaiseBeforeGeoGain(GeoGainContext context)
        {
            if (BeforeGeoGain != null)
                BeforeGeoGain(context);
        }

        private static void OnEquipmentChanged()
        {
            if (EquipmentChanged != null)
                EquipmentChanged();
        }
    }
}
