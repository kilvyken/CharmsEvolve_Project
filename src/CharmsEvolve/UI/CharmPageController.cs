using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using CharmsEvolve.Data;
using CharmsEvolve.Gameplay;
using CharmsEvolve.Icons;
using CharmsEvolve.Interop;

namespace CharmsEvolve.UI
{
    /// <summary>
    /// Pages the game's own 40-slot charm collection. It reuses CharmsPane, the
    /// "UI Charms" FSM, the original cursor, the original grid roots, native Sprite
    /// assets, native detail widgets and native visual/audio actions.
    /// </summary>
    internal sealed class CharmPageController : IDisposable
    {
        private const int PageCount = 4;
        private const float PageFadeOutSeconds = 0.08f;
        private const float PageFadeInSeconds = 0.11f;

        // Same world-space position and collider size used by CharmPreset's selector.
        private static readonly Vector3 PageSelectorPosition = new Vector3(0.6f, 1.4f, -3.33f);
        private static readonly Vector2 PageSelectorColliderSize = new Vector2(1.3f, 1.3f);

        private sealed class MarkerState
        {
            public GameObject GameObject;
            public bool OriginalActive;
        }

        private sealed class Slot
        {
            public int OriginalId;
            public int Column;
            public int Row;
            public GameObject Root;
            public SpriteRenderer Icon;
            public Sprite OriginalSprite;
            public Color OriginalColor;
            public bool OriginalEnabled;
            public readonly List<MarkerState> EquippedMarkers = new List<MarkerState>();
            public readonly List<MarkerState> LockedMarkers = new List<MarkerState>();

            public bool Contains(GameObject selected)
            {
                if (selected == null || Root == null)
                    return false;

                Transform selectedTransform = selected.transform;
                Transform rootTransform = Root.transform;
                return selected == Root ||
                       selectedTransform.IsChildOf(rootTransform) ||
                       rootTransform.IsChildOf(selectedTransform);
            }
        }

        private readonly Plugin _plugin;
        private readonly CharmStateService _state;
        private readonly CharmTextureRegistry _textures;
        private readonly ComboEngine _combos;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<int, Slot> _slotByOriginalId = new Dictionary<int, Slot>();

        private GameObject _pane;
        private Component _uiCharmsFsm;
        private Component _updateCursorFsm;
        private Component _nameText;
        private Component _descriptionText;
        private SpriteRenderer _detailIcon;
        private object _fadeGroup;

        private GameObject _pageSelector;
        private SpriteRenderer _pageSelectorRenderer;
        private BoxCollider2D _pageSelectorCollider;
        private readonly Sprite[] _pageSelectorSprites = new Sprite[PageCount];
        private bool _pageSelectorSelected;
        private Slot _lastGridSlot;

        private string _originalName = string.Empty;
        private string _originalDescription = string.Empty;
        private Sprite _originalDetailSprite;
        private Color _originalDetailColor;
        private bool _originalDetailEnabled;
        private bool _detailSnapshotValid;

        private int _page;
        private bool _transitioning;
        private Coroutine _pageTransition;
        private float _nextBuildAttempt;
        private float _nextRefresh;
        private string _status = string.Empty;
        private float _statusUntil;
        private bool _built;
        private bool _disposed;
        private int _lastConsumedFrame = -1;
        private string _lastConsumedEvent = string.Empty;
        private bool _loggedMissingNativeFeedback;

        private static CharmPageController _eventTarget;

        public CharmPageController(
            Plugin plugin,
            CharmStateService state,
            CharmTextureRegistry textures,
            ComboEngine combos)
        {
            _plugin = plugin;
            _state = state;
            _textures = textures;
            _combos = combos;
        }

        public void InstallPatches(Harmony harmony)
        {
            _eventTarget = this;
            MethodInfo prefix = AccessTools.Method(typeof(CharmPageController), "FsmEventPrefix");
            int patched = 0;

            Type playMakerFsm = AccessTools.TypeByName("PlayMakerFSM");
            patched += PatchOneArgumentEventMethods(harmony, playMakerFsm, "SendEvent", prefix);

            Type fsm = AccessTools.TypeByName("HutongGames.PlayMaker.Fsm");
            patched += PatchOneArgumentEventMethods(harmony, fsm, "Event", prefix);

            Plugin.Log.LogInfo("Patched native charm-navigation event entry points: " + patched);
        }

        public void Tick()
        {
            if (_disposed)
                return;

            if (!_built)
            {
                TryBuild();
                return;
            }

            if (_pane == null || _uiCharmsFsm == null)
            {
                ResetBuild();
                return;
            }

            // CharmPreset constrains this selector because the inventory may shift it by +100 Y.
            if (_pageSelector != null)
            {
                Vector3 position = _pageSelector.transform.position;
                if (Mathf.Abs(position.y - PageSelectorPosition.y) > 0.01f)
                {
                    position.y = PageSelectorPosition.y;
                    _pageSelector.transform.position = position;
                }
            }

            // Do not reset the selected charm page when the player visits the inventory's
            // equipment or journal panes. Keeping it makes the page feel integrated with the
            // original left/right inventory arrows.
            if (_pane.activeInHierarchy && _page > 0 && !_transitioning &&
                Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.10f;
                ApplyPageVisuals();
            }
        }

