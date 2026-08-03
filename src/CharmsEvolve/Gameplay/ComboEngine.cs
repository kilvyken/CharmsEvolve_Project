using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CharmsEvolve.Data;
using CharmsEvolve.Api;

namespace CharmsEvolve.Gameplay
{
    internal sealed class ComboEngine
    {
        private static readonly Regex NumberRegex =
            new Regex(@"(?<!\d)([1-9]|[1-3]\d|40)(?!\d)", RegexOptions.Compiled);

        private readonly CharmStateService _state;
        private readonly Dictionary<string, int> _nameToId =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ActiveSynergy> _cache = new List<ActiveSynergy>();
        private bool _dirty = true;

        public ComboEngine(CharmStateService state)
        {
            _state = state;
            _state.EquipmentChanged += MarkDirty;

            for (int id = 1; id <= 40; id++)
            {
                BaseCharmDefinition definition = CharmDatabase.GetBase(id);
                if (!string.IsNullOrEmpty(definition.NameZh))
                    _nameToId[definition.NameZh] = id;
                if (!string.IsNullOrEmpty(definition.NameEn))
                    _nameToId[definition.NameEn] = id;
            }

            _nameToId["国王之魂"] = 36;
            _nameToId["Kingsoul"] = 36;
            _nameToId["虚空之心"] = 36;
            _nameToId["Void Heart"] = 36;
            _nameToId["格林之子"] = 40;
            _nameToId["Grimmchild"] = 40;
            _nameToId["无忧旋律"] = 40;
            _nameToId["Carefree Melody"] = 40;
        }

        public IList<ActiveSynergy> GetActiveSynergies()
        {
            RebuildIfDirty();
            return _cache.AsReadOnly();
        }

        public IList<ActiveSynergy> GetActiveSynergiesFor(string sourceKey)
        {
            RebuildIfDirty();
            List<ActiveSynergy> result = new List<ActiveSynergy>();
            for (int i = 0; i < _cache.Count; i++)
            {
                if (string.Equals(_cache[i].SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                    result.Add(_cache[i]);
            }
            return result;
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        private void RebuildIfDirty()
        {
            if (!_dirty)
                return;

            _cache.Clear();

            foreach (string key in _state.EquippedKeys)
            {
                CopyCharmDefinition definition = CharmDatabase.GetCopy(key);
                if (definition == null)
                    continue;

                AddIfActive(definition, definition.VanillaSynergy);
                AddIfActive(definition, definition.EnhancedSynergy);
                AddIfActive(definition, definition.LegacyEnhancement);
                AddIfActive(definition, definition.VoidKnight);

                if (definition.StackableSynergies != null)
                {
                    for (int i = 0; i < definition.StackableSynergies.Length; i++)
                        AddIfActive(definition, definition.StackableSynergies[i]);
                }
            }

            _dirty = false;
        }

        private void AddIfActive(CopyCharmDefinition source, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            HashSet<int> references = ExtractReferences(text);
            references.Remove(source.OriginalId);

            bool active = true;
            foreach (int id in references)
            {
                if (!_state.IsOriginalOrCopyEquipped(id))
                {
                    active = false;
                    break;
                }
            }

            int[] ids = new int[references.Count];
            references.CopyTo(ids);
            Array.Sort(ids);

            SynergyEvaluationContext context = new SynergyEvaluationContext(
                source.Key,
                text,
                ids,
                active);
            CharmsEvolveApi.RaiseEvaluateSynergy(context);
            if (!context.Active)
                return;

            ActiveSynergy synergy = new ActiveSynergy();
            synergy.SourceKey = source.Key;
            synergy.Description = context.Description;
            synergy.ReferencedOriginalIds = context.ReferencedOriginalIds ?? ids;
            _cache.Add(synergy);
        }

        private HashSet<int> ExtractReferences(string text)
        {
            HashSet<int> result = new HashSet<int>();

            MatchCollection matches = NumberRegex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                int id;
                if (int.TryParse(matches[i].Groups[1].Value, out id))
                    result.Add(id);
            }

            foreach (KeyValuePair<string, int> pair in _nameToId)
            {
                if (text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(pair.Value);
            }

            return result;
        }
    }
}
