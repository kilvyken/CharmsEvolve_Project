using System;
using CharmsEvolve.Interop;

namespace CharmsEvolve.Gameplay
{
    public sealed class RuntimeModifierSnapshot
    {
        public float NailDamageMultiplier = 1f;
        public float SpellDamageMultiplier = 1f;
        public float GeoMultiplier = 1f;
        public float MoveSpeedMultiplier = 1f;
        public float DashCooldownMultiplier = 1f;
        public float AttackCooldownMultiplier = 1f;
        public float FocusTimeMultiplier = 1f;
        public float NailChargeTimeMultiplier = 1f;
        public float NailRangeMultiplier = 1f;
        public int SpellCostDelta;
        public int BonusMaxHealth;
        public int BonusBlueHealth;
        public int SoulOnDamage;
        public int BaldurShellExtraBlocks;
    }

    internal sealed class RuntimeModifierService
    {
        private readonly CharmStateService _state;
        private bool _dirty = true;
        private RuntimeModifierSnapshot _snapshot = new RuntimeModifierSnapshot();
        private int _lastHealth = int.MinValue;
        private bool _lastFuryAlways;

        public event Action<RuntimeModifierSnapshot> Rebuilt;

        public RuntimeModifierService(CharmStateService state)
        {
            _state = state;
            _state.EquipmentChanged += MarkDirty;
        }

        public RuntimeModifierSnapshot Snapshot
        {
            get
            {
                RebuildIfDirty();
                return _snapshot;
            }
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void RebuildIfDirty()
        {
            int currentHealth = GameReflection.GetCurrentHealth();
            bool furyAlways = _state.IsOriginalOrCopyEquipped(27);
            if (currentHealth != _lastHealth || furyAlways != _lastFuryAlways)
            {
                _lastHealth = currentHealth;
                _lastFuryAlways = furyAlways;
                _dirty = true;
            }

            if (!_dirty)
                return;

            RuntimeModifierSnapshot value = new RuntimeModifierSnapshot();

            int furyCopies = _state.GetCopyCount(6);
            int strengthCopies = _state.GetCopyCount(25);
            int shamanCopies = _state.GetCopyCount(19);
            int greedCopies = _state.GetCopyCount(24);

            bool vanillaStrength = GameReflection.IsVanillaCharmEquipped(25);
            float desiredStrength =
                1f + 0.5f * (strengthCopies + (vanillaStrength ? 1 : 0));
            float vanillaStrengthFactor = vanillaStrength ? 1.5f : 1f;
            float strengthExtra = desiredStrength / vanillaStrengthFactor;

            bool vanillaFury = GameReflection.IsVanillaCharmEquipped(6);
            bool furyActive = currentHealth <= 1 || furyAlways;
            int furyStacks = furyActive
                ? furyCopies + (vanillaFury ? 1 : 0)
                : 0;
            float desiredFury = 1f + 0.75f * furyStacks;
            float vanillaFuryFactor =
                vanillaFury && currentHealth <= 1 ? 1.75f : 1f;
            float furyExtra = desiredFury / vanillaFuryFactor;

            value.NailDamageMultiplier = strengthExtra * furyExtra;

            bool vanillaShaman = GameReflection.IsVanillaCharmEquipped(19);
            float desiredShaman =
                1f + 0.33f * (shamanCopies + (vanillaShaman ? 1 : 0));
            float vanillaShamanFactor = vanillaShaman ? 1.33f : 1f;
            value.SpellDamageMultiplier = desiredShaman / vanillaShamanFactor;

            value.GeoMultiplier = 1f + 0.20f * greedCopies;

            value.MoveSpeedMultiplier = Pow(1.20f, _state.GetCopyCount(37));
            value.DashCooldownMultiplier = Pow(0.67f, _state.GetCopyCount(31));
            value.AttackCooldownMultiplier = Pow(0.61f, _state.GetCopyCount(32));
            value.FocusTimeMultiplier = Pow(0.67f, _state.GetCopyCount(7));
            value.NailChargeTimeMultiplier = Pow(0.56f, _state.GetCopyCount(26));
            value.NailRangeMultiplier =
                Pow(1.15f, _state.GetCopyCount(18)) *
                Pow(1.25f, _state.GetCopyCount(13));

            value.SpellCostDelta = -9 * _state.GetCopyCount(33);
            value.BonusMaxHealth = 2 * _state.GetCopyCount(23);
            value.BonusBlueHealth =
                2 * _state.GetCopyCount(8) +
                4 * _state.GetCopyCount(9);
            value.SoulOnDamage = 15 * _state.GetCopyCount(3);
            value.BaldurShellExtraBlocks = 4 * _state.GetCopyCount(5);

            _snapshot = value;
            _dirty = false;

            if (Rebuilt != null)
                Rebuilt(_snapshot);
        }

        private static float Pow(float value, int count)
        {
            float result = 1f;
            for (int i = 0; i < count; i++)
                result *= value;
            return result;
        }
    }
}