        public void LateTick()
        {
            if (!_built || _pane == null || !_pane.activeInHierarchy)
                return;

            if (_page > 0 && !_transitioning)
            {
                Slot selected = GetSelectedSlot();
                if (selected != null)
                {
                    _lastGridSlot = selected;
                    UpdateDetails(selected);
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            if (_eventTarget == this)
                _eventTarget = null;

            if (_pageTransition != null && _plugin != null)
                _plugin.StopCoroutine(_pageTransition);

            RestoreVanillaVisuals();
            RestoreNativeDetails();
            if (_pageSelector != null)
                UnityEngine.Object.Destroy(_pageSelector);

            _slots.Clear();
            _slotByOriginalId.Clear();
            ResetBuildObjectsOnly();
        }

        private static int PatchOneArgumentEventMethods(
            Harmony harmony,
            Type type,
            string methodName,
            MethodInfo prefix)
        {
            if (type == null || prefix == null)
                return 0;

            int count = 0;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                Type parameterType = parameters[0].ParameterType;
                if (parameterType != typeof(string) &&
                    parameterType.Name.IndexOf("FsmEvent", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                    count++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug("Could not patch " + type.FullName + "." + methodName + ": " + ex.Message);
                }
            }

            return count;
        }

        private static bool FsmEventPrefix(object __instance, object __0)
        {
            CharmPageController target = _eventTarget;
            if (target == null || target._disposed)
                return true;

            string eventName = CharmUtil.GetEventName(__0);
            return !target.TryConsumeUiEvent(__instance, eventName);
        }

        private bool TryConsumeUiEvent(object eventOwner, string eventName)
        {
            if (!_built || _pane == null || !_pane.activeInHierarchy)
                return false;
            if (!CharmUtil.IsUiCharmsOwner(eventOwner))
                return false;
            if (string.IsNullOrEmpty(eventName))
                return false;

            // One input can pass through both PlayMakerFSM.SendEvent and Fsm.Event.
            // A duplicated event that was already consumed must remain consumed.
            if (_lastConsumedFrame == Time.frameCount &&
                string.Equals(_lastConsumedEvent, eventName, StringComparison.Ordinal))
                return true;

            bool onSelector = IsPageSelectorSelected();
            Slot selected = GetSelectedSlot();
            string stateName = CharmUtil.GetActiveStateName(_uiCharmsFsm);

            if (onSelector)
            {
                if (IsEvent(eventName, "UI LEFT"))
                {
                    MarkConsumed(eventName);
                    RequestPage((_page + PageCount - 1) % PageCount);
                    return true;
                }

                if (IsEvent(eventName, "UI RIGHT"))
                {
                    MarkConsumed(eventName);
                    RequestPage((_page + 1) % PageCount);
                    return true;
                }

                if (IsEvent(eventName, "UI UP") || IsEvent(eventName, "UI RS UP"))
                {
                    MarkConsumed(eventName);
                    LeaveSelectorToEquipment();
                    return true;
                }

                if (IsEvent(eventName, "UI DOWN") || IsEvent(eventName, "UI RS DOWN"))
                {
                    MarkConsumed(eventName);
                    LeaveSelectorToGrid();
                    return true;
                }

                // Confirm is deliberately not a page switch. Left/right are the only page
                // controls, matching the requested CharmPreset-style navigation.
                if (IsEvent(eventName, "UI CONFIRM"))
                {
                    MarkConsumed(eventName);
                    return true;
                }
            }

            // The selector sits between the equipped strip and the collection. This is the
            // equivalent of CharmPreset's To Preset / Idle Preset path.
            if (selected != null && selected.Row == 0 &&
                (IsEvent(eventName, "UI UP") || IsEvent(eventName, "UI RS UP")))
            {
                MarkConsumed(eventName);
                SelectPageSelector(selected);
                return true;
            }

            if (StateContains(stateName, "Equipped") &&
                (IsEvent(eventName, "UI DOWN") || IsEvent(eventName, "UI RS DOWN")))
            {
                MarkConsumed(eventName);
                SelectPageSelector(_lastGridSlot);
                return true;
            }

            // All horizontal collection movement is left to the native FSM. In particular,
            // left/right at the grid edge still reaches the inventory arrows and then the
            // equipment/journal panes.
            if (_page > 0 && selected != null && IsEvent(eventName, "UI CONFIRM"))
            {
                MarkConsumed(eventName);
                ConfirmSelected(selected);
                return true;
            }

            return false;
        }

        private static bool IsEvent(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.Ordinal);
        }

        private static bool StateContains(string stateName, string fragment)
        {
            return !string.IsNullOrEmpty(stateName) &&
                   stateName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void MarkConsumed(string eventName)
        {
            _lastConsumedFrame = Time.frameCount;
            _lastConsumedEvent = eventName;
        }

        private bool IsPageSelectorSelected()
        {
            GameObject selected = GetSelectedObject();
            if (_pageSelector != null && selected != null)
            {
                Transform selectedTransform = selected.transform;
                Transform selectorTransform = _pageSelector.transform;
                _pageSelectorSelected = selected == _pageSelector ||
                                        selectedTransform.IsChildOf(selectorTransform) ||
                                        selectorTransform.IsChildOf(selectedTransform);
            }

            return _pageSelectorSelected;
        }

        private void SelectPageSelector(Slot fromSlot)
        {
            if (_pageSelector == null)
                return;

            if (fromSlot != null)
                _lastGridSlot = fromSlot;

            _pageSelectorSelected = true;
            SetCursorItem(_pageSelector);
        }

        private void LeaveSelectorToGrid()
        {
            _pageSelectorSelected = false;

            // Original reference transition: Idle Preset --UI DOWN--> To Bot.
            if (CharmUtil.TrySetFsmState(_uiCharmsFsm, "To Bot"))
                return;

            Slot fallback = _lastGridSlot;
            if (fallback == null && _slots.Count > 0)
                fallback = _slots[0];
            if (fallback != null)
                SetCursorItem(fallback.Root);
        }

        private void LeaveSelectorToEquipment()
        {
            _pageSelectorSelected = false;

            // Original reference transition: Idle Preset --UI UP--> To Equipment.
            if (CharmUtil.TrySetFsmState(_uiCharmsFsm, "To Equipment"))
                return;

            GameObject fallback = FindEquipmentTarget();
            if (fallback != null)
                SetCursorItem(fallback);
            else if (_lastGridSlot != null)
                SetCursorItem(_lastGridSlot.Root);
        }

        private void SetCursorItem(GameObject item)
        {
            if (item == null)
                return;

            bool set = false;
            if (_updateCursorFsm != null)
                set |= CharmUtil.SetFsmGameObjectVariable(_updateCursorFsm, "Item", item);
            set |= CharmUtil.SetFsmGameObjectVariable(_uiCharmsFsm, "Item", item);
            set |= CharmUtil.SetFsmGameObjectVariable(_uiCharmsFsm, "Selected Item", item);

            if (_updateCursorFsm != null)
            {
                CharmUtil.SendFsmEvent(_updateCursorFsm, "UPDATE CURSOR");
                CharmUtil.SendFsmEvent(_updateCursorFsm, "UPDATE");
            }

            if (!set)
                Plugin.Log.LogDebug("Native cursor Item variable was not found for " + item.name + ".");
        }

        private void ConfirmSelected(Slot slot)
        {
            if (_transitioning)
                return;

            CopyCharmDefinition definition = GetDefinition(slot);
            if (definition == null)
                return;

            bool wasEquipped = _state.IsEquipped(definition.Key);
            string reason;
            if (_state.Toggle(definition, out reason))
            {
                bool nowEquipped = _state.IsEquipped(definition.Key);
                _status = nowEquipped ? "已装备。" : "已卸下。";
                ApplySlotVisual(slot);
                ReplayNativeEquipFeedback(slot, nowEquipped);
            }
            else
            {
                _status = reason;
                ReplayNativeReminderFeedback(reason);
            }

            _statusUntil = Time.unscaledTime + 2.5f;
            UpdateDetails(slot);

            if (wasEquipped == _state.IsEquipped(definition.Key) && string.IsNullOrEmpty(reason))
                Plugin.Log.LogDebug("Custom charm confirm completed without an equipment-state change: " + definition.Key);
        }

        private void RequestPage(int page)
        {
            page = ((page % PageCount) + PageCount) % PageCount;
            if (page == _page || _transitioning)
                return;

            if (_pageTransition != null)
                _plugin.StopCoroutine(_pageTransition);
            _pageTransition = _plugin.StartCoroutine(PageTransition(page));
        }

        private IEnumerator PageTransition(int targetPage)
        {
            _transitioning = true;
            _status = string.Empty;
            _statusUntil = 0f;

            if (_page == 0 && targetPage > 0)
            {
                CaptureNativeGrid();
                CaptureNativeDetails();
            }

            // CharmPreset uses the audio action at UI Charms/Tween Up[1] when changing
            // presets; reuse the same native action for page changes.
            TryInvokeNativeAudioAction("Tween Up", 1);

            float elapsed = 0f;
            while (elapsed < PageFadeOutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetGridTransitionAlpha(1f - Mathf.Clamp01(elapsed / PageFadeOutSeconds));
                yield return null;
            }

            _page = targetPage;
            UpdatePageSelectorSprite();
            if (_page == 0)
            {
                RestoreVanillaVisuals();
                RestoreNativeDetails();
            }
            else
            {
                ApplyPageVisuals();
                Slot selected = GetSelectedSlot();
                if (selected != null)
                    UpdateDetails(selected);
            }

            elapsed = 0f;
            while (elapsed < PageFadeInSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetGridTransitionAlpha(Mathf.Clamp01(elapsed / PageFadeInSeconds));
                yield return null;
            }

            if (_page == 0)
                RestoreVanillaVisuals();
            else
                ApplyPageVisuals();

            _transitioning = false;
            _pageTransition = null;
        }

        private void SetGridTransitionAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Icon == null)
                    continue;

                Color baseColor = slot.OriginalColor;
                slot.Icon.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            }
        }

