using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CharmsEvolve.Interop
{
    /// <summary>
    /// Reflection-only bridge to the live Hollow Knight charm inventory.
    /// Unity 5 used a PlayMaker FSM named "UI Charms" as the pane anchor.
    /// Unity 6 exposes CharmItem components under a live "Charms" container,
    /// so the component hierarchy is now the primary discovery path and the
    /// old FSM lookup remains as a compatibility fallback.
    /// </summary>
    internal static class CharmUtil
    {
        private static GameObject _charmsPane;
        private static GameObject _charmsGrid;
        private static Component _uiCharmsFsm;
        private static Type _charmItemType;
        private static bool _charmItemTypeResolved;
        private static float _nextProbeAt;

        public static GameObject CharmsPane
        {
            get
            {
                EnsureResolved();
                return _charmsPane;
            }
        }

        public static GameObject CharmsGrid
        {
            get
            {
                EnsureResolved();
                return _charmsGrid;
            }
        }

        public static Component UiCharmsFsm
        {
            get
            {
                EnsureResolved();
                return _uiCharmsFsm;
            }
        }

        public static Component[] FindAllCharmItems()
        {
            Type type = ResolveCharmItemTypeQuietly();
            if (type == null)
                return new Component[0];

            try
            {
                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
                List<Component> result = new List<Component>(objects.Length);
                for (int i = 0; i < objects.Length; i++)
                {
                    Component component = objects[i] as Component;
                    if (component != null && component.gameObject != null)
                        result.Add(component);
                }
                return result.ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("CharmItem resource scan failed: " + ex.Message);
                return new Component[0];
            }
        }

        public static bool TryGetCharmItemId(Component charmItem, out int id)
        {
            id = 0;
            if (charmItem == null || charmItem.gameObject == null)
                return false;

            if (int.TryParse(charmItem.gameObject.name, out id))
                return id > 0;

            string[] members =
            {
                "charmId", "charmID", "charmNum", "charmNumber", "id", "index", "Index"
            };
            for (int i = 0; i < members.Length; i++)
            {
                object value;
                if (!TryGetMember(charmItem, members[i], out value) || value == null)
                    continue;

                try
                {
                    id = Convert.ToInt32(value);
                    if (id > 0)
                        return true;
                }
                catch
                {
                    // Continue probing other known member names.
                }
            }

            id = 0;
            return false;
        }

        public static bool IsUnderCharmPane(GameObject gameObject)
        {
            if (gameObject == null)
                return false;

            EnsureResolved();
            return IsSameOrChild(gameObject, _charmsGrid) ||
                   IsSameOrChild(gameObject, _charmsPane);
        }

        public static bool IsUnderCharmGrid(GameObject gameObject)
        {
            if (gameObject == null)
                return false;

            EnsureResolved();
            return IsSameOrChild(gameObject, _charmsGrid);
        }

        public static GameObject GetOwnerGameObject(object candidate)
        {
            if (candidate == null)
                return null;

            Component component = candidate as Component;
            if (component != null)
                return component.gameObject;

            GameObject direct = candidate as GameObject;
            if (direct != null)
                return direct;

            object value;
            string[] members =
            {
                "GameObject", "OwnerGameObject", "gameObject", "Owner", "FsmComponent", "OwnerComponent"
            };
            for (int i = 0; i < members.Length; i++)
            {
                if (!TryGetMember(candidate, members[i], out value) || value == null || ReferenceEquals(value, candidate))
                    continue;

                GameObject gameObject = value as GameObject;
                if (gameObject != null)
                    return gameObject;

                Component ownerComponent = value as Component;
                if (ownerComponent != null)
                    return ownerComponent.gameObject;

                object wrapped;
                if (TryGetMember(value, "Value", out wrapped))
                {
                    gameObject = wrapped as GameObject;
                    if (gameObject != null)
                        return gameObject;
                }
            }

            return null;
        }

        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
                return "<null>";

            List<string> names = new List<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                names.Add(current.gameObject.name ?? "<unnamed>");
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        public static Component LocateMyFSM(this GameObject owner, string fsmName)
        {
            if (owner == null || string.IsNullOrEmpty(fsmName))
                return null;

            Type playMakerFsmType = AccessTools.TypeByName("PlayMakerFSM");
            if (playMakerFsmType == null)
                return null;

            Component[] components = owner.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !playMakerFsmType.IsInstanceOfType(component))
                    continue;

                if (string.Equals(GetFsmName(component), fsmName, StringComparison.Ordinal))
                    return component;
            }

            return null;
        }

        public static string GetFsmName(object fsmComponent)
        {
            object value;
            if (TryGetMember(fsmComponent, "FsmName", out value) ||
                TryGetMember(fsmComponent, "fsmName", out value) ||
                TryGetMember(fsmComponent, "Name", out value))
                return value == null ? string.Empty : value.ToString();

            object fsm;
            if (TryGetMember(fsmComponent, "Fsm", out fsm) && fsm != null &&
                TryGetMember(fsm, "Name", out value))
                return value == null ? string.Empty : value.ToString();

            return string.Empty;
        }

        public static string GetActiveStateName(object fsmComponentOrFsm)
        {
            object value;
            if (TryGetMember(fsmComponentOrFsm, "ActiveStateName", out value) ||
                TryGetMember(fsmComponentOrFsm, "activeStateName", out value))
                return value == null ? string.Empty : value.ToString();

            object fsm;
            if (TryGetMember(fsmComponentOrFsm, "Fsm", out fsm) && fsm != null &&
                (TryGetMember(fsm, "ActiveStateName", out value) ||
                 TryGetMember(fsm, "activeStateName", out value)))
                return value == null ? string.Empty : value.ToString();

            return string.Empty;
        }

        public static GameObject GetFsmGameObjectVariable(object fsmComponent, string variableName)
        {
            object variable = GetFsmVariable(fsmComponent, "GetFsmGameObject", variableName);
            if (variable == null)
                return null;

            object value;
            return TryGetMember(variable, "Value", out value) ? value as GameObject : null;
        }

        public static bool SetFsmGameObjectVariable(object fsmComponent, string variableName, GameObject value)
        {
            object variable = GetFsmVariable(fsmComponent, "GetFsmGameObject", variableName);
            return variable != null && TrySetMember(variable, "Value", value);
        }

        public static bool TrySetFsmState(object fsmComponentOrFsm, string stateName)
        {
            if (fsmComponentOrFsm == null || string.IsNullOrEmpty(stateName))
                return false;

            if (InvokeStringMethod(fsmComponentOrFsm, "SetState", stateName))
                return true;

            object fsm;
            return TryGetMember(fsmComponentOrFsm, "Fsm", out fsm) &&
                   fsm != null &&
                   InvokeStringMethod(fsm, "SetState", stateName);
        }

        public static bool SendFsmEvent(object fsmComponentOrFsm, string eventName)
        {
            if (fsmComponentOrFsm == null || string.IsNullOrEmpty(eventName))
                return false;

            if (InvokeStringMethod(fsmComponentOrFsm, "SendEvent", eventName) ||
                InvokeStringMethod(fsmComponentOrFsm, "Event", eventName))
                return true;

            object fsm;
            return TryGetMember(fsmComponentOrFsm, "Fsm", out fsm) &&
                   fsm != null &&
                   (InvokeStringMethod(fsm, "SendEvent", eventName) ||
                    InvokeStringMethod(fsm, "Event", eventName));
        }

        public static bool IsUiCharmsOwner(object candidate)
        {
            if (candidate == null)
                return false;

            EnsureResolved();
            if (ReferenceEquals(candidate, _uiCharmsFsm))
                return true;

            string directName = GetFsmName(candidate);
            GameObject owner = GetOwnerGameObject(candidate);
            string ownerName = owner == null ? string.Empty : owner.name ?? string.Empty;
            bool charmNamed = directName.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ownerName.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0;
            if (string.Equals(directName, "UI Charms", StringComparison.Ordinal) ||
                IsUnderCharmGrid(owner) ||
                (charmNamed && IsUnderCharmPane(owner)))
                return true;

            object component;
            if ((TryGetMember(candidate, "FsmComponent", out component) ||
                 TryGetMember(candidate, "Owner", out component) ||
                 TryGetMember(candidate, "OwnerComponent", out component)) &&
                component != null)
            {
                string name = GetFsmName(component);
                GameObject componentOwner = GetOwnerGameObject(component);
                string componentOwnerName = componentOwner == null
                    ? string.Empty
                    : componentOwner.name ?? string.Empty;
                if (string.Equals(name, "UI Charms", StringComparison.Ordinal) ||
                    IsUnderCharmGrid(componentOwner) ||
                    ((name.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      componentOwnerName.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0) &&
                     IsUnderCharmPane(componentOwner)))
                    return true;
            }

            // Do not treat every FSM under a broad pause-menu ancestor as charm UI.
            // Unity 6 may place the resolved pane above multiple inventory tabs.
            return false;
        }

        public static string GetEventName(object eventArgument)
        {
            if (eventArgument == null)
                return string.Empty;

            string text = eventArgument as string;
            if (text != null)
                return text;

            object value;
            if (TryGetMember(eventArgument, "Name", out value) ||
                TryGetMember(eventArgument, "name", out value))
                return value == null ? string.Empty : value.ToString();

            return eventArgument.ToString();
        }

        public static bool TryGetMember(object target, string name, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrEmpty(name))
                return false;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    value = property.GetValue(target, null);
                    return true;
                }
                catch
                {
                    // Try a field with the same name.
                }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field == null)
                return false;

            try
            {
                value = field.GetValue(target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
                return false;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(target, ConvertMemberValue(value, property.PropertyType), null);
                    return true;
                }
                catch
                {
                    // Try a field with the same name.
                }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field == null)
                return false;

            try
            {
                field.SetValue(target, ConvertMemberValue(value, field.FieldType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object GetFsmVariable(object fsmComponent, string getterName, string variableName)
        {
            if (fsmComponent == null || string.IsNullOrEmpty(variableName))
                return null;

            object variables;
            if (!(TryGetMember(fsmComponent, "FsmVariables", out variables) ||
                  TryGetMember(fsmComponent, "fsmVariables", out variables)))
            {
                object fsm;
                if (!TryGetMember(fsmComponent, "Fsm", out fsm) || fsm == null ||
                    !(TryGetMember(fsm, "Variables", out variables) ||
                      TryGetMember(fsm, "FsmVariables", out variables)))
                    return null;
            }

            if (variables == null)
                return null;

            MethodInfo getter = variables.GetType().GetMethod(
                getterName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            if (getter == null)
                return null;

            try
            {
                return getter.Invoke(variables, new object[] { variableName });
            }
            catch
            {
                return null;
            }
        }

        private static bool InvokeStringMethod(object target, string methodName, string value)
        {
            if (target == null)
                return false;

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            if (method == null)
                return false;

            try
            {
                method.Invoke(target, new object[] { value });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureResolved()
        {
            bool paneAlive = _charmsPane != null;
            bool gridAlive = _charmsGrid != null;
            if (paneAlive && gridAlive && _uiCharmsFsm != null)
                return;
            if (Time.unscaledTime < _nextProbeAt)
                return;

            _nextProbeAt = Time.unscaledTime + 0.75f;
            ResolveFromCharmItems();
            ResolveLegacyUiCharms();

            if (_charmsGrid != null && _charmsPane == null)
                _charmsPane = FindPaneFromGrid(_charmsGrid);
            if (_charmsPane == null && _charmsGrid != null)
                _charmsPane = _charmsGrid;
            if (_charmsGrid == null && _charmsPane != null)
            {
                Transform grid = FindNamedDescendant(_charmsPane.transform, "Charms", 8);
                if (grid != null)
                    _charmsGrid = grid.gameObject;
            }
            if (_uiCharmsFsm == null)
                _uiCharmsFsm = FindBestCharmNavigationFsm(_charmsPane, _charmsGrid);
        }

        private static void ResolveFromCharmItems()
        {
            Component[] items = FindAllCharmItems();
            if (items.Length == 0)
                return;

            Dictionary<Transform, HashSet<int>> idsByGrid =
                new Dictionary<Transform, HashSet<int>>();
            Dictionary<Transform, int> activeByGrid =
                new Dictionary<Transform, int>();

            for (int i = 0; i < items.Length; i++)
            {
                Component item = items[i];
                int id;
                if (!TryGetCharmItemId(item, out id) || id < 1 || id > 42 ||
                    !IsLiveSceneObject(item.gameObject))
                    continue;

                Transform grid = FindCharmCollectionAncestor(item.transform);
                if (grid == null)
                    continue;

                HashSet<int> ids;
                if (!idsByGrid.TryGetValue(grid, out ids))
                {
                    ids = new HashSet<int>();
                    idsByGrid[grid] = ids;
                    activeByGrid[grid] = 0;
                }
                ids.Add(id);
                if (item.gameObject.activeInHierarchy)
                    activeByGrid[grid] = activeByGrid[grid] + 1;
            }

            Transform best = null;
            int bestScore = int.MinValue;
            foreach (KeyValuePair<Transform, HashSet<int>> pair in idsByGrid)
            {
                Transform grid = pair.Key;
                int active;
                activeByGrid.TryGetValue(grid, out active);
                int score = pair.Value.Count * 100 + active * 5;
                if (grid != null && grid.parent != null)
                    score += 20;
                if (grid != null && grid.gameObject.activeInHierarchy)
                    score += 10;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = grid;
                }
            }

            if (best == null)
                return;

            _charmsGrid = best.gameObject;
            _charmsPane = FindPaneFromGrid(_charmsGrid);
        }

        private static void ResolveLegacyUiCharms()
        {
            if (_uiCharmsFsm != null && _charmsPane != null)
                return;

            Type playMakerFsmType = AccessTools.TypeByName("PlayMakerFSM");
            if (playMakerFsmType == null)
                return;

            UnityEngine.Object[] fsms = Resources.FindObjectsOfTypeAll(playMakerFsmType);
            Component best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < fsms.Length; i++)
            {
                Component component = fsms[i] as Component;
                if (component == null || component.gameObject == null ||
                    !string.Equals(GetFsmName(component), "UI Charms", StringComparison.Ordinal))
                    continue;

                int score = IsLiveSceneObject(component.gameObject) ? 1000 : 0;
                if (component.gameObject.activeInHierarchy)
                    score += 100;
                if (FindNamedDescendant(component.transform, "Charms", 8) != null)
                    score += 50;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }
            }

            if (best != null)
            {
                _uiCharmsFsm = best;
                if (_charmsPane == null)
                    _charmsPane = best.gameObject;
                if (_charmsGrid == null)
                {
                    Transform grid = FindNamedDescendant(best.transform, "Charms", 8);
                    _charmsGrid = grid == null ? best.gameObject : grid.gameObject;
                }
            }
        }

        private static Type ResolveCharmItemTypeQuietly()
        {
            if (_charmItemTypeResolved)
                return _charmItemType;

            _charmItemTypeResolved = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                    continue;

                Type exact = assembly.GetType("CharmItem", false);
                if (exact != null && typeof(Component).IsAssignableFrom(exact))
                {
                    _charmItemType = exact;
                    return _charmItemType;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types = GetLoadableTypes(assemblies[i]);
                for (int j = 0; j < types.Length; j++)
                {
                    Type candidate = types[j];
                    if (candidate == null || !typeof(Component).IsAssignableFrom(candidate))
                        continue;
                    if (string.Equals(candidate.Name, "CharmItem", StringComparison.OrdinalIgnoreCase))
                    {
                        _charmItemType = candidate;
                        return _charmItemType;
                    }
                }
            }

            return null;
        }

        public static bool IsLiveSceneObject(GameObject gameObject)
        {
            if (gameObject == null)
                return false;
            try
            {
                return gameObject.scene.IsValid() && gameObject.scene.isLoaded;
            }
            catch
            {
                return gameObject.activeInHierarchy;
            }
        }

        private static Transform FindCharmCollectionAncestor(Transform start)
        {
            Transform current = start;
            for (int depth = 0; current != null && depth < 16; depth++, current = current.parent)
            {
                string name = current.gameObject.name ?? string.Empty;
                if (string.Equals(name, "Equipped Charms", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (string.Equals(name, "Charms", StringComparison.OrdinalIgnoreCase))
                    return current;
            }
            return null;
        }

        private static GameObject FindPaneFromGrid(GameObject grid)
        {
            if (grid == null)
                return null;

            List<Transform> ancestors = new List<Transform>();
            Transform current = grid.transform;
            for (int depth = 0; current != null && depth < 12; depth++, current = current.parent)
                ancestors.Add(current);

            // Prefer the nearest native screen root that owns both the collection and
            // equipped strip. This avoids accidentally selecting UICanvas/_UIManager,
            // which also contains the equipment and journal tabs in Unity 6.
            for (int i = 0; i < ancestors.Count; i++)
            {
                GameObject candidate = ancestors[i].gameObject;
                if (HasComponentTypeName(candidate, "FadeGroup") &&
                    FindNamedDescendant(candidate.transform, "Equipped Charms", 6) != null)
                    return candidate;
            }

            for (int i = 0; i < ancestors.Count; i++)
            {
                GameObject candidate = ancestors[i].gameObject;
                string name = candidate.name ?? string.Empty;
                bool screenNamed = name.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   name.IndexOf("PlaymodeMenuScreen", StringComparison.OrdinalIgnoreCase) >= 0;
                if (screenNamed && FindNamedDescendant(candidate.transform, "Equipped Charms", 6) != null)
                    return candidate;
            }

            Transform best = grid.transform;
            int bestScore = 0;
            for (int i = 0; i < ancestors.Count; i++)
            {
                int score = ScorePaneCandidate(ancestors[i].gameObject, i);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = ancestors[i];
                }
            }
            return best == null ? grid : best.gameObject;
        }

        private static int ScorePaneCandidate(GameObject candidate, int depth)
        {
            if (candidate == null)
                return int.MinValue;

            string name = candidate.name ?? string.Empty;
            int score = 120 - (depth * 12);
            if (string.Equals(name, "Charms", StringComparison.OrdinalIgnoreCase))
                score += 20;
            if (name.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 30;
            if (name.IndexOf("PlaymodeMenuScreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("NewPauseMenuScreen", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 180;
            if (name.IndexOf("UICanvas", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 20;
            if (HasComponentTypeName(candidate, "FadeGroup"))
                score += 220;
            if (FindNamedDescendant(candidate.transform, "Equipped Charms", 5) != null)
                score += 120;
            if (FindNamedDescendant(candidate.transform, "Charms", 5) != null)
                score += 40;
            return score;
        }

        private static Component FindBestCharmNavigationFsm(GameObject pane, GameObject grid)
        {
            Type playMakerFsmType = AccessTools.TypeByName("PlayMakerFSM");
            if (playMakerFsmType == null)
                return null;

            List<Component> candidates = new List<Component>();
            AddFsmCandidates(pane, playMakerFsmType, candidates);
            if (grid != null && grid != pane)
                AddFsmCandidates(grid, playMakerFsmType, candidates);

            Transform ancestor = pane == null ? null : pane.transform.parent;
            for (int depth = 0; ancestor != null && depth < 5; depth++, ancestor = ancestor.parent)
            {
                Component[] components = ancestor.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component != null && playMakerFsmType.IsInstanceOfType(component) && !candidates.Contains(component))
                        candidates.Add(component);
                }
            }

            Component best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Component fsm = candidates[i];
                if (fsm == null || fsm.gameObject == null)
                    continue;

                string fsmName = GetFsmName(fsm);
                string ownerName = fsm.gameObject.name ?? string.Empty;
                int score = 0;
                if (string.Equals(fsmName, "UI Charms", StringComparison.Ordinal))
                    score += 1000;
                if (fsmName.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 300;
                if (ownerName.IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 180;
                if (GetFsmGameObjectVariable(fsm, "Item") != null ||
                    GetFsmVariable(fsm, "GetFsmGameObject", "Item") != null)
                    score += 100;
                if (IsSameOrChild(fsm.gameObject, grid))
                    score += 80;
                if (fsm.gameObject.activeInHierarchy)
                    score += 20;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = fsm;
                }
            }

            return best;
        }

        private static void AddFsmCandidates(GameObject root, Type playMakerFsmType, List<Component> result)
        {
            if (root == null)
                return;

            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && playMakerFsmType.IsInstanceOfType(component) && !result.Contains(component))
                    result.Add(component);
            }
        }

        private static Transform FindNamedDescendant(Transform root, string name, int maxDepth)
        {
            if (root == null || maxDepth < 0)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                    continue;
                if (string.Equals(child.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return child;

                Transform nested = FindNamedDescendant(child, name, maxDepth - 1);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static bool HasComponentTypeName(GameObject gameObject, string typeName)
        {
            if (gameObject == null)
                return false;

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSameOrChild(GameObject candidate, GameObject root)
        {
            if (candidate == null || root == null)
                return false;
            return candidate == root || candidate.transform.IsChildOf(root.transform);
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

        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (value == null || targetType == null)
                return value;
            if (targetType.IsInstanceOfType(value))
                return value;
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, Convert.ToInt32(value));
            return Convert.ChangeType(value, targetType);
        }
    }
}
