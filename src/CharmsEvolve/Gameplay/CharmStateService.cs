using System;
using System.Collections.Generic;
using CharmsEvolve.Data;
using CharmsEvolve.Interop;
using CharmsEvolve.Save;
using CharmsEvolve.Api;

namespace CharmsEvolve.Gameplay
{
    internal sealed class CharmStateService
    {
        private readonly Plugin _plugin;
        private readonly SaveRepository _repository;
        private readonly HashSet<string> _owned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _equipped =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _slot = -1;
        private bool _saveDirty;
        private float _saveDelay;
        private float _notchSyncDelay;

        public event Action EquipmentChanged;
        public event Action OwnershipChanged;

        public CharmStateService(Plugin plugin, SaveRepository repository)
        {
            _plugin = plugin;
            _repository = repository;
        }

        public int CurrentSlot
        {
            get { return _slot; }
        }

        public IEnumerable<string> EquippedKeys
        {
            get { return _equipped; }
        }

        public void LoadSlot(int slot)
        {
            FlushSave();

            _slot = slot;
            _owned.Clear();
            _equipped.Clear();

            if (slot >= 0)
            {
                CharmSaveData data = _repository.Load(slot);
                if (data.Owned != null)
                {
                    for (int i = 0; i < data.Owned.Count; i++)
                        if (CharmDatabase.GetCopy(data.Owned[i]) != null)
                            _owned.Add(data.Owned[i]);
                }

                if (_plugin.UnlockAllCopies.Value && _owned.Count == 0)
                {
                    for (int i = 0; i < CharmDatabase.AllCopies.Count; i++)
                        _owned.Add(CharmDatabase.AllCopies[i].Key);
                    _saveDirty = true;
                }

                if (data.Equipped != null)
                {
                    for (int i = 0; i < data.Equipped.Count; i++)
                    {
                        string key = data.Equipped[i];
                        if (_owned.Contains(key) && CharmDatabase.GetCopy(key) != null)
                            _equipped.Add(key);
                    }
                }
            }

            _notchSyncDelay = 0f;
            RaiseEquipmentChanged();
            if (OwnershipChanged != null)
                OwnershipChanged();
        }

        public void Tick()
        {
            if (_saveDirty)
            {
                _saveDelay -= UnityEngine.Time.unscaledDeltaTime;
                if (_saveDelay <= 0f)
                    FlushSave();
            }

            _notchSyncDelay -= UnityEngine.Time.unscaledDeltaTime;
            if (_notchSyncDelay <= 0f)
            {
                _notchSyncDelay = 0.1f;
                SyncNotchUsage();
            }
        }

        public bool IsOwned(string key)
        {
            return key != null && _owned.Contains(key);
        }

        public bool IsEquipped(string key)
        {
            return key != null && _equipped.Contains(key);
        }

        public int GetCopyCount(int originalId)
        {
            int count = 0;
            for (int kind = 0; kind < 3; kind++)
            {
                if (_equipped.Contains(CharmKey.For(originalId, (CopyKind)kind)))
                    count++;
            }
            return count;
        }

        public int GetTotalStackCount(int originalId)
        {
            return GetCopyCount(originalId) +
                   (GameReflection.IsVanillaCharmEquipped(originalId) ? 1 : 0);
        }

        public bool IsOriginalOrCopyEquipped(int originalId)
        {
            return GetTotalStackCount(originalId) > 0;
        }

        public int GetCustomNotchCost()
        {
            int total = 0;
            foreach (string key in _equipped)
            {
                CopyCharmDefinition definition = CharmDatabase.GetCopy(key);
                if (definition != null)
                    total += CharmsEvolveApi.ResolveCharmCost(definition);
            }
            return total;
        }

        public int GetTotalUsedNotches()
        {
            return GameReflection.GetVanillaEquippedCost() + GetCustomNotchCost();
        }

        public bool CanEditEquipment()
        {
            return _plugin.AllowEquipAnywhere.Value || GameReflection.IsAtBench();
        }