        private void TryBuild()
        {
            if (Time.unscaledTime < _nextBuildAttempt)
                return;
            _nextBuildAttempt = Time.unscaledTime + 0.75f;

            GameObject pane = CharmUtil.CharmsPane;
            Component uiFsm = CharmUtil.UiCharmsFsm;
            if (pane == null || uiFsm == null)
                return;

            _pane = pane;
            _uiCharmsFsm = uiFsm;
            _updateCursorFsm = FindFsmInChildren(pane, "Update Cursor");
            _fadeGroup = FindComponentByTypeName(pane, "FadeGroup");

            if (!BuildSlots())
            {
                Plugin.Log.LogWarning("UI Charms found, but the native 40-charm grid could not be resolved yet.");
                ResetBuildObjectsOnly();
                return;
            }

            ResolveDetailPanel();
            BuildPageSelector();
            EnforceNativeRendererSettings();

            if (_nameText == null || _descriptionText == null)
                Plugin.Log.LogWarning("Native charm detail text was not fully resolved; current-game object names may differ.");
            if (_detailIcon == null)
                Plugin.Log.LogWarning("Native charm detail SpriteRenderer was not resolved.");

            _built = true;
            UpdatePageSelectorSprite();
            Plugin.Log.LogInfo("Native charm pager ready. Grid slots: " + _slots.Count + ".");
        }

