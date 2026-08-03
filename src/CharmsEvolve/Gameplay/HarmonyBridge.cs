using System;
using System.Reflection;
using HarmonyLib;

namespace CharmsEvolve.Gameplay
{
    internal sealed class HarmonyBridge
    {
        private static HarmonyBridge _active;

        private readonly EffectRuntime _effects;
        private readonly CharmStateService _state;

        public HarmonyBridge(EffectRuntime effects, CharmStateService state)
        {
            _effects = effects;
            _state = state;
        }

        public void Install(Harmony harmony, bool enableGameplayPatches)
        {
            _active = this;

            PatchNotchRecalculation(harmony);

            if (!enableGameplayPatches)
            {
                Plugin.Log.LogInfo("Experimental gameplay patches disabled.");
                return;
            }

            PatchOutgoingDamage(harmony);
            PatchHeroDamage(harmony);
            PatchGeoGain(harmony);
        }

        private static void PatchOutgoingDamage(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("HealthManager");
            if (type == null)
            {
                Plugin.Log.LogError("Gameplay patch missing type: HealthManager. Outgoing copy-charm damage effects are disabled.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonyBridge), "OutgoingDamagePrefix");
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            int count = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, "TakeDamage", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length < 1 ||
                    parameters[0].ParameterType.Name.IndexOf("HitInstance", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                harmony.Patch(methods[i], prefix: new HarmonyMethod(prefix));
                count++;
            }

            if (count == 0)
                Plugin.Log.LogError("No HealthManager.TakeDamage(HitInstance) overload was patched; outgoing damage effects are disabled.");
            else
                Plugin.Log.LogInfo("Patched outgoing damage overloads: " + count);
        }

        private static void PatchHeroDamage(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("HeroController");
            if (type == null)
            {
                Plugin.Log.LogError("Gameplay patch missing type: HeroController. Incoming damage and on-damage effects are disabled.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonyBridge), "HeroDamagePrefix");
            MethodInfo postfix = AccessTools.Method(typeof(HarmonyBridge), "HeroDamagePostfix");
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            int count = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, "TakeHealth", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    harmony.Patch(
                        methods[i],
                        prefix: new HarmonyMethod(prefix),
                        postfix: new HarmonyMethod(postfix));
                    count++;
                }
            }

            if (count == 0)
                Plugin.Log.LogError("No HeroController.TakeHealth(int) overload was patched; incoming damage and on-damage effects are disabled.");
            else
                Plugin.Log.LogInfo("Patched hero damage overloads: " + count);
        }

        private static void PatchGeoGain(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("HeroController");
            if (type == null)
            {
                Plugin.Log.LogError("Gameplay patch missing type: HeroController. Geo effects are disabled.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(HarmonyBridge), "GeoGainPrefix");
            string[] names = { "AddGeo", "GiveGeo" };
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            int count = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                bool nameMatch = false;
                for (int n = 0; n < names.Length; n++)
                    nameMatch |= string.Equals(methods[i].Name, names[n], StringComparison.Ordinal);
                if (!nameMatch)
                    continue;

                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                {
                    harmony.Patch(methods[i], prefix: new HarmonyMethod(prefix));
                    count++;
                }
            }

            if (count == 0)
                Plugin.Log.LogError("No HeroController AddGeo/GiveGeo overload was patched; Geo effects are disabled.");
            else
                Plugin.Log.LogInfo("Patched geo gain overloads: " + count);
        }

        private static void PatchNotchRecalculation(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("PlayerData");
            if (type == null)
            {
                Plugin.Log.LogError("Gameplay patch missing type: PlayerData. Custom notch synchronization is disabled.");
                return;
            }

            MethodInfo postfix = AccessTools.Method(typeof(HarmonyBridge), "NotchPostfix");
            string[] names =
            {
                "CountCharmSlots",
                "UpdateCharmSlots",
                "CalculateNotchesUsed",
                "UpdateCharms"
            };

            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            int count = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                for (int n = 0; n < names.Length; n++)
                {
                    if (!string.Equals(methods[i].Name, names[n], StringComparison.Ordinal))
                        continue;
                    harmony.Patch(methods[i], postfix: new HarmonyMethod(postfix));
                    count++;
                    break;
                }
            }

            if (count == 0)
                Plugin.Log.LogError("No PlayerData notch recalculation method was patched; custom notch synchronization may drift.");
            else
                Plugin.Log.LogInfo("Patched notch recalculation methods: " + count);
        }

        private static void OutgoingDamagePrefix(object[] __args)
        {
            if (_active == null || __args == null || __args.Length == 0)
                return;

            object hit = __args[0];
            _active._effects.ModifyOutgoingHit(ref hit);
            __args[0] = hit;
        }

        private static void HeroDamagePrefix(object[] __args)
        {
            if (_active == null || __args == null || __args.Length == 0)
                return;

            int damage;
            try
            {
                damage = Convert.ToInt32(__args[0]);
            }
            catch
            {
                return;
            }

            __args[0] = _active._effects.ModifyIncomingDamage(damage);
        }

        private static void HeroDamagePostfix(object[] __args)
        {
            if (_active == null || __args == null || __args.Length == 0)
                return;

            int damage;
            try
            {
                damage = Convert.ToInt32(__args[0]);
            }
            catch
            {
                return;
            }

            _active._effects.OnHeroDamaged(damage);
        }

        private static void GeoGainPrefix(object[] __args)
        {
            if (_active == null || __args == null || __args.Length == 0)
                return;

            int amount;
            try
            {
                amount = Convert.ToInt32(__args[0]);
            }
            catch
            {
                return;
            }

            __args[0] = _active._effects.ModifyGeo(amount);
        }

        private static void NotchPostfix()
        {
            if (_active != null)
                _active._state.SyncNotchUsage();
        }
    }
}
