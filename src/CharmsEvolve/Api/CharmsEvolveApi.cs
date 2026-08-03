using System;
using System.Collections.Generic;
using UnityEngine;
using CharmsEvolve.Data;
using CharmsEvolve.Gameplay;
using CharmsEvolve.Icons;

namespace CharmsEvolve.Api
{
    public sealed class CharmCostContext
    {
        public string CharmKey;
        public int OriginalCharmId;
        public int Cost;

        public CharmCostContext(string charmKey, int originalCharmId, int cost)
        {
            CharmKey = charmKey ?? string.Empty;
            OriginalCharmId = originalCharmId;
            Cost = cost;
        }
    }

    public sealed class CharmDescriptionContext
    {
        public string CharmKey;
        public int OriginalCharmId;
        public bool IsVanillaCharm;
        public string Title;
        public string Description;

        public CharmDescriptionContext(
            string charmKey,
            int originalCharmId,
            bool isVanillaCharm,
            string title,
            string description)
        {
            CharmKey = charmKey ?? string.Empty;
            OriginalCharmId = originalCharmId;
            IsVanillaCharm = isVanillaCharm;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    public sealed class CharmEffectTickContext
    {
        public float DeltaTime;
        public float UnscaledDeltaTime;

        public CharmEffectTickContext(float deltaTime, float unscaledDeltaTime)
        {
            DeltaTime = deltaTime;
            UnscaledDeltaTime = unscaledDeltaTime;
        }
    }

    public sealed class SynergyEvaluationContext
    {
        public string SourceKey;
        public string Description;
        public int[] ReferencedOriginalIds;
        public bool Active;

        public SynergyEvaluationContext(
            string sourceKey,
            string description,
            int[] referencedOriginalIds,
            bool active)
        {
            SourceKey = sourceKey ?? string.Empty;
            Description = description ?? string.Empty;
            ReferencedOriginalIds = referencedOriginalIds ?? new int[0];
            Active = active;
        }
    }

    /// <summary>
    /// Stable extension surface for other DLLs and future generated charm modules.
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
        public static event Action<RuntimeModifierSnapshot> RuntimeModifiersBuilding;
        public static event Action<CharmCostContext> ResolveNotchCost;
        public static event Action<CharmDescriptionContext> BuildCharmDescription;
        public static event Action<SynergyEvaluationContext> EvaluateSynergy;
        public static event Action<CharmEffectTickContext> EffectTick;
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

        /// <summary>
        /// Returns vanilla + X/Y/Z equipped instances for an original charm id.
        /// This is the canonical equivalence query for future effect modules.
        /// </summary>
        public static int GetEffectiveCharmCount(int originalCharmId)
        {
            return _state == null ? 0 : _state.GetTotalStackCount(originalCharmId);
        }

        public static bool IsOriginalOrCopyEquipped(int originalCharmId)
        {
            return _state != null && _state.IsOriginalOrCopyEquipped(originalCharmId);
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

        public static int GetCharmCost(string key)
        {
            CopyCharmDefinition definition = CharmDatabase.GetCopy(key);
            return definition == null ? 0 : ResolveCharmCost(definition);
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
        /// Direct native-Sprite override. Recommended for the future custom charm sprites (currently 126 forms).
        /// The caller retains ownership of the Sprite.
        /// </summary>
        public static void RegisterSprite(string charmKey, Sprite sprite)
        {
            if (_textures == null)
                throw new InvalidOperationException("CharmsEvolve 尚未初始化。");
            _textures.RegisterSprite(charmKey, sprite);
        }

        /// <summary>
        /// Texture compatibility override. uv uses Unity texture coordinates.
        /// </summary>
        public static void RegisterTexture(string charmKey, Texture texture, Rect uv)
        {
            if (_textures == null)
                throw new InvalidOperationException("CharmsEvolve 尚未初始化。");
            _textures.Register(charmKey, texture, uv);
        }

        public static bool RegisterTextureFromPng(string charmKey, string pngPath)
        {
            return _textures != null && _textures.RegisterPng(charmKey, pngPath);
        }

        public static bool ResetTexture(string charmKey)
        {
            return _textures != null && _textures.Unregister(charmKey);
        }

        /// <summary>
        /// Lets future effect modules put a structured failure into BepInEx LogOutput.log.
        /// </summary>
        public static void ReportEffectError(string effectId, string message, Exception exception)
        {
            string prefix = "Charm effect " + (effectId ?? "<unknown>") + " failed: " + (message ?? string.Empty);
            if (exception == null)
                Plugin.Log.LogError(prefix);
            else
                Plugin.Log.LogError(prefix + "\n" + exception);
        }

        internal static int ResolveCharmCost(CopyCharmDefinition definition)
        {
            if (definition == null)
                return 0;

            CharmCostContext context = new CharmCostContext(
                definition.Key,
                definition.OriginalId,
                definition.Cost);
            InvokeHandlers(ResolveNotchCost, context, "ResolveNotchCost");
            return Math.Max(0, context.Cost);
        }

        internal static void RaiseBeforeOutgoingDamage(GameplayDamageContext context)
        {
            InvokeHandlers(BeforeOutgoingDamage, context, "BeforeOutgoingDamage");
        }

        internal static void RaiseHeroDamaged(HeroDamagedContext context)
        {
            InvokeHandlers(HeroDamaged, context, "HeroDamaged");
        }

        internal static void RaiseBeforeGeoGain(GeoGainContext context)
        {
            InvokeHandlers(BeforeGeoGain, context, "BeforeGeoGain");
        }

        internal static void RaiseRuntimeModifiersBuilding(RuntimeModifierSnapshot context)
        {
            InvokeHandlers(RuntimeModifiersBuilding, context, "RuntimeModifiersBuilding");
        }

        internal static void RaiseBuildCharmDescription(CharmDescriptionContext context)
        {
            InvokeHandlers(BuildCharmDescription, context, "BuildCharmDescription");
        }

        internal static void RaiseEvaluateSynergy(SynergyEvaluationContext context)
        {
            InvokeHandlers(EvaluateSynergy, context, "EvaluateSynergy");
        }

        internal static void RaiseEffectTick(CharmEffectTickContext context)
        {
            InvokeHandlers(EffectTick, context, "EffectTick");
        }

        private static void OnEquipmentChanged()
        {
            Action handlers = EquipmentChanged;
            if (handlers == null)
                return;

            Delegate[] calls = handlers.GetInvocationList();
            for (int i = 0; i < calls.Length; i++)
            {
                try
                {
                    ((Action)calls[i])();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("CharmsEvolveApi EquipmentChanged handler failed: " + ex);
                }
            }
        }

        private static void InvokeHandlers<T>(Action<T> handlers, T context, string eventName)
        {
            if (handlers == null)
                return;

            Delegate[] calls = handlers.GetInvocationList();
            for (int i = 0; i < calls.Length; i++)
            {
                try
                {
                    ((Action<T>)calls[i])(context);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("CharmsEvolveApi " + eventName + " handler failed: " + ex);
                }
            }
        }
    }
}