        private bool BuildSlots()
        {
            _slots.Clear();
            _slotByOriginalId.Clear();

            SpriteRenderer[] renderers = _pane.GetComponentsInChildren<SpriteRenderer>(true);
            Dictionary<int, SpriteRenderer> bestRenderer = new Dictionary<int, SpriteRenderer>();
            Dictionary<int, int> bestScore = new Dictionary<int, int>();

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer.sprite == null)
                    continue;

                int id;
                if (!TryParseCharmSpriteId(renderer.sprite.name, out id) || id < 1 || id > 40)
                    continue;

                int score = ScoreGridRenderer(renderer);
                int previous;
                if (bestScore.TryGetValue(id, out previous) && previous >= score)
                    continue;

                bestScore[id] = score;
                bestRenderer[id] = renderer;
            }

            for (int id = 1; id <= 40; id++)
            {
                SpriteRenderer renderer;
                if (!bestRenderer.TryGetValue(id, out renderer))
                    continue;

                GameObject root = FindSelectableRoot(renderer.gameObject, _pane.transform);
                Slot slot = new Slot
                {
                    OriginalId = id,
                    Root = root,
                    Icon = renderer,
                    OriginalSprite = renderer.sprite,
                    OriginalColor = renderer.color,
                    OriginalEnabled = renderer.enabled
                };
                CaptureMarkers(slot);
                _slots.Add(slot);
                _slotByOriginalId[id] = slot;
            }

            if (_slots.Count < 40)
                return false;

            AssignGridCoordinates();
            return true;
        }

        private static bool TryParseCharmSpriteId(string name, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(name))
                return false;

            Match match = Regex.Match(name, @"(?:^|_)charm0*(\d+)(?:$|_)", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out id);
        }

        private int ScoreGridRenderer(SpriteRenderer renderer)
        {
            int score = 0;
            Transform current = renderer.transform;
            while (current != null && current != _pane.transform)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    string typeName = component.GetType().Name;
                    if (typeName.IndexOf("Collider2D", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 100;
                    if (string.Equals(typeName, "PlayMakerFSM", StringComparison.Ordinal))
                        score += 30;
                }
                current = current.parent;
            }

            string objectName = renderer.gameObject.name ?? string.Empty;
            if (objectName.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 20;

            Vector3 paneLocal = _pane.transform.InverseTransformPoint(renderer.transform.position);
            if (paneLocal.y < 1.2f)
                score += 20;
            return score;
        }

        private static GameObject FindSelectableRoot(GameObject icon, Transform pane)
        {
            Transform current = icon.transform;
            GameObject best = icon;
            while (current != null && current != pane)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    string typeName = component.GetType().Name;
                    if (typeName.IndexOf("Collider2D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(typeName, "PlayMakerFSM", StringComparison.Ordinal))
                        best = current.gameObject;
                }
                current = current.parent;
            }
            return best;
        }

        private void AssignGridCoordinates()
        {
            _slots.Sort(delegate(Slot a, Slot b)
            {
                Vector3 pa = _pane.transform.InverseTransformPoint(a.Root.transform.position);
                Vector3 pb = _pane.transform.InverseTransformPoint(b.Root.transform.position);
                int rowCompare = -pa.y.CompareTo(pb.y);
                return rowCompare != 0 ? rowCompare : pa.x.CompareTo(pb.x);
            });

            int row = -1;
            float rowY = float.PositiveInfinity;
            int column = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                float y = _pane.transform.InverseTransformPoint(slot.Root.transform.position).y;
                if (row < 0 || Mathf.Abs(y - rowY) > 0.28f)
                {
                    row++;
                    rowY = y;
                    column = 0;
                }

                slot.Row = row;
                slot.Column = column++;
            }
        }

        private static void CaptureMarkers(Slot slot)
        {
            SpriteRenderer[] children = slot.Root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < children.Length; i++)
            {
                SpriteRenderer renderer = children[i];
                if (renderer == null || renderer == slot.Icon)
                    continue;

                string name = renderer.gameObject.name ?? string.Empty;
                if (name.IndexOf("equipped", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slot.EquippedMarkers.Add(new MarkerState
                    {
                        GameObject = renderer.gameObject,
                        OriginalActive = renderer.gameObject.activeSelf
                    });
                }
                else if (name.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("unowned", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slot.LockedMarkers.Add(new MarkerState
                    {
                        GameObject = renderer.gameObject,
                        OriginalActive = renderer.gameObject.activeSelf
                    });
                }
            }
        }

        private void CaptureNativeGrid()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Icon != null)
                {
                    slot.OriginalSprite = slot.Icon.sprite;
                    slot.OriginalColor = slot.Icon.color;
                    slot.OriginalEnabled = slot.Icon.enabled;
                }

                CaptureMarkerActivity(slot.EquippedMarkers);
                CaptureMarkerActivity(slot.LockedMarkers);
            }
        }

        private static void CaptureMarkerActivity(List<MarkerState> markers)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                MarkerState marker = markers[i];
                if (marker.GameObject != null)
                    marker.OriginalActive = marker.GameObject.activeSelf;
            }
        }

        private void ApplyPageVisuals()
        {
            for (int i = 0; i < _slots.Count; i++)
                ApplySlotVisual(_slots[i]);
        }

        private void ApplySlotVisual(Slot slot)
        {
            if (slot == null || slot.Icon == null)
                return;

            if (_page == 0)
            {
                RestoreSlot(slot);
                return;
            }

            CopyCharmDefinition definition = GetDefinition(slot);
            if (definition == null)
                return;

            Sprite sprite;
            if (_textures.TryGetSprite(definition.Key, definition.OriginalId, out sprite) && sprite != null)
                slot.Icon.sprite = sprite;
            else
                slot.Icon.sprite = slot.OriginalSprite;

            bool owned = _state.IsOwned(definition.Key);
            slot.Icon.enabled = owned;
            slot.Icon.color = slot.OriginalColor;
            SetMarkers(slot.EquippedMarkers, _state.IsEquipped(definition.Key));
            SetMarkers(slot.LockedMarkers, !owned);
        }

        private void RestoreVanillaVisuals()
        {
            for (int i = 0; i < _slots.Count; i++)
                RestoreSlot(_slots[i]);
        }

        private static void RestoreSlot(Slot slot)
        {
            if (slot == null || slot.Icon == null)
                return;

            slot.Icon.sprite = slot.OriginalSprite;
            slot.Icon.color = slot.OriginalColor;
            slot.Icon.enabled = slot.OriginalEnabled;
            RestoreMarkers(slot.EquippedMarkers);
            RestoreMarkers(slot.LockedMarkers);
        }

        private static void RestoreMarkers(List<MarkerState> markers)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                MarkerState marker = markers[i];
                if (marker.GameObject != null)
                    marker.GameObject.SetActive(marker.OriginalActive);
            }
        }

        private static void SetMarkers(List<MarkerState> markers, bool active)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                MarkerState marker = markers[i];
                if (marker.GameObject != null)
                    marker.GameObject.SetActive(active);
            }
        }

        private CopyCharmDefinition GetDefinition(Slot slot)
        {
            if (slot == null || _page <= 0)
                return null;

            CopyKind kind = (CopyKind)(_page - 1);
            return CharmDatabase.GetCopy(CharmKey.For(slot.OriginalId, kind));
        }

        private GameObject GetSelectedObject()
        {
            GameObject selected = null;
            if (_updateCursorFsm != null)
                selected = CharmUtil.GetFsmGameObjectVariable(_updateCursorFsm, "Item");
            if (selected == null)
                selected = CharmUtil.GetFsmGameObjectVariable(_uiCharmsFsm, "Item");
            if (selected == null)
                selected = CharmUtil.GetFsmGameObjectVariable(_uiCharmsFsm, "Selected Item");
            return selected;
        }

        private Slot GetSelectedSlot()
        {
            GameObject selected = GetSelectedObject();
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Contains(selected))
                    return _slots[i];
            }
            return null;
        }

        private GameObject FindEquipmentTarget()
        {
            Collider2D[] colliders = _pane.GetComponentsInChildren<Collider2D>(true);
            GameObject best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || collider.gameObject == _pageSelector)
                    continue;

                bool gridObject = false;
                for (int j = 0; j < _slots.Count; j++)
                {
                    if (_slots[j].Contains(collider.gameObject))
                    {
                        gridObject = true;
                        break;
                    }
                }
                if (gridObject)
                    continue;

                string name = collider.gameObject.name ?? string.Empty;
                Vector3 local = _pane.transform.InverseTransformPoint(collider.transform.position);
                if (local.y <= PageSelectorPosition.y)
                    continue;

                float score = Mathf.Abs(local.x - PageSelectorPosition.x) + Mathf.Abs(local.y - 2.5f);
                if (name.IndexOf("equip", StringComparison.OrdinalIgnoreCase) >= 0)
                    score -= 5f;
                if (name.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score -= 2f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = collider.gameObject;
                }
            }
            return best;
        }

        private void BuildPageSelector()
        {
            Transform existing = _pane.transform.Find("CharmsEvolve Page Selector");
            if (existing != null)
                _pageSelector = existing.gameObject;
            else
            {
                _pageSelector = new GameObject("CharmsEvolve Page Selector");
                _pageSelector.transform.SetParent(_pane.transform, false);
            }

            _pageSelector.layer = ResolveUiLayer();
            _pageSelector.transform.position = PageSelectorPosition;
            _pageSelector.transform.localScale = Vector3.one;

            _pageSelectorRenderer = _pageSelector.GetComponent<SpriteRenderer>();
            if (_pageSelectorRenderer == null)
                _pageSelectorRenderer = _pageSelector.AddComponent<SpriteRenderer>();

            _pageSelectorCollider = _pageSelector.GetComponent<BoxCollider2D>();
            if (_pageSelectorCollider == null)
                _pageSelectorCollider = _pageSelector.AddComponent<BoxCollider2D>();
            _pageSelectorCollider.size = PageSelectorColliderSize;

            for (int i = 0; i < PageCount; i++)
            {
                Sprite sprite;
                if (_textures.TryGetSprite(null, i + 1, out sprite))
                    _pageSelectorSprites[i] = sprite;
            }

            _pageSelectorRenderer.sortingLayerName = "HUD";
            _pageSelectorRenderer.gameObject.layer = ResolveUiLayer();
            _pageSelectorRenderer.color = GetNativeUiColor();
            UpdatePageSelectorSprite();
        }

        private Color GetNativeUiColor()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Icon != null)
                    return _slots[i].Icon.color;
            }
            return Color.white;
        }

        private void UpdatePageSelectorSprite()
        {
            if (_pageSelectorRenderer == null)
                return;

            Sprite sprite = _pageSelectorSprites[Mathf.Clamp(_page, 0, PageCount - 1)];
            if (sprite != null)
                _pageSelectorRenderer.sprite = sprite;
        }

        private void ResolveDetailPanel()
        {
            Component[] components = _pane.GetComponentsInChildren<Component>(true);
            int bestNameScore = int.MinValue;
            int bestDescriptionScore = int.MinValue;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;

                string typeName = component.GetType().FullName ?? component.GetType().Name;
                if (typeName.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string objectName = component.gameObject.name ?? string.Empty;
                int nameScore = ScoreNameText(objectName, component);
                int descriptionScore = ScoreDescriptionText(objectName, component);
                if (nameScore > bestNameScore)
                {
                    bestNameScore = nameScore;
                    _nameText = component;
                }
                if (descriptionScore > bestDescriptionScore)
                {
                    bestDescriptionScore = descriptionScore;
                    _descriptionText = component;
                }
            }

            SpriteRenderer[] renderers = _pane.GetComponentsInChildren<SpriteRenderer>(true);
            int bestIconScore = int.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || IsGridIcon(renderer))
                    continue;

                string name = renderer.gameObject.name ?? string.Empty;
                int score = 0;
                if (name.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;
                if (name.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;

                Vector3 local = _pane.transform.InverseTransformPoint(renderer.transform.position);
                if (local.x > 0f)
                    score += 20;
                if (score > bestIconScore)
                {
                    bestIconScore = score;
                    _detailIcon = renderer;
                }
            }
        }

        private bool IsGridIcon(SpriteRenderer renderer)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Icon == renderer)
                    return true;
            }
            return false;
        }

        private static int ScoreNameText(string objectName, Component component)
        {
            int score = 0;
            if (objectName.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 100;
            if (objectName.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 50;

            string text = GetText(component);
            if (!string.IsNullOrEmpty(text) && text.Length < 80)
                score += 10;
            return score;
        }

        private static int ScoreDescriptionText(string objectName, Component component)
        {
            int score = 0;
            if (objectName.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 120;
            if (objectName.IndexOf("desc", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 90;
            if (objectName.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10;

            string text = GetText(component);
            if (!string.IsNullOrEmpty(text) && text.Length >= 40)
                score += 20;
            return score;
        }

        private void UpdateDetails(Slot slot)
        {
            CopyCharmDefinition definition = GetDefinition(slot);
            if (definition == null)
                return;

            string title = definition.DisplayName;
            string description = "花费：" + definition.Cost + " 槽\n" + definition.Description;

            IList<ActiveSynergy> synergies = _combos.GetActiveSynergiesFor(definition.Key);
            if (synergies.Count > 0)
            {
                description += "\n\n已激活联动：";
                for (int i = 0; i < synergies.Count; i++)
                    description += "\n• " + synergies[i].Description;
            }
            else if (!string.IsNullOrEmpty(definition.EnhancedSynergy))
            {
                description += "\n\n联动：" + definition.EnhancedSynergy;
            }

            if (definition.StackableSynergies != null && definition.StackableSynergies.Length > 0)
            {
                description += "\n可叠加联动：";
                for (int i = 0; i < definition.StackableSynergies.Length; i++)
                {
                    if (!string.IsNullOrEmpty(definition.StackableSynergies[i]))
                        description += "\n• " + definition.StackableSynergies[i];
                }
            }

            if (Time.unscaledTime < _statusUntil && !string.IsNullOrEmpty(_status))
                description += "\n\n" + _status;

            SetText(_nameText, title);
            SetText(_descriptionText, description);

            if (_detailIcon != null)
            {
                Sprite sprite;
                if (_textures.TryGetSprite(definition.Key, definition.OriginalId, out sprite) && sprite != null)
                    _detailIcon.sprite = sprite;
                _detailIcon.color = Color.white;
                _detailIcon.enabled = _state.IsOwned(definition.Key);
            }
        }

        private void CaptureNativeDetails()
        {
            _originalName = GetText(_nameText);
            _originalDescription = GetText(_descriptionText);
            if (_detailIcon != null)
            {
                _originalDetailSprite = _detailIcon.sprite;
                _originalDetailColor = _detailIcon.color;
                _originalDetailEnabled = _detailIcon.enabled;
            }
            _detailSnapshotValid = true;
        }

        private void RestoreNativeDetails()
        {
            if (!_detailSnapshotValid)
                return;

            SetText(_nameText, _originalName);
            SetText(_descriptionText, _originalDescription);
            if (_detailIcon != null)
            {
                _detailIcon.sprite = _originalDetailSprite;
                _detailIcon.color = _originalDetailColor;
                _detailIcon.enabled = _originalDetailEnabled;
            }
        }

        private static string GetText(Component component)
        {
            if (component == null)
                return string.Empty;

            object value;
            if (CharmUtil.TryGetMember(component, "text", out value) ||
                CharmUtil.TryGetMember(component, "Text", out value))
                return value == null ? string.Empty : value.ToString();
            return string.Empty;
        }

        private static void SetText(Component component, string text)
        {
            if (component == null)
                return;
            if (!CharmUtil.TrySetMember(component, "text", text ?? string.Empty))
                CharmUtil.TrySetMember(component, "Text", text ?? string.Empty);
        }

        private void EnforceNativeRendererSettings()
        {
            int uiLayer = ResolveUiLayer();
            List<SpriteRenderer> renderers = new List<SpriteRenderer>();
            for (int i = 0; i < _slots.Count; i++)
            {
                SpriteRenderer renderer = _slots[i].Icon;
                if (renderer == null)
                    continue;

                renderer.sortingLayerName = "HUD";
                renderer.gameObject.layer = uiLayer;
                renderers.Add(renderer);
            }

            if (_detailIcon != null)
            {
                _detailIcon.sortingLayerName = "HUD";
                _detailIcon.gameObject.layer = uiLayer;
                renderers.Add(_detailIcon);
            }

            if (_pageSelectorRenderer != null)
            {
                _pageSelectorRenderer.sortingLayerName = "HUD";
                _pageSelectorRenderer.gameObject.layer = uiLayer;
                renderers.Add(_pageSelectorRenderer);
            }

            AppendToFadeGroup(renderers);
        }

        private static int ResolveUiLayer()
        {
            Type physLayers = AccessTools.TypeByName("PhysLayers");
            if (physLayers != null && physLayers.IsEnum)
            {
                try
                {
                    return Convert.ToInt32(Enum.Parse(physLayers, "UI", true));
                }
                catch
                {
                    // Fall through to the named Unity layer.
                }
            }

            int layer = LayerMask.NameToLayer("UI");
            return layer >= 0 ? layer : 5;
        }

        private void AppendToFadeGroup(List<SpriteRenderer> additions)
        {
            if (_fadeGroup == null || additions == null || additions.Count == 0)
                return;

            object current;
            if (!(CharmUtil.TryGetMember(_fadeGroup, "spriteRenderers", out current) ||
                  CharmUtil.TryGetMember(_fadeGroup, "SpriteRenderers", out current)))
                return;

            List<SpriteRenderer> merged = new List<SpriteRenderer>();
            IEnumerable enumerable = current as IEnumerable;
            if (enumerable != null)
            {
                foreach (object value in enumerable)
                {
                    SpriteRenderer renderer = value as SpriteRenderer;
                    if (renderer != null && !merged.Contains(renderer))
                        merged.Add(renderer);
                }
            }

            for (int i = 0; i < additions.Count; i++)
            {
                if (additions[i] != null && !merged.Contains(additions[i]))
                    merged.Add(additions[i]);
            }

            SpriteRenderer[] array = merged.ToArray();
            if (!CharmUtil.TrySetMember(_fadeGroup, "spriteRenderers", array))
                CharmUtil.TrySetMember(_fadeGroup, "SpriteRenderers", array);
        }

        private void ReplayNativeEquipFeedback(Slot slot, bool equipping)
        {
            if (slot == null || slot.Root == null)
                return;

            CharmUtil.SetFsmGameObjectVariable(_uiCharmsFsm, "Item", slot.Root);
            CharmUtil.SetFsmGameObjectVariable(_uiCharmsFsm, "Selected Item", slot.Root);
            if (_updateCursorFsm != null)
                CharmUtil.SetFsmGameObjectVariable(_updateCursorFsm, "Item", slot.Root);

            string[] stateNames = equipping
                ? new[] { "Tween Up", "Equip Charm", "Equip" }
                : new[] { "Tween Down", "Unequip Charm", "Unequip" };

            bool replayed = false;
            for (int i = 0; i < stateNames.Length && !replayed; i++)
                replayed = TryInvokeNativeFeedbackActions(stateNames[i]);

            if (!replayed)
            {
                Component[] components = slot.Root.GetComponentsInChildren<Component>(true);
                string visualEvent = equipping ? "EQUIP" : "UNEQUIP";
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null || !string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                        continue;

                    replayed |= CharmUtil.SendFsmEvent(component, visualEvent);
                }
            }

            // CharmPreset uses Tween Up[1] as the native one-shot audio source.
            if (!replayed)
                replayed = TryInvokeNativeAudioAction(equipping ? "Tween Up" : "Tween Down", 1);

            if (!replayed && !_loggedMissingNativeFeedback)
            {
                _loggedMissingNativeFeedback = true;
                Plugin.Log.LogWarning(
                    "Native charm equip feedback states were not found. The current game build may use renamed UI Charms states.");
            }
        }

        private void ReplayNativeReminderFeedback(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && reason.IndexOf("长椅", StringComparison.Ordinal) >= 0)
            {
                if (!TryInvokeNativeFeedbackActions("Bench Reminder"))
                    TryInvokeNativeAudioAction("Bench Reminder", 0);
                return;
            }

            if (!TryInvokeNativeFeedbackActions("Bound Reminder"))
                TryInvokeNativeAudioAction("Bound Reminder", 0);
        }

        private bool TryInvokeNativeFeedbackActions(string stateName)
        {
            IList actions = GetStateActions(stateName);
            if (actions == null || actions.Count == 0)
                return false;

            bool invoked = false;
            for (int i = 0; i < actions.Count; i++)
            {
                object action = actions[i];
                if (action == null)
                    continue;

                string typeName = action.GetType().Name;
                if (!IsSafeNativeFeedbackAction(typeName))
                    continue;

                MethodInfo onEnter = action.GetType().GetMethod(
                    "OnEnter",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (onEnter == null)
                    continue;

                try
                {
                    onEnter.Invoke(action, null);
                    invoked = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug("Native feedback action " + typeName + " failed: " + ex.Message);
                }
            }

            return invoked;
        }

        private static bool IsSafeNativeFeedbackAction(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return false;

            if (typeName.IndexOf("PlayerData", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("SetFsm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("SendEvent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("BoolTest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("IntTest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Check", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return typeName.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Tween", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("iTween", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Animator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryInvokeNativeAudioAction(string stateName, int preferredIndex)
        {
            IList actions = GetStateActions(stateName);
            if (actions == null || actions.Count == 0)
                return false;

            if (preferredIndex >= 0 && preferredIndex < actions.Count &&
                TryInvokeActionIfAudio(actions[preferredIndex]))
                return true;

            for (int i = 0; i < actions.Count; i++)
            {
                if (TryInvokeActionIfAudio(actions[i]))
                    return true;
            }
            return false;
        }

        private static bool TryInvokeActionIfAudio(object action)
        {
            if (action == null ||
                action.GetType().Name.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            MethodInfo onEnter = action.GetType().GetMethod(
                "OnEnter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (onEnter == null)
                return false;

            try
            {
                onEnter.Invoke(action, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IList GetStateActions(string stateName)
        {
            if (_uiCharmsFsm == null || string.IsNullOrEmpty(stateName))
                return null;

            object fsm;
            if (!CharmUtil.TryGetMember(_uiCharmsFsm, "Fsm", out fsm) || fsm == null)
                return null;

            object statesValue;
            if (!(CharmUtil.TryGetMember(fsm, "States", out statesValue) ||
                  CharmUtil.TryGetMember(fsm, "states", out statesValue)))
                return null;

            IEnumerable states = statesValue as IEnumerable;
            if (states == null)
                return null;

            foreach (object state in states)
            {
                object nameValue;
                if (!CharmUtil.TryGetMember(state, "Name", out nameValue) ||
                    !string.Equals(nameValue == null ? string.Empty : nameValue.ToString(), stateName, StringComparison.Ordinal))
                    continue;

                object actionsValue;
                if (CharmUtil.TryGetMember(state, "Actions", out actionsValue) ||
                    CharmUtil.TryGetMember(state, "actions", out actionsValue))
                    return actionsValue as IList;
            }

            return null;
        }

        private static Component FindFsmInChildren(GameObject root, string fsmName)
        {
            if (root == null)
                return null;

            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                    continue;
                if (string.Equals(CharmUtil.GetFsmName(component), fsmName, StringComparison.Ordinal))
                    return component;
            }
            return null;
        }

        private static object FindComponentByTypeName(GameObject root, string typeName)
        {
            if (root == null)
                return null;

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    return component;
            }
            return null;
        }

        private void ResetBuild()
        {
            RestoreVanillaVisuals();
            RestoreNativeDetails();
            _built = false;
            ResetBuildObjectsOnly();
        }

        private void ResetBuildObjectsOnly()
        {
            _slots.Clear();
            _slotByOriginalId.Clear();
            _pane = null;
            _uiCharmsFsm = null;
            _updateCursorFsm = null;
            _nameText = null;
            _descriptionText = null;
            _detailIcon = null;
            _fadeGroup = null;
            _pageSelector = null;
            _pageSelectorRenderer = null;
            _pageSelectorCollider = null;
            _pageSelectorSelected = false;
        }
    }
}
