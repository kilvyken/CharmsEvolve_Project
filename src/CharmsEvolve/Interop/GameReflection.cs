using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CharmsEvolve.Interop
{
    internal static class GameReflection
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type _playerDataType;
        private static Type _gameManagerType;
        private static Type _heroControllerType;
        private static Type _inventoryManagerType;
        private static bool _inventoryManagerTypeResolved;

        private static UnityEngine.Object _inventoryObject;
        private static float _inventoryProbeAt;

        public static bool HasPlayerData()
        {
            return GetPlayerData() != null;
        }

        public static object GetPlayerData()
        {
            if (_playerDataType == null)
                _playerDataType = AccessTools.TypeByName("PlayerData");
            return GetSingleton(_playerDataType);
        }

        public static object GetGameManager()
        {
            if (_gameManagerType == null)
                _gameManagerType = AccessTools.TypeByName("GameManager");
            return GetSingleton(_gameManagerType);
        }

        public static object GetHeroController()
        {
            if (_heroControllerType == null)
                _heroControllerType = AccessTools.TypeByName("HeroController");
            return GetSingleton(_heroControllerType);
        }

        public static int GetCurrentSaveSlot()
        {
            object gm = GetGameManager();
            if (gm == null)
                return -1;

            object value;
            if (TryGetMember(gm, "profileID", out value) ||
                TryGetMember(gm, "profileId", out value) ||
                TryGetMember(gm, "saveSlot", out value) ||
                TryGetMember(gm, "currentProfile", out value))
            {
                return ConvertToInt(value, -1);
            }

            return -1;
        }

        public static bool GetPlayerBool(string name, bool fallback)
        {
            object pd = GetPlayerData();
            if (pd == null)
                return fallback;

            try
            {
                MethodInfo method = pd.GetType().GetMethod(
                    "GetBool",
                    AnyInstance,
                    null,
                    new[] { typeof(string) },
                    null);
                if (method != null)
                    return Convert.ToBoolean(method.Invoke(pd, new object[] { name }));
            }
            catch
            {
                // fall back to direct field access
            }

            object value;
            return TryGetMember(pd, name, out value) ? ConvertToBool(value, fallback) : fallback;
        }

        public static int GetPlayerInt(string name, int fallback)
        {
            object pd = GetPlayerData();
            if (pd == null)
                return fallback;

            try
            {
                MethodInfo method = pd.GetType().GetMethod(
                    "GetInt",
                    AnyInstance,
                    null,
                    new[] { typeof(string) },
                    null);
                if (method != null)
                    return ConvertToInt(method.Invoke(pd, new object[] { name }), fallback);
            }
            catch
            {
                // fall back to direct field access
            }

            object value;
            return TryGetMember(pd, name, out value) ? ConvertToInt(value, fallback) : fallback;
        }

        public static void SetPlayerIntDirect(string name, int value)
        {
            object pd = GetPlayerData();
            if (pd != null)
                TrySetMember(pd, name, value);
        }

        public static void SetPlayerBoolDirect(string name, bool value)
        {
            object pd = GetPlayerData();
            if (pd != null)
                TrySetMember(pd, name, value);
        }

        public static bool IsVanillaCharmEquipped(int originalId)
        {
            return GetPlayerBool("equippedCharm_" + originalId, false);
        }

        public static int GetVanillaCharmCost(int originalId)
        {
            return Math.Max(0, GetPlayerInt("charmCost_" + originalId, 0));
        }

        public static int GetVanillaEquippedCost()
        {
            if (!HasPlayerData())
                return 0;

            int total = 0;
            for (int id = 1; id <= 40; id++)
            {
                if (IsVanillaCharmEquipped(id))
                    total += GetVanillaCharmCost(id);
            }

            return total;
        }

        public static int GetCharmSlots()
        {
            return Math.Max(0, GetPlayerInt("charmSlots", 0));
        }

        public static int GetCurrentHealth()
        {
            return GetPlayerInt("health", 0);
        }

        public static bool IsAtBench()
        {
            if (GetPlayerBool("atBench", false) ||
                GetPlayerBool("sittingOnBench", false) ||
                GetPlayerBool("isSitting", false))
                return true;

            object hero = GetHeroController();
            object value;
            if (hero != null &&
                (TryGetMember(hero, "atBench", out value) ||
                 TryGetMember(hero, "sittingOnBench", out value)))
                return ConvertToBool(value, false);

            return false;
        }

        public static bool IsVoidHeartForm()
        {
            int royalState = GetPlayerInt("royalCharmState", 0);
            return royalState >= 4 ||
                   GetPlayerBool("gotShadeCharm", false) ||
                   GetPlayerBool("gotCharm_36", false) && GetPlayerBool("equippedCharm_36", false) &&
                   royalState > 2;
        }

        public static bool IsCarefreeMelodyForm()
        {
            return GetPlayerBool("destroyedNightmareLantern", false) ||
                   GetPlayerBool("banishedGrimm", false) ||
                   GetPlayerBool("carefreeMelody", false);
        }

        public static bool IsInventoryActive()
        {
            if (Time.unscaledTime >= _inventoryProbeAt)
            {
                _inventoryProbeAt = Time.unscaledTime + 0.75f;
                _inventoryObject = FindActiveInventoryObject();
            }

            GameObject inventoryGameObject = GetGameObject(_inventoryObject);
            if (inventoryGameObject == null || !inventoryGameObject.activeInHierarchy)
                return false;

            Component component = _inventoryObject as Component;
            if (component != null)
            {
                object openValue;
                string[] openMembers =
                {
                    "isOpen",
                    "inventoryOpen",
                    "isInventoryOpen",
                    "open",
                    "showing",
                    "visible"
                };

                for (int i = 0; i < openMembers.Length; i++)
                {
                    if (TryGetMember(component, openMembers[i], out openValue) &&
                        openValue is bool)
                        return (bool)openValue;
                }
            }

            // Unity 6 builds may no longer expose the old InventoryManager type.
            // The fallback object is only selected when its active hierarchy looks like
            // an inventory/charm screen, so paused state is safe as the final open check.
            return IsGamePaused() || HasActiveCharmNamedChild(inventoryGameObject.transform, 0);
        }

        private static bool IsGamePaused()
        {
            object gm = GetGameManager();
            object value;
            if (gm != null && TryGetMember(gm, "gameState", out value) && value != null)
                return value.ToString().IndexOf("PAUSED", StringComparison.OrdinalIgnoreCase) >= 0;
            return false;
        }


        public static bool IsCharmPaneActive()
        {
            if (!IsInventoryActive())
                return false;

            Component component = _inventoryObject as Component;
            if (component != null)
            {
                string[] pageMembers =
                {
                    "currentPane",
                    "currentPage",
                    "selectedPane",
                    "inventoryPage",
                    "pane",
                    "page"
                };

                for (int i = 0; i < pageMembers.Length; i++)
                {
                    object value;
                    if (!TryGetMember(component, pageMembers[i], out value) || value == null)
                        continue;

                    string text = value.ToString();
                    if (text.IndexOf("CHARM", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    if (text.IndexOf("MAP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("JOURNAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("INVENTORY", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }

            GameObject inventoryGameObject = GetGameObject(_inventoryObject);
            Transform root = inventoryGameObject == null ? null : inventoryGameObject.transform;
            if (root != null &&
                (IsCharmObjectName(root.gameObject.name) || HasActiveCharmNamedChild(root, 0)))
                return true;

            // 部分游戏版本不暴露当前子页字段；如果已经找到了明确的
            // Inventory 对象，则保留旧行为，允许显示复制护符页。
            return inventoryGameObject != null &&
                   InventoryObjectNameScore(inventoryGameObject.name) > 0;
        }

        private static bool HasActiveCharmNamedChild(Transform root, int depth)
        {
            if (root == null || depth > 6)
                return false;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.gameObject == null ||
                    !child.gameObject.activeInHierarchy)
                    continue;

                string name = child.gameObject.name ?? string.Empty;
                if (IsCharmObjectName(name) &&
                    name.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;

                if (HasActiveCharmNamedChild(child, depth + 1))
                    return true;
            }

            return false;
        }

        public static bool AddSoul(int amount)
        {
            if (amount <= 0)
                return false;

            object hero = GetHeroController();
            if (hero == null)
                return false;

            string[] candidates = { "AddMPCharge", "AddMPChargeSpa", "SoulGain" };
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo method = FindSingleIntMethod(hero.GetType(), candidates[i]);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(hero, new object[] { amount });
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug("Soul method failed: " + ex.Message);
                }
            }

            return false;
        }


        public static bool AddGeo(int amount)
        {
            if (amount <= 0)
                return false;

            object hero = GetHeroController();
            if (hero == null)
                return false;

            string[] candidates = { "AddGeo", "GiveGeo" };
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo method = FindSingleIntMethod(hero.GetType(), candidates[i]);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(hero, new object[] { amount });
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug("Geo method failed: " + ex.Message);
                }
            }

            return false;
        }

        public static bool TryReadDamage(object hitInstance, out int damage, out string attackType)
        {
            damage = 0;
            attackType = string.Empty;
            if (hitInstance == null)
                return false;

            object damageValue;
            if (!(TryGetMember(hitInstance, "DamageDealt", out damageValue) ||
                  TryGetMember(hitInstance, "damageDealt", out damageValue) ||
                  TryGetMember(hitInstance, "Damage", out damageValue)))
                return false;

            damage = ConvertToInt(damageValue, 0);

            object attackValue;
            if (TryGetMember(hitInstance, "AttackType", out attackValue) ||
                TryGetMember(hitInstance, "attackType", out attackValue))
                attackType = attackValue == null ? string.Empty : attackValue.ToString();

            return true;
        }

        public static bool TryWriteDamage(ref object hitInstance, int damage)
        {
            if (hitInstance == null)
                return false;

            return TrySetMember(hitInstance, "DamageDealt", damage) ||
                   TrySetMember(hitInstance, "damageDealt", damage) ||
                   TrySetMember(hitInstance, "Damage", damage);
        }

        public static bool TryGetMember(object target, string name, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrEmpty(name))
                return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = instance == null ? AnyStatic : AnyInstance;

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                value = field.GetValue(instance);
                return true;
            }

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(instance, null);
                return true;
            }

            return false;
        }

        public static bool TrySetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
                return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = instance == null ? AnyStatic : AnyInstance;

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, ConvertValue(value, field.FieldType));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, ConvertValue(value, property.PropertyType), null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static object GetSingleton(Type type)
        {
            if (type == null)
                return null;

            object value;
            if (TryGetMember(type, "instance", out value) ||
                TryGetMember(type, "Instance", out value))
                return value;

            return null;
        }

        private static UnityEngine.Object FindActiveInventoryObject()
        {
            try
            {
                Type inventoryType = ResolveInventoryManagerTypeQuietly();
                if (inventoryType != null)
                {
                    UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(inventoryType);
                    for (int i = 0; i < objects.Length; i++)
                    {
                        GameObject gameObject = GetGameObject(objects[i]);
                        if (gameObject != null && gameObject.activeInHierarchy)
                            return objects[i];
                    }
                }

                // Newer Unity builds may rename/remove InventoryManager. Only perform the
                // wider scene-name scan while paused, keeping normal gameplay overhead low.
                if (IsGamePaused())
                    return FindActiveInventoryObjectBySceneName();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("Inventory probe failed: " + ex.Message);
            }

            return null;
        }

        private static Type ResolveInventoryManagerTypeQuietly()
        {
            if (_inventoryManagerTypeResolved)
                return _inventoryManagerType;

            _inventoryManagerTypeResolved = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            string[] exactNames =
            {
                "InventoryManager",
                "InventoryController",
                "InventoryScreen",
                "InventoryUI"
            };

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                    continue;

                for (int j = 0; j < exactNames.Length; j++)
                {
                    Type candidate = assembly.GetType(exactNames[j], false);
                    if (candidate != null && typeof(Component).IsAssignableFrom(candidate))
                    {
                        _inventoryManagerType = candidate;
                        return _inventoryManagerType;
                    }
                }
            }

            // Namespaces differ between game revisions, so perform one quiet suffix scan.
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types = GetLoadableTypes(assemblies[i]);
                for (int j = 0; j < types.Length; j++)
                {
                    Type candidate = types[j];
                    if (candidate == null || !typeof(Component).IsAssignableFrom(candidate))
                        continue;

                    string name = candidate.Name ?? string.Empty;
                    if (string.Equals(name, "InventoryManager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "InventoryController", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "InventoryScreen", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "InventoryUI", StringComparison.OrdinalIgnoreCase))
                    {
                        _inventoryManagerType = candidate;
                        return _inventoryManagerType;
                    }
                }
            }

            // Do not call Harmony AccessTools.TypeByName here: Harmony logs a warning for
            // every miss, which caused the repeated InventoryManager warning spam.
            return null;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
                return new Type[0];

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Type[] source = ex.Types;
                if (source == null)
                    return new Type[0];

                List<Type> result = new List<Type>();
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] != null)
                        result.Add(source[i]);
                }
                return result.ToArray();
            }
            catch
            {
                return new Type[0];
            }
        }

        private static UnityEngine.Object FindActiveInventoryObjectBySceneName()
        {
            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
            GameObject best = null;
            int bestScore = 0;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject gameObject = objects[i] as GameObject;
                if (gameObject == null || !gameObject.activeInHierarchy)
                    continue;

                int score = InventoryObjectNameScore(gameObject.name);
                if (IsCharmObjectName(gameObject.name))
                    score += 100;
                if (gameObject.transform != null && HasActiveCharmNamedChild(gameObject.transform, 0))
                    score += 80;

                if (score > bestScore)
                {
                    best = gameObject;
                    bestScore = score;
                }
            }

            return bestScore >= 50 ? best : null;
        }

        private static GameObject GetGameObject(UnityEngine.Object value)
        {
            Component component = value as Component;
            if (component != null)
                return component.gameObject;
            return value as GameObject;
        }

        private static int InventoryObjectNameScore(string value)
        {
            string name = value ?? string.Empty;
            int score = 0;

            if (name.Equals("Inventory", StringComparison.OrdinalIgnoreCase))
                score += 100;
            else if (name.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 60;

            if (name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Root", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 15;

            return score;
        }

        private static bool IsCharmObjectName(string value)
        {
            string name = value ?? string.Empty;
            return name.Equals("Charms", StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf("Charm Page", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Charms Pane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Charm Screen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static MethodInfo FindSingleIntMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(AnyInstance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, name, StringComparison.Ordinal))
                    continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                    return methods[i];
            }

            return null;
        }

        private static int ConvertToInt(object value, int fallback)
        {
            try
            {
                return value == null ? fallback : Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ConvertToBool(object value, bool fallback)
        {
            try
            {
                return value == null ? fallback : Convert.ToBoolean(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;
            if (targetType.IsInstanceOfType(value))
                return value;
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, Convert.ToInt32(value));
            return Convert.ChangeType(value, targetType);
        }
    }
}
