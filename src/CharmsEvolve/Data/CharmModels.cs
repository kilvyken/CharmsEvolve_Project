using System;
using System.Collections.Generic;

namespace CharmsEvolve.Data
{
    public enum CopyKind
    {
        FragileReplica = 0,
        PaleKingReplica = 1,
        DreamImprint = 2
    }

    [Serializable]
    public sealed class BaseCharmDefinition
    {
        public int OriginalId;
        public string NameZh;
        public string NameEn;
        public string BaseEffect;
        public string VanillaSynergy;
        public int VanillaCost;
        public int CopyCost;
        public string EnhancedSynergy;
        public string VoidKnight;
        public string LegacyEnhancement;
        public string CopyX;
        public string CopyY;
        public string CopyZ;
        public string[] StackableSynergies;

        public BaseCharmDefinition(
            int originalId,
            string nameZh,
            string nameEn,
            string baseEffect,
            string vanillaSynergy,
            int vanillaCost,
            int copyCost,
            string enhancedSynergy,
            string voidKnight,
            string legacyEnhancement,
            string copyX,
            string copyY,
            string copyZ,
            string[] stackableSynergies)
        {
            OriginalId = originalId;
            NameZh = nameZh ?? string.Empty;
            NameEn = nameEn ?? string.Empty;
            BaseEffect = baseEffect ?? string.Empty;
            VanillaSynergy = vanillaSynergy ?? string.Empty;
            VanillaCost = vanillaCost;
            CopyCost = copyCost;
            EnhancedSynergy = enhancedSynergy ?? string.Empty;
            VoidKnight = voidKnight ?? string.Empty;
            LegacyEnhancement = legacyEnhancement ?? string.Empty;
            CopyX = copyX ?? string.Empty;
            CopyY = copyY ?? string.Empty;
            CopyZ = copyZ ?? string.Empty;
            StackableSynergies = stackableSynergies ?? new string[0];
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(NameEn)) return NameZh;
                return NameZh + " " + NameEn;
            }
        }
    }

    [Serializable]
    public sealed class CopyCharmDefinition
    {
        public string Key;
        public int RuntimeId;
        public int OriginalId;
        public CopyKind Kind;
        public int Cost;
        public string NameZh;
        public string NameEn;
        public string Description;
        public string SourceEffect;
        public string VanillaSynergy;
        public string EnhancedSynergy;
        public string VoidKnight;
        public string LegacyEnhancement;
        public string[] StackableSynergies;

        public string DisplayName
        {
            get
            {
                string suffix;
                switch (Kind)
                {
                    case CopyKind.FragileReplica: suffix = "·易碎仿制品"; break;
                    case CopyKind.PaleKingReplica: suffix = "·白王复制品"; break;
                    default: suffix = "·梦境印刻物"; break;
                }

                return NameZh + suffix;
            }
        }

        public string EnglishDisplayName
        {
            get
            {
                string suffix;
                switch (Kind)
                {
                    case CopyKind.FragileReplica: suffix = " - Fragile Replica"; break;
                    case CopyKind.PaleKingReplica: suffix = " - Pale King Replica"; break;
                    default: suffix = " - Dream Imprint"; break;
                }

                return (NameEn ?? string.Empty) + suffix;
            }
        }
    }

    public sealed class ActiveSynergy
    {
        public string SourceKey;
        public string Description;
        public int[] ReferencedOriginalIds;
    }

    public static class CharmKey
    {
        public static string For(int originalId, CopyKind kind)
        {
            switch (kind)
            {
                case CopyKind.FragileReplica: return "X-" + originalId.ToString("00");
                case CopyKind.PaleKingReplica: return "Y-" + originalId.ToString("00");
                default: return "Z-" + originalId.ToString("00");
            }
        }

        public static int RuntimeId(int originalId, CopyKind kind)
        {
            return 1000 + ((int)kind * 100) + originalId;
        }
    }
}
