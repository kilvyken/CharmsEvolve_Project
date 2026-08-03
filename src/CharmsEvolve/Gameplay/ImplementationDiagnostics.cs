using System;
using System.Collections.Generic;
using CharmsEvolve.Data;

namespace CharmsEvolve.Gameplay
{
    internal static class ImplementationDiagnostics
    {
        private static readonly int[] LiveBaseEffectIds = { 3, 6, 19, 24, 25 };
        private static readonly int[] LiveSpecialSynergyIds = { 1, 2, 4, 10, 14, 16, 20, 21, 24, 36 };
        private static readonly int[] ComputedButNotAppliedIds = { 5, 7, 8, 9, 13, 18, 23, 26, 31, 32, 33, 37 };

        public static void LogStartupAudit()
        {
            Plugin.Log.LogInfo(
                "Effect audit: " + CharmDatabase.AllCopies.Count +
                " copy definitions loaded from 42 source forms mapped onto 40 physical charm slots.");

            Plugin.Log.LogInfo(
                "Live generic copy-base effects currently patched for original ids: " +
                Join(LiveBaseEffectIds) + ".");

            Plugin.Log.LogInfo(
                "Live hand-written special synergies currently patched around original ids: " +
                Join(LiveSpecialSynergyIds) + ".");

            Plugin.Log.LogWarning(
                "Modifier values are calculated but not yet connected to a game method for original ids: " +
                Join(ComputedButNotAppliedIds) +
                ". Their UI text may exist while the gameplay effect is not active.");

            List<int> unimplemented = new List<int>();
            for (int id = 1; id <= 42; id++)
            {
                if (!Contains(LiveBaseEffectIds, id) &&
                    !Contains(LiveSpecialSynergyIds, id) &&
                    !Contains(ComputedButNotAppliedIds, id))
                    unimplemented.Add(id);
            }

            Plugin.Log.LogWarning(
                "No built-in runtime implementation is registered yet for original ids: " +
                Join(unimplemented.ToArray()) +
                ". Use CharmsEvolveApi runtime events or add a Harmony module before acceptance testing these effects.");

            Plugin.Log.LogWarning(
                "Vanilla/copy equivalence is currently guaranteed only inside CharmsEvolve effect code and API queries. " +
                "The game's own arbitrary PlayerData charm checks are not globally proxied, so unpatched vanilla FSM synergies may not recognize a copy charm.");
        }

        private static bool Contains(int[] values, int target)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] == target)
                    return true;
            return false;
        }

        private static string Join(int[] values)
        {
            string[] text = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                text[i] = values[i].ToString();
            return string.Join(",", text);
        }
    }
}
