using System;
using UnityEngine;
using CharmsEvolve.Interop;
using CharmsEvolve.Api;

namespace CharmsEvolve.Gameplay
{
    internal sealed class EffectRuntime
    {
        private readonly CharmStateService _state;
        private readonly RuntimeModifierService _modifiers;
        private readonly ComboEngine _combos;
        private bool _addingSoul;
        private float _geoTimer;
        private float _sharpShadowSoulCooldown;

        public EffectRuntime(
            CharmStateService state,
            RuntimeModifierService modifiers,
            ComboEngine combos)
        {
            _state = state;
            _modifiers = modifiers;
            _combos = combos;
        }

        public void Tick()
        {
            _sharpShadowSoulCooldown =
                Mathf.Max(0f, _sharpShadowSoulCooldown - Time.deltaTime);

            TickGeoGenerationSynergy();
            CharmsEvolveApi.RaiseEffectTick(new CharmEffectTickContext(Time.deltaTime, Time.unscaledDeltaTime));
        }

        public void ModifyOutgoingHit(ref object hitInstance)
        {
            int damage;
            string attackType;
            if (!GameReflection.TryReadDamage(hitInstance, out damage, out attackType))
                return;

            float multiplier = 1f;
            string normalized = attackType == null ? string.Empty : attackType.ToLowerInvariant();

            if (normalized.Contains("nail") || normalized.Contains("slash"))
                multiplier = _modifiers.Snapshot.NailDamageMultiplier;
            else if (normalized.Contains("spell") ||
                     normalized.Contains("fireball") ||
                     normalized.Contains("quake") ||
                     normalized.Contains("scream"))
                multiplier = _modifiers.Snapshot.SpellDamageMultiplier;

            GameplayDamageContext context =
                new GameplayDamageContext(damage, attackType, multiplier);
            Api.CharmsEvolveApi.RaiseBeforeOutgoingDamage(context);

            int adjusted = Math.Max(0, (int)Math.Round(context.BaseDamage * context.Multiplier));
            GameReflection.TryWriteDamage(ref hitInstance, adjusted);

            TryApplySharpShadowSoul(normalized);
        }

        public int ModifyIncomingDamage(int damage)
        {
            if (damage <= 0)
                return damage;

            // 表格：坚硬外壳 + 稳定之体 + 虚空之心，将 2 点伤害压为 1 点。
            if (damage >= 2 &&
                _state.IsOriginalOrCopyEquipped(4) &&
                _state.IsOriginalOrCopyEquipped(14) &&
                _state.IsOriginalOrCopyEquipped(36) &&
                GameReflection.IsVoidHeartForm())
                return 1;

            return damage;
        }

        public void OnHeroDamaged(int damage)
        {
            if (damage <= 0 || _addingSoul)
                return;

            int soul = _modifiers.Snapshot.SoulOnDamage;
            HeroDamagedContext context = new HeroDamagedContext(damage, soul);
            Api.CharmsEvolveApi.RaiseHeroDamaged(context);

            if (context.SoulToGrant <= 0)
                return;

            try
            {
                _addingSoul = true;
                GameReflection.AddSoul(context.SoulToGrant);
            }
            finally
            {
                _addingSoul = false;
            }
        }

        public int ModifyGeo(int amount)
        {
            GeoGainContext context =
                new GeoGainContext(amount, _modifiers.Snapshot.GeoMultiplier);
            Api.CharmsEvolveApi.RaiseBeforeGeoGain(context);
            return Math.Max(0, (int)Math.Round(context.BaseAmount * context.Multiplier));
        }

        private void TickGeoGenerationSynergy()
        {
            // 表格：聚集蜂群 + 易碎贪婪，每 10 秒生成 1~25 Geo；
            // 再加入任性的指南针后，周期缩短至 8 秒。
            bool active =
                _state.IsOriginalOrCopyEquipped(1) &&
                _state.IsOriginalOrCopyEquipped(24);

            if (!active)
            {
                _geoTimer = 0f;
                return;
            }

            float interval = _state.IsOriginalOrCopyEquipped(2) ? 8f : 10f;
            _geoTimer += Time.deltaTime;
            if (_geoTimer < interval)
                return;

            _geoTimer -= interval;
            int amount = UnityEngine.Random.Range(1, 26);

            if (_state.IsOriginalOrCopyEquipped(10))
                amount = Mathf.CeilToInt(amount * 1.5f);

            GameReflection.AddGeo(amount);
        }

        private void TryApplySharpShadowSoul(string normalizedAttackType)
        {
            if (_sharpShadowSoulCooldown > 0f ||
                string.IsNullOrEmpty(normalizedAttackType) ||
                (!normalizedAttackType.Contains("shadow") &&
                 !normalizedAttackType.Contains("dash")))
                return;

            if (!_state.IsOriginalOrCopyEquipped(16) ||
                !_state.IsOriginalOrCopyEquipped(36) ||
                !GameReflection.IsVoidHeartForm())
                return;

            int soul = 0;
            if (_state.IsOriginalOrCopyEquipped(21))
                soul = 20;
            else if (_state.IsOriginalOrCopyEquipped(20))
                soul = 8;

            if (soul <= 0)
                return;

            _sharpShadowSoulCooldown = 0.12f;
            GameReflection.AddSoul(soul);
        }
    }

    public sealed class GameplayDamageContext
    {
        public int BaseDamage;
        public string AttackType;
        public float Multiplier;

        public GameplayDamageContext(int baseDamage, string attackType, float multiplier)
        {
            BaseDamage = baseDamage;
            AttackType = attackType ?? string.Empty;
            Multiplier = multiplier;
        }
    }

    public sealed class HeroDamagedContext
    {
        public int Damage;
        public int SoulToGrant;

        public HeroDamagedContext(int damage, int soulToGrant)
        {
            Damage = damage;
            SoulToGrant = soulToGrant;
        }
    }

    public sealed class GeoGainContext
    {
        public int BaseAmount;
        public float Multiplier;

        public GeoGainContext(int baseAmount, float multiplier)
        {
            BaseAmount = baseAmount;
            Multiplier = multiplier;
        }
    }
}