        public bool CanEquip(CopyCharmDefinition definition, out string reason)
        {
            reason = string.Empty;
            if (definition == null)
            {
                reason = "护符数据不存在。";
                return false;
            }

            if (!_owned.Contains(definition.Key))
            {
                reason = "尚未获得该复制护符。";
                return false;
            }

            if (!CanEditEquipment())
            {
                reason = "需要在长椅处装备护符。";
                return false;
            }

            if (_equipped.Contains(definition.Key))
                return true;

            int newCost = GetTotalUsedNotches() + CharmsEvolveApi.ResolveCharmCost(definition);
            int slots = GameReflection.GetCharmSlots();
            if (newCost <= slots)
                return true;

            if (_plugin.AllowCustomOvercharm.Value)
                return true;

            reason = "护符槽不足。";
            return false;
        }

        public bool Toggle(CopyCharmDefinition definition, out string reason)
        {
            reason = string.Empty;
            if (definition == null)
            {
                reason = "护符数据不存在。";
                return false;
            }

            if (_equipped.Contains(definition.Key))
            {
                if (!CanEditEquipment())
                {
                    reason = "需要在长椅处卸下护符。";
                    return false;
                }

                _equipped.Remove(definition.Key);
                MarkChanged();
                Plugin.Log.LogInfo("Unequipped copy charm " + definition.Key + "; custom notch total=" + GetCustomNotchCost() + ".");
                return true;
            }

            if (!CanEquip(definition, out reason))
                return false;

            _equipped.Add(definition.Key);
            MarkChanged();
            Plugin.Log.LogInfo("Equipped copy charm " + definition.Key + "; resolved cost=" +
                CharmsEvolveApi.ResolveCharmCost(definition) + ", custom notch total=" + GetCustomNotchCost() + ".");
            return true;
        }

        public bool SetOwned(string key, bool owned)
        {
            if (CharmDatabase.GetCopy(key) == null)
                return false;

            bool changed;
            if (owned)
            {
                changed = _owned.Add(key);
            }
            else
            {
                changed = _owned.Remove(key);
                changed |= _equipped.Remove(key);
            }

            if (!changed)
                return false;

            _saveDirty = true;
            _saveDelay = 0.25f;
            if (OwnershipChanged != null)
                OwnershipChanged();
            RaiseEquipmentChanged();
            return true;
        }

        public bool SetEquipped(string key, bool equipped, out string reason)
        {
            CopyCharmDefinition definition = CharmDatabase.GetCopy(key);
            if (definition == null)
            {
                reason = "护符数据不存在。";
                return false;
            }

            if (equipped == IsEquipped(key))
            {
                reason = string.Empty;
                return true;
            }

            return Toggle(definition, out reason);
        }

        public void SyncNotchUsage()
        {
            if (_slot < 0 || !GameReflection.HasPlayerData())
                return;

            int total = GetTotalUsedNotches();
            GameReflection.SetPlayerIntDirect("charmSlotsFilled", total);
            GameReflection.SetPlayerBoolDirect(
                "overcharmed",
                total > GameReflection.GetCharmSlots());
        }

        public void FlushSave()
        {
            if (!_saveDirty || _slot < 0)
                return;

            CharmSaveData data = new CharmSaveData();
            data.Owned.AddRange(_owned);
            data.Equipped.AddRange(_equipped);
            data.Owned.Sort(StringComparer.OrdinalIgnoreCase);
            data.Equipped.Sort(StringComparer.OrdinalIgnoreCase);
            _repository.Save(_slot, data);
            _saveDirty = false;
        }

        private void MarkChanged()
        {
            _saveDirty = true;
            _saveDelay = 0.25f;
            _notchSyncDelay = 0f;
            RaiseEquipmentChanged();
        }

        private void RaiseEquipmentChanged()
        {
            if (EquipmentChanged != null)
                EquipmentChanged();
        }
    }
}
