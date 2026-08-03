using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CharmsEvolve.Interop
{
    /// <summary>
    /// Reflection-only bridge to the live Hollow Knight charm inventory FSM.
    /// The project intentionally does not create a Canvas or require a compile-time PlayMaker reference.
    /// </summary>
    internal static class CharmUtil
    {
        private static GameObject _charmsPane;
        private static Component _uiCharmsFsm;
        private static float _nextProbeAt;

        public static GameObject CharmsPane
        {
            get
            {
                EnsureResolved();
                return _charmsPane;
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

            if (ReferenceEquals(candidate, _uiCharmsFsm))
                return true;

            string directName = GetFsmName(candidate);
            if (string.Equals(directName, "UI Charms", StringComparison.Ordinal))
                return true;

            object component;
            if ((TryGetMember(candidate, "FsmComponent", out component) ||
                 TryGetMember(candidate, "Owner", out component) ||
                 TryGetMember(candidate, "OwnerComponent", out component)) &&
                component != null)
                return string.Equals(GetFsmName(component), "UI Charms", StringComparison.Ordinal);

            object gameObjectValue;
            if ((TryGetMember(candidate, "GameObject", out gameObjectValue) ||
                 TryGetMember(candidate, "OwnerGameObject", out gameObjectValue)) &&
                gameObjectValue is GameObject)
            {
                Component fsm = ((GameObject)gameObjectValue).LocateMyFSM("UI Charms");
                return fsm != null;
            }

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
                    property.SetValue(target, value, null);
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
                field.SetValue(target, value);
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
            if (_charmsPane != null && _uiCharmsFsm != null)
                return;
            if (Time.unscaledTime < _nextProbeAt)
                return;

            _nextProbeAt = Time.unscaledTime + 0.75f;
            Type playMakerFsmType = AccessTools.TypeByName("PlayMakerFSM");
            if (playMakerFsmType == null)
                return;

            UnityEngine.Object[] fsms = Resources.FindObjectsOfTypeAll(playMakerFsmType);
            for (int i = 0; i < fsms.Length; i++)
            {
                Component component = fsms[i] as Component;
                if (component == null || component.gameObject == null)
                    continue;
                if (!string.Equals(GetFsmName(component), "UI Charms", StringComparison.Ordinal))
                    continue;

                _uiCharmsFsm = component;
                _charmsPane = component.gameObject;
                return;
            }
        }
    }
}
