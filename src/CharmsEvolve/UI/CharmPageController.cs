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
using CharmsEvolve.Api;

namespace CharmsEvolve.UI
{
    /// <summary>
    /// Pages the game's own 40-slot charm collection. Unity 6 is anchored by the
    /// live CharmItem/Charms hierarchy; Unity 5 can still fall back to "UI Charms".
    /// The original cursor, grid roots, native Sprite/detail widgets and native
    /// visual/audio actions are reused instead of drawing an overlay.
    /// </summary>
    internal sealed class CharmPageController : IDisposable
    {
        private const int PageCount = 4;
        private const float PageFadeOutSeconds = 0.08f;
        private const float PageFadeInSeconds = 0.11f;
        private const float SpecialFormHoldSeconds = 0.70f;

        // Legacy fallback used by Unity 5. Unity 6 resolves a position above the live grid.
        private static readonly Vector3 PageSelectorPosition = new Vector3(0.6f, 1.4f, -3.33f);
        private static readonly Vector2 PageSelectorColliderSize = new Vector2(1.3f, 1.3f);

        private sealed class MarkerState
        {
            public GameObject GameObject;
            public bool OriginalActive;
        }

        private sealed class RendererState
        {
            public SpriteRenderer Renderer;
            public bool OriginalEnabled;
            public Material OriginalMaterial;
            public int OriginalSortingLayerId;
            public int OriginalSortingOrder;
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
            public Color PageColor;
            public bool OriginalEnabled;
            public bool OriginalRootActive;
            public Material OriginalMaterial;
            public int OriginalSortingLayerId;
            public int OriginalSortingOrder;
            public readonly List<MarkerState> EquippedMarkers = new List<MarkerState>();
            public readonly List<MarkerState> LockedMarkers = new List<MarkerState>();
            public readonly List<RendererState> AuxiliaryRenderers = new List<RendererState>();

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
        private readonly Dictionary<int, Sprite> _nativeSpriteFallbacks = new Dictionary<int, Sprite>();
        private bool _nativeSpriteScanComplete;

        private GameObject _pane;
        private GameObject _gridRoot;
        private Component _uiCharmsFsm;
        private Component _updateCursorFsm;
        private Component _nameText;
        private Component _descriptionText;
        private SpriteRenderer _detailIcon;
        private object _fadeGroup;

        private GameObject _pageSelector;
        private SpriteRenderer _pageSelectorRenderer;
        private Vector3 _resolvedPageSelectorPosition = PageSelectorPosition;
        private Component _pageSelectorCollider;
        private readonly Sprite[] _pageSelectorSprites = new Sprite[PageCount];
        private bool _pageSelectorSelected;
        private Slot _lastGridSlot;
        private GameObject _lastEquipmentTarget;
        private SpriteRenderer _nativeIconRendererTemplate;
        private float _nextSelectorCursorRefresh;

        private string _originalName = string.Empty;
        private string _originalDescription = string.Empty;
        private Sprite _originalDetailSprite;
        private Color _originalDetailColor;
        private bool _originalDetailEnabled;
        private bool _detailSnapshotValid;
        private bool _descriptionTypographyCaptured;
        private float _originalDescriptionFontSize;
        private float _originalDescriptionFontSizeMin;
        private float _originalDescriptionFontSizeMax;
        private bool _originalDescriptionAutoSizing;

        // Slot 36 and 40 each expose one additional form per custom page.
        // false = Void Heart / Grimmchild, true = Kingsoul / Carefree Melody.
        private readonly bool[] _showKingsoul = new bool[3];
        private readonly bool[] _showCarefreeMelody = new bool[3];
        private bool _specialFormHoldActive;
        private bool _specialFormHoldTriggered;
        private float _specialFormHoldStartedAt;
        private int _specialFormHoldStartedFrame;
        private Slot _specialFormHoldSlot;
        private bool _loggedSubmitProbeFallback;

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
        private bool _loggedMissingNavigationFsm;
        private bool _loggedGridDiagnostics;
        private float _nextGridWarningAt;
        private Color _nativeUnequippedColor = Color.white;
        private Color _nativeEquippedColor = new Color(0.42f, 0.42f, 0.42f, 1f);
        private bool _hasNativeUnequippedColor;
        private bool _hasNativeEquippedColor;
        private const string VanillaDescriptionMarker = "\n\n【Charms Evolve】";

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

            if (_pane == null || _gridRoot == null)
            {
                ResetBuild();
                return;
            }

            if (!_gridRoot.activeInHierarchy)
            {
                CancelSpecialFormHold(false);
                return;
            }

            UpdateSpecialFormHold();

            // Unity 5 could shift the selector by +100 Y. Unity 6 uses a different
            // hierarchy, so constrain it to the position resolved from the live grid.
            if (_pageSelector != null &&
                (_pageSelector.transform.position - _resolvedPageSelectorPosition).sqrMagnitude > 0.0001f)
                _pageSelector.transform.position = _resolvedPageSelectorPosition;

            // Unity 6's Idle Equipped state can rewrite the Update Cursor "Item" variable
            // after a foreign selectable object is chosen. While the selector owns focus,
            // gently reassert it instead of letting the native FSM silently snap away.
            if (_pageSelectorSelected && _pageSelector != null &&
                Time.unscaledTime >= _nextSelectorCursorRefresh &&
                !IsPageSelectorObject(GetSelectedObject()))
            {
                _nextSelectorCursorRefresh = Time.unscaledTime + 0.08f;
                SetCursorItem(_pageSelector);
            }

            // Do not reset the selected charm page when the player visits the inventory's
            // equipment or journal panes. Keeping it makes the page feel integrated with the
            // original left/right inventory arrows.
            if (_page > 0 && !_transitioning && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.10f;
                ApplyPageVisuals();
            }
        }

        public void LateTick()
        {
            if (!_built || _pane == null || _gridRoot == null || !_gridRoot.activeInHierarchy)
                return;

            if (_transitioning)
                return;

            Slot selected = GetSelectedSlot();
            if (selected == null)
                return;

            _lastGridSlot = selected;
            if (_page > 0)
                UpdateDetails(selected);
            else
                UpdateVanillaDetails(selected);
        }

        public void Dispose()
        {
            _disposed = true;
            CancelSpecialFormHold(false);
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
            if (!_built || _pane == null || _gridRoot == null || !_gridRoot.activeInHierarchy)
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

            if (_specialFormHoldActive &&
                (eventName.IndexOf("LEFT", StringComparison.Ordinal) >= 0 ||
                 eventName.IndexOf("RIGHT", StringComparison.Ordinal) >= 0 ||
                 eventName.IndexOf("UP", StringComparison.Ordinal) >= 0 ||
                 eventName.IndexOf("DOWN", StringComparison.Ordinal) >= 0))
                CancelSpecialFormHold(false);

            GameObject eventOwnerObject = CharmUtil.GetOwnerGameObject(eventOwner);
            GameObject selectedObject = GetSelectedObject();
            bool onSelector = _pageSelectorSelected ||
                              IsPageSelectorObject(selectedObject) ||
                              IsPageSelectorObject(eventOwnerObject);
            if (onSelector)
                _pageSelectorSelected = true;
            Slot selected = GetSelectedSlot();
            if (selected == null)
                selected = GetSlotFromEventOwner(eventOwner);
            if (selected != null)
                _lastGridSlot = selected;
            string stateName = CharmUtil.GetActiveStateName(_uiCharmsFsm ?? eventOwner);

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

            // Match CharmPreset's vertical bridge. From the collection's top row, UP
            // enters the page selector. From the equipped strip, DOWN enters it only
            // when the cursor has reached the right-most equipped charm.
            if (selected != null && selected.Row == 0 &&
                (IsEvent(eventName, "UI UP") || IsEvent(eventName, "UI RS UP")))
            {
                MarkConsumed(eventName);
                SelectPageSelector(selected);
                return true;
            }

            bool downFromEquipment =
                IsEvent(eventName, "UI DOWN") || IsEvent(eventName, "UI RS DOWN");
            if (downFromEquipment &&
                (IsEquipmentSelection(selectedObject) || StateContains(stateName, "Equipped")) &&
                IsRightmostEquipmentSelection(selectedObject))
            {
                _lastEquipmentTarget = ResolveEquipmentSelectable(selectedObject) ??
                                       FindRightmostEquipmentTarget();
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
                if (IsSpecialFormSlot(selected))
                    BeginSpecialFormHold(selected);
                else
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
            if (_pageSelectorSelected)
                return true;

            GameObject selected = GetSelectedObject();
            if (IsPageSelectorObject(selected))
                _pageSelectorSelected = true;
            return _pageSelectorSelected;
        }

        private bool IsPageSelectorObject(GameObject candidate)
        {
            if (candidate == null || _pageSelector == null)
                return false;
            return candidate == _pageSelector ||
                   candidate.transform.IsChildOf(_pageSelector.transform) ||
                   _pageSelector.transform.IsChildOf(candidate.transform);
        }

        private void SelectPageSelector(Slot fromSlot)
        {
            if (_pageSelector == null)
                return;

            if (fromSlot != null)
                _lastGridSlot = fromSlot;

            _pageSelectorSelected = true;
            _nextSelectorCursorRefresh = 0f;
            SetCursorItem(_pageSelector);
        }

        private void LeaveSelectorToGrid()
        {
            _pageSelectorSelected = false;

            // Unity 6 no longer exposes CharmPreset's old "Idle Collection" state.
            // Enter the closest known collection state when available, then explicitly
            // set the cursor item so the transition does not depend on cloned FSM actions.
            CharmUtil.TrySetFsmState(_uiCharmsFsm, "Charm");
            Slot fallback = FindGridEntryBelowSelector();
            if (fallback == null)
                fallback = _lastGridSlot;
            if (fallback == null && _slots.Count > 0)
                fallback = _slots[0];
            if (fallback != null)
            {
                _lastGridSlot = fallback;
                SetCursorItem(fallback.Root);
            }
        }

        private void LeaveSelectorToEquipment()
        {
            _pageSelectorSelected = false;

            CharmUtil.TrySetFsmState(_uiCharmsFsm, "Idle Equipped");
            GameObject fallback = _lastEquipmentTarget;
            if (fallback == null || !fallback.activeInHierarchy)
                fallback = FindRightmostEquipmentTarget();
            if (fallback != null)
            {
                _lastEquipmentTarget = fallback;
                SetCursorItem(fallback);
            }
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

        private bool IsSpecialFormSlot(Slot slot)
        {
            return slot != null && _page > 0 &&
                   (slot.OriginalId == 36 || slot.OriginalId == 40);
        }

        private void BeginSpecialFormHold(Slot slot)
        {
            if (!IsSpecialFormSlot(slot) || _transitioning)
                return;

            if (_specialFormHoldActive && _specialFormHoldSlot == slot)
                return;

            CancelSpecialFormHold(false);
            _specialFormHoldActive = true;
            _specialFormHoldTriggered = false;
            _specialFormHoldStartedAt = Time.unscaledTime;
            _specialFormHoldStartedFrame = Time.frameCount;
            _specialFormHoldSlot = slot;
        }

        private void UpdateSpecialFormHold()
        {
            if (!_specialFormHoldActive || _specialFormHoldSlot == null)
                return;
            if (_page <= 0 || _transitioning || GetSelectedSlot() != _specialFormHoldSlot)
            {
                CancelSpecialFormHold(false);
                return;
            }

            // Give the input system one frame after UI CONFIRM before testing release.
            if (Time.frameCount <= _specialFormHoldStartedFrame + 1)
                return;

            bool sourceResolved;
            bool pressed = GameReflection.IsMenuSubmitPressed(out sourceResolved);
            if (!sourceResolved && !_loggedSubmitProbeFallback)
            {
                _loggedSubmitProbeFallback = true;
                Plugin.Log.LogWarning(
                    "Menu-submit hold source was not resolved. Tap CONFIRM still works, but the 36/40 long-hold form switch needs an InputHandler member dump from UnityExplorer.");
            }

            if (pressed)
            {
                if (!_specialFormHoldTriggered &&
                    Time.unscaledTime - _specialFormHoldStartedAt >= SpecialFormHoldSeconds)
                {
                    _specialFormHoldTriggered = true;
                    TriggerNativeFormSwapFeedback(_specialFormHoldSlot);
                    SwapSpecialForm(_specialFormHoldSlot);
                }
                return;
            }

            Slot releasedSlot = _specialFormHoldSlot;
            bool triggered = _specialFormHoldTriggered;
            CancelSpecialFormHold(false);
            if (!triggered && releasedSlot != null)
                ConfirmSelected(releasedSlot);
        }

        private void CancelSpecialFormHold(bool confirmTap)
        {
            Slot slot = _specialFormHoldSlot;
            bool triggered = _specialFormHoldTriggered;
            _specialFormHoldActive = false;
            _specialFormHoldTriggered = false;
            _specialFormHoldStartedAt = 0f;
            _specialFormHoldStartedFrame = 0;
            _specialFormHoldSlot = null;

            if (confirmTap && !triggered && slot != null)
                ConfirmSelected(slot);
        }

        private void SwapSpecialForm(Slot slot)
        {
            if (!IsSpecialFormSlot(slot))
                return;

            CopyCharmDefinition oldDefinition = GetDefinition(slot);
            int pageIndex = _page - 1;
            string target;
            if (slot.OriginalId == 36)
            {
                _showKingsoul[pageIndex] = !_showKingsoul[pageIndex];
                target = _showKingsoul[pageIndex] ? "国王之魂" : "虚空之心";
            }
            else
            {
                _showCarefreeMelody[pageIndex] = !_showCarefreeMelody[pageIndex];
                target = _showCarefreeMelody[pageIndex] ? "无忧旋律" : "格林之子";
            }

            CopyCharmDefinition newDefinition = GetDefinition(slot);
            string reason;
            if (oldDefinition != null && newDefinition != null &&
                !_state.SwapEquippedForm(oldDefinition.Key, newDefinition.Key, out reason))
            {
                if (slot.OriginalId == 36)
                    _showKingsoul[pageIndex] = !_showKingsoul[pageIndex];
                else
                    _showCarefreeMelody[pageIndex] = !_showCarefreeMelody[pageIndex];

                _status = reason;
                _statusUntil = Time.unscaledTime + 2.5f;
                ReplayNativeReminderFeedback(reason);
                ApplySlotVisual(slot);
                UpdateDetails(slot);
                Plugin.Log.LogWarning("Custom charm form switch rejected: " + reason);
                return;
            }

            _status = "已切换为" + target + "。";
            _statusUntil = Time.unscaledTime + 2.5f;
            ApplySlotVisual(slot);
            UpdateDetails(slot);
            Plugin.Log.LogInfo("Custom charm form switched on page " + _page +
                ", slot " + slot.OriginalId + " -> " + target + ".");
        }

        private void TriggerNativeFormSwapFeedback(Slot slot)
        {
            if (slot == null)
                return;

            string[] stateCandidates =
            {
                "Overcharm", "Overcharmed", "Over Charm", "Charm Overload",
                "Bound Reminder", "Notches Full", "Full", "Shake"
            };
            bool replayed = false;
            for (int i = 0; i < stateCandidates.Length && !replayed; i++)
                replayed = TryInvokeNativeFeedbackActions(stateCandidates[i]);
            if (!replayed)
                replayed = TryInvokeNativeFeedbackStateByFragments(
                    new[] { "overcharm", "over charm", "overload", "bound", "shake", "full" });

            Component[] components = slot.Root == null
                ? new Component[0]
                : slot.Root.GetComponentsInChildren<Component>(true);
            string[] events = { "OVERCHARM", "OVERCHARMED", "SHAKE", "BOUND", "INVALID" };
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                    continue;
                for (int j = 0; j < events.Length; j++)
                    replayed |= CharmUtil.SendFsmEvent(component, events[j]);
            }

            if (!replayed)
            {
                TryInvokeNativeAudioAction("Bound Reminder", 0);
                Plugin.Log.LogWarning(
                    "Native overcharm shake/audio state was not resolved for the 36/40 long-hold form switch. See the emitted FSM diagnostic list.");
            }
        }

        private void RequestPage(int page)
        {
            CancelSpecialFormHold(false);
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

                Color baseColor = slot.PageColor;
                slot.Icon.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            }
        }

        private void TryBuild()
        {
            if (Time.unscaledTime < _nextBuildAttempt)
                return;
            _nextBuildAttempt = Time.unscaledTime + 0.75f;

            GameObject grid = CharmUtil.CharmsGrid;
            GameObject pane = CharmUtil.CharmsPane;
            if (grid == null && pane == null)
                return;

            _gridRoot = grid ?? pane;
            _pane = pane ?? _gridRoot;
            _uiCharmsFsm = CharmUtil.UiCharmsFsm;
            _updateCursorFsm = FindFsmAround(_pane, "Update Cursor");
            _fadeGroup = FindComponentByTypeNameAround(_pane, "FadeGroup");

            if (!BuildSlots())
            {
                if (Time.unscaledTime >= _nextGridWarningAt)
                {
                    _nextGridWarningAt = Time.unscaledTime + 10f;
                    Plugin.Log.LogWarning(
                        "Charm pane was resolved, but fewer than 40 live collection slots were found. Unity 6 CharmItem diagnostics follow.");
                    LogGridProbeDiagnostics();
                }
                ResetBuildObjectsOnly();
                return;
            }

            ResolveDetailPanel();
            ResolveNativeIconRendererTemplate();
            CaptureDescriptionTypography();
            _resolvedPageSelectorPosition = ResolvePageSelectorPosition();
            BuildPageSelector();
            EnforceNativeRendererSettings();

            if (_uiCharmsFsm == null && !_loggedMissingNavigationFsm)
            {
                _loggedMissingNavigationFsm = true;
                Plugin.Log.LogWarning(
                    "The Unity 6 charm grid was found without a single global charm-navigation FSM. Slot-local FSM events and the Update Cursor FSM will be used as fallbacks.");
            }
            if (_nameText == null || _descriptionText == null)
                Plugin.Log.LogWarning("Native charm detail text was not fully resolved; current-game object names may differ.");
            if (_detailIcon == null)
                Plugin.Log.LogWarning("Native charm detail SpriteRenderer was not resolved.");

            _built = true;
            UpdatePageSelectorSprite();
            Plugin.Log.LogInfo(
                "Native charm pager ready. Grid slots=" + _slots.Count +
                ", grid=" + CharmUtil.GetHierarchyPath(_gridRoot) +
                ", pane=" + CharmUtil.GetHierarchyPath(_pane) +
                ", navigationFSM=" + (_uiCharmsFsm == null ? "<none>" : CharmUtil.GetFsmName(_uiCharmsFsm)) + ".");
            LogGridProbeDiagnostics();
        }

        private bool BuildSlots()
        {
            _slots.Clear();
            _slotByOriginalId.Clear();

            Dictionary<int, Slot> bestSlots = new Dictionary<int, Slot>();
            Dictionary<int, int> bestScores = new Dictionary<int, int>();

            Component[] charmItems = CharmUtil.FindAllCharmItems();
            for (int i = 0; i < charmItems.Length; i++)
            {
                Component item = charmItems[i];
                int id;
                if (!CharmUtil.TryGetCharmItemId(item, out id) || id < 1 || id > 40)
                    continue;
                if (!IsLiveCollectionRoot(item.gameObject))
                    continue;

                TryAddSlotCandidate(bestSlots, bestScores, id, item.gameObject, item, 500);
            }

            // Unity 6 can deactivate CharmItem while retaining the numbered slot root.
            // Enumerate those roots directly so Resources/active-state differences do not
            // reduce the collection to only the currently visible or equipped items.
            if (_gridRoot != null)
            {
                Transform[] transforms = _gridRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null || transform.gameObject == null)
                        continue;

                    int id;
                    if (!TryResolveSlotId(transform.gameObject, out id) || id < 1 || id > 40)
                        continue;
                    if (!IsLiveCollectionRoot(transform.gameObject))
                        continue;

                    Component charmItem = FindDirectComponentByTypeName(transform.gameObject, "CharmItem");
                    TryAddSlotCandidate(bestSlots, bestScores, id, transform.gameObject, charmItem, 300);
                }
            }

            // Unity 5 fallback: old builds have no CharmItem component and identify the
            // collection through inventory Sprite names.
            AddLegacySpriteCandidates(bestSlots, bestScores);

            for (int id = 1; id <= 40; id++)
            {
                Slot slot;
                if (!bestSlots.TryGetValue(id, out slot) || slot == null)
                    continue;

                _slots.Add(slot);
                _slotByOriginalId[id] = slot;
            }

            if (_slots.Count < 40)
                return false;

            AssignGridCoordinates();
            CaptureNativeCharmPalette();
            return true;
        }

        private void TryAddSlotCandidate(
            Dictionary<int, Slot> bestSlots,
            Dictionary<int, int> bestScores,
            int id,
            GameObject root,
            Component charmItem,
            int baseScore)
        {
            if (root == null)
                return;

            SpriteRenderer icon = ResolveCharmItemIcon(root, charmItem, id);
            if (icon == null)
                return;

            int score = baseScore + ScoreSlotRoot(root, icon, charmItem);
            int previous;
            if (bestScores.TryGetValue(id, out previous) && previous >= score)
                return;

            Slot slot = new Slot
            {
                OriginalId = id,
                Root = root,
                Icon = icon,
                OriginalSprite = icon.sprite,
                OriginalColor = icon.color,
                OriginalEnabled = icon.enabled,
                OriginalRootActive = root.activeSelf,
                OriginalMaterial = icon.sharedMaterial,
                OriginalSortingLayerId = icon.sortingLayerID,
                OriginalSortingOrder = icon.sortingOrder
            };
            CaptureMarkers(slot);
            slot.PageColor = slot.OriginalColor;
            bestScores[id] = score;
            bestSlots[id] = slot;
        }

        private bool IsLiveCollectionRoot(GameObject gameObject)
        {
            if (gameObject == null || _gridRoot == null)
                return false;
            if (gameObject != _gridRoot && !gameObject.transform.IsChildOf(_gridRoot.transform))
                return false;

            Transform current = gameObject.transform;
            Transform boundary = _pane == null ? null : _pane.transform.parent;
            while (current != null && current != boundary)
            {
                if (string.Equals(current.gameObject.name, "Equipped Charms", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (current == _gridRoot.transform)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static Component FindDirectComponentByTypeName(GameObject root, string typeName)
        {
            if (root == null)
                return null;

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                    return component;
            }
            return null;
        }

        private static SpriteRenderer ResolveCharmItemIcon(GameObject root, Component charmItem, int expectedId)
        {
            if (root == null)
                return null;

            List<SpriteRenderer> candidates = new List<SpriteRenderer>();
            HashSet<SpriteRenderer> charmItemReferences = new HashSet<SpriteRenderer>();
            AddRendererCandidate(candidates, root.GetComponent<SpriteRenderer>());

            string[] members =
            {
                "spriteRenderer", "SpriteRenderer", "renderer", "Renderer",
                "icon", "Icon", "charmRenderer", "CharmRenderer", "display", "Display"
            };
            if (charmItem != null)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    object value;
                    if (!CharmUtil.TryGetMember(charmItem, members[i], out value) || value == null)
                        continue;

                    SpriteRenderer renderer = value as SpriteRenderer;
                    Component component = value as Component;
                    if (renderer == null && component != null)
                        renderer = component.GetComponent<SpriteRenderer>();
                    if (renderer == null)
                        continue;

                    AddRendererCandidate(candidates, renderer);
                    charmItemReferences.Add(renderer);
                }
            }

            SpriteRenderer[] descendants = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < descendants.Length; i++)
                AddRendererCandidate(candidates, descendants[i]);

            SpriteRenderer best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                SpriteRenderer renderer = candidates[i];
                if (renderer == null || IsMarkerRenderer(renderer) ||
                    IsBlurOrEffectName(renderer.gameObject.name) ||
                    IsBlurOrEffectName(renderer.sprite == null ? null : renderer.sprite.name))
                    continue;

                int score = ScoreCharmRendererCandidate(
                    renderer, root, expectedId, charmItemReferences.Contains(renderer));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = renderer;
                }
            }
            return best;
        }

        private static void AddRendererCandidate(List<SpriteRenderer> candidates, SpriteRenderer renderer)
        {
            if (renderer != null && !candidates.Contains(renderer))
                candidates.Add(renderer);
        }

        private static int ScoreCharmRendererCandidate(
            SpriteRenderer renderer, GameObject root, int expectedId, bool referencedByCharmItem)
        {
            int score = referencedByCharmItem ? 300 : 0;
            if (renderer.gameObject == root)
                score += 180;

            string objectName = renderer.gameObject.name ?? string.Empty;
            string spriteName = renderer.sprite == null ? string.Empty : renderer.sprite.name ?? string.Empty;
            if (renderer.sprite != null)
            {
                score += 100;
                int spriteId;
                if (TryParseCharmSpriteId(spriteName, out spriteId))
                {
                    if (expectedId > 0 && spriteId == expectedId)
                        score += 1000;
                    else if (expectedId > 0)
                        score -= 500;
                }
                if (spriteName.StartsWith("Inv_", StringComparison.OrdinalIgnoreCase))
                    score += 200;
            }

            if (objectName.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 70;
            if (IsBlurOrEffectName(objectName) || IsBlurOrEffectName(spriteName))
                score -= 1200;
            if (string.Equals(renderer.sortingLayerName, "HUD", StringComparison.OrdinalIgnoreCase))
                score += 20;
            return score;
        }

        private static bool IsMarkerRenderer(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.gameObject == null)
                return false;
            string name = renderer.gameObject.name ?? string.Empty;
            return name.IndexOf("equipped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("unowned", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("cursor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("highlight", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ScoreSlotRoot(GameObject root, SpriteRenderer icon, Component charmItem)
        {
            int score = 0;
            if (root.activeInHierarchy)
                score += 80;
            if (root.transform.parent != null &&
                string.Equals(root.transform.parent.gameObject.name, "Charms", StringComparison.OrdinalIgnoreCase))
                score += 100;
            if (charmItem != null)
                score += 100;
            if (icon != null && icon.sprite != null)
                score += 40;

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;
                string typeName = component.GetType().Name;
                if (typeName.IndexOf("Collider2D", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 30;
                if (string.Equals(typeName, "PlayMakerFSM", StringComparison.Ordinal))
                    score += 20;
            }
            return score;
        }

        private void AddLegacySpriteCandidates(
            Dictionary<int, Slot> bestSlots,
            Dictionary<int, int> bestScores)
        {
            GameObject searchRoot = _gridRoot ?? _pane;
            if (searchRoot == null)
                return;

            SpriteRenderer[] renderers = searchRoot.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer.sprite == null)
                    continue;

                int id;
                if (!TryParseCharmSpriteId(renderer.sprite.name, out id) || id < 1 || id > 40)
                    continue;

                GameObject root = FindSelectableRoot(renderer.gameObject, searchRoot.transform);
                TryAddSlotCandidate(bestSlots, bestScores, id, root, null, 100);
            }
        }

        private static bool TryParseCharmSpriteId(string name, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(name))
                return false;

            Match match = Regex.Match(
                name,
                @"(?:^|[_\s-])charm[_\s-]*0*(\d+)(?:$|[_\s-])",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(name, @"(?:^|_)charm0*(\d+)(?:$|_)", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out id);
        }

        private static bool TryResolveSlotId(GameObject candidate, out int id)
        {
            id = 0;
            if (candidate == null)
                return false;

            if (int.TryParse(candidate.name, out id) && id >= 1 && id <= 40)
                return true;

            Component charmItem = FindDirectComponentByTypeName(candidate, "CharmItem");
            if (charmItem != null && CharmUtil.TryGetCharmItemId(charmItem, out id) && id >= 1 && id <= 40)
                return true;

            string name = candidate.name ?? string.Empty;
            bool directCollectionChild = candidate.transform.parent != null &&
                string.Equals(candidate.transform.parent.gameObject.name, "Collected Charms", StringComparison.OrdinalIgnoreCase);
            bool slotNamed = directCollectionChild ||
                name.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0;

            if (slotNamed)
            {
                MatchCollection matches = Regex.Matches(name, @"(?<!\d)0*(40|[1-3]\d|[1-9])(?!\d)");
                for (int i = 0; i < matches.Count; i++)
                {
                    if (int.TryParse(matches[i].Groups[1].Value, out id) && id >= 1 && id <= 40)
                        return true;
                }
            }

            SpriteRenderer renderer = candidate.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null &&
                TryParseCharmSpriteId(renderer.sprite.name, out id) && id >= 1 && id <= 40)
                return true;

            return false;
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
                        string.Equals(typeName, "PlayMakerFSM", StringComparison.Ordinal) ||
                        string.Equals(typeName, "CharmItem", StringComparison.OrdinalIgnoreCase))
                        best = current.gameObject;
                }
                current = current.parent;
            }
            return best;
        }

        private void LogGridProbeDiagnostics()
        {
            if (_loggedGridDiagnostics && _built)
                return;
            _loggedGridDiagnostics = true;

            Component[] items = CharmUtil.FindAllCharmItems();
            List<string> samples = new List<string>();
            int collectionCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                Component item = items[i];
                int id;
                if (CharmUtil.TryGetCharmItemId(item, out id) && id >= 1 && id <= 42 &&
                    item != null && item.gameObject != null)
                {
                    if (_gridRoot != null &&
                        (item.gameObject == _gridRoot || item.transform.IsChildOf(_gridRoot.transform)))
                        collectionCount++;

                    if (samples.Count < 20)
                    {
                        SpriteRenderer renderer = ResolveCharmItemIcon(item.gameObject, item, id);
                        samples.Add(id + "@" + CharmUtil.GetHierarchyPath(item.gameObject) +
                            " scene=" + CharmUtil.IsLiveSceneObject(item.gameObject) +
                            " active=" + item.gameObject.activeInHierarchy +
                            " sprite=" + (renderer == null || renderer.sprite == null ? "<none>" : renderer.sprite.name));
                    }
                }
            }

            List<int> missing = new List<int>();
            for (int id = 1; id <= 40; id++)
            {
                if (!_slotByOriginalId.ContainsKey(id))
                    missing.Add(id);
            }

            int descendantCount = 0;
            int directChildCount = 0;
            HashSet<int> namedIds = new HashSet<int>();
            List<string> directChildren = new List<string>();
            if (_gridRoot != null)
            {
                Transform root = _gridRoot.transform;
                directChildCount = root.childCount;
                Transform[] transforms = _gridRoot.GetComponentsInChildren<Transform>(true);
                descendantCount = transforms.Length;
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    int id;
                    if (transform != null && TryResolveSlotId(transform.gameObject, out id) && id >= 1 && id <= 40)
                        namedIds.Add(id);
                }

                for (int i = 0; i < root.childCount && directChildren.Count < 50; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null)
                        continue;
                    int id;
                    bool hasId = TryResolveSlotId(child.gameObject, out id);
                    directChildren.Add(child.gameObject.name +
                        (hasId ? "=>" + id : "") +
                        "[active=" + child.gameObject.activeInHierarchy +
                        ",children=" + child.childCount + "]");
                }
            }

            Plugin.Log.LogInfo(
                "Charm grid probe: grid=" + CharmUtil.GetHierarchyPath(_gridRoot) +
                ", pane=" + CharmUtil.GetHierarchyPath(_pane) +
                ", directChildren=" + directChildCount +
                ", descendants=" + descendantCount +
                ", recognizableIds=" + namedIds.Count +
                (namedIds.Count == 0 ? "" : " [" + string.Join(",", new List<int>(namedIds).ConvertAll(delegate(int value) { return value.ToString(); }).ToArray()) + "]") + ".");
            if (directChildren.Count > 0)
                Plugin.Log.LogInfo("Charm grid direct children: " + string.Join(" | ", directChildren.ToArray()));

            Plugin.Log.LogInfo(
                "CharmItem probe: resources=" + items.Length +
                ", under selected Charms root=" + collectionCount +
                ", resolved slots=" + _slots.Count +
                ", missing=" + (missing.Count == 0 ? "<none>" : string.Join(",", missing.ConvertAll(delegate(int value) { return value.ToString(); }).ToArray())) + ".");
            for (int i = 0; i < samples.Count; i++)
                Plugin.Log.LogInfo("CharmItem sample: " + samples[i]);

            LogNativeFeedbackDiagnostics();
        }

        private void LogNativeFeedbackDiagnostics()
        {
            List<Component> fsms = GetFeedbackFsms();
            List<string> fsmSummaries = new List<string>();
            for (int i = 0; i < fsms.Count && fsmSummaries.Count < 16; i++)
            {
                Component fsm = fsms[i];
                if (fsm == null)
                    continue;

                List<string> states = GetStateNames(fsm);
                List<string> relevant = new List<string>();
                for (int j = 0; j < states.Count; j++)
                {
                    string state = states[j];
                    if (state.IndexOf("equip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("over", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("shake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("bound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("tween", StringComparison.OrdinalIgnoreCase) >= 0)
                        relevant.Add(state);
                }

                if (relevant.Count > 0 || CharmUtil.GetFsmName(fsm).IndexOf("Charm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fsmSummaries.Add(
                        CharmUtil.GetHierarchyPath(fsm.gameObject) + " :: " +
                        CharmUtil.GetFsmName(fsm) + " [" +
                        (relevant.Count == 0 ? "no matching states" : string.Join(",", relevant.ToArray())) + "]");
                }
            }

            Plugin.Log.LogInfo("Charm UI FSM probe: candidates=" + fsms.Count +
                ", relevant=" + fsmSummaries.Count + ".");
            for (int i = 0; i < fsmSummaries.Count; i++)
                Plugin.Log.LogInfo("Charm UI FSM: " + fsmSummaries[i]);

            Slot sampleSlot = _slots.Count > 0 ? _slots[0] : null;
            if (sampleSlot == null || sampleSlot.Root == null)
                return;

            Component[] components = sampleSlot.Root.GetComponentsInChildren<Component>(true);
            List<string> methods = new List<string>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;
                string typeName = component.GetType().Name;
                if (!string.Equals(typeName, "CharmItem", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(typeName, "CharmDisplay", StringComparison.OrdinalIgnoreCase))
                    continue;

                MethodInfo[] candidates = component.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int j = 0; j < candidates.Length; j++)
                {
                    MethodInfo method = candidates[j];
                    string name = method.Name ?? string.Empty;
                    if (method.GetParameters().Length == 0 &&
                        (name.IndexOf("equip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("anim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("select", StringComparison.OrdinalIgnoreCase) >= 0))
                        methods.Add(typeName + "." + name);
                }
            }
            if (methods.Count > 0)
                Plugin.Log.LogInfo("CharmItem/CharmDisplay visual-method probe: " + string.Join(",", methods.ToArray()));
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
                    continue;
                }

                if (name.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("unowned", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slot.LockedMarkers.Add(new MarkerState
                    {
                        GameObject = renderer.gameObject,
                        OriginalActive = renderer.gameObject.activeSelf
                    });
                    continue;
                }

                // Unity 6 slot roots contain extra children such as "Royal Charm".
                // Their glow/blur renderers inherit activeSelf=true while the parent is
                // inactive; activating the root for a copied charm therefore reveals
                // those effects unless they are explicitly suppressed.
                slot.AuxiliaryRenderers.Add(new RendererState
                {
                    Renderer = renderer,
                    OriginalEnabled = renderer.enabled,
                    OriginalMaterial = renderer.sharedMaterial,
                    OriginalSortingLayerId = renderer.sortingLayerID,
                    OriginalSortingOrder = renderer.sortingOrder
                });
            }
        }

        private void CaptureNativeGrid()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Root != null)
                    slot.OriginalRootActive = slot.Root.activeSelf;
                if (slot.Icon != null)
                {
                    slot.OriginalSprite = slot.Icon.sprite;
                    if (IsUsableInventoryCharmSprite(slot.OriginalSprite, slot.OriginalId))
                        _nativeSpriteFallbacks[slot.OriginalId] = slot.OriginalSprite;
                    slot.OriginalColor = slot.Icon.color;
                    slot.PageColor = slot.OriginalColor;
                    slot.OriginalEnabled = slot.Icon.enabled;
                    slot.OriginalMaterial = slot.Icon.sharedMaterial;
                    slot.OriginalSortingLayerId = slot.Icon.sortingLayerID;
                    slot.OriginalSortingOrder = slot.Icon.sortingOrder;
                }

                CaptureMarkerActivity(slot.EquippedMarkers);
                CaptureMarkerActivity(slot.LockedMarkers);
                CaptureRendererActivity(slot.AuxiliaryRenderers);
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

        private static void CaptureRendererActivity(List<RendererState> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                RendererState state = renderers[i];
                if (state.Renderer == null)
                    continue;
                state.OriginalEnabled = state.Renderer.enabled;
                state.OriginalMaterial = state.Renderer.sharedMaterial;
                state.OriginalSortingLayerId = state.Renderer.sortingLayerID;
                state.OriginalSortingOrder = state.Renderer.sortingOrder;
            }
        }

        private void CaptureNativeCharmPalette()
        {
            _hasNativeUnequippedColor = false;
            _hasNativeEquippedColor = false;

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot == null || slot.Icon == null)
                    continue;

                if (GameReflection.IsVanillaCharmEquipped(slot.OriginalId))
                {
                    if (!_hasNativeEquippedColor)
                    {
                        _nativeEquippedColor = slot.Icon.color;
                        _hasNativeEquippedColor = true;
                    }
                }
                else if (!_hasNativeUnequippedColor)
                {
                    _nativeUnequippedColor = slot.Icon.color;
                    _hasNativeUnequippedColor = true;
                }
            }

            if (!_hasNativeUnequippedColor && _slots.Count > 0 && _slots[0].Icon != null)
            {
                _nativeUnequippedColor = _slots[0].Icon.color;
                _hasNativeUnequippedColor = true;
            }

            if (!_hasNativeEquippedColor)
            {
                Color source = _nativeUnequippedColor;
                _nativeEquippedColor = new Color(source.r * 0.42f, source.g * 0.42f, source.b * 0.42f, source.a);
                Plugin.Log.LogWarning("No equipped vanilla charm was visible while building the pager; using a native-color-derived dim fallback until the pane is rebuilt with an equipped charm.");
            }
            else
            {
                Plugin.Log.LogInfo("Captured native equipped charm tint from the original charm collection.");
            }
        }

        private Color ResolveNativeUnequippedColor(Slot slot)
        {
            return _hasNativeUnequippedColor ? _nativeUnequippedColor : slot.OriginalColor;
        }

        private Color ResolveNativeEquippedColor(Slot slot)
        {
            if (_hasNativeEquippedColor)
                return _nativeEquippedColor;

            Color source = ResolveNativeUnequippedColor(slot);
            return new Color(source.r * 0.42f, source.g * 0.42f, source.b * 0.42f, source.a);
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

            bool owned = _state.IsOwned(definition.Key);
            bool equipped = _state.IsEquipped(definition.Key);

            // Unity 6 keeps uncollected vanilla slot roots inactive. Enabling only the
            // SpriteRenderer cannot make a copied charm visible, so custom pages own
            // the slot root's active state and restore it on the vanilla page.
            if (slot.Root != null && slot.Root.activeSelf != owned)
                slot.Root.SetActive(owned);

            // Keep only the actual icon plus explicit equipped/locked markers. Unity 6
            // leaves children such as "Royal Charm" armed under inactive roots; without
            // this suppression those children become the large blurred blobs seen in the
            // collection when the root is activated.
            SetAuxiliaryRenderers(slot.AuxiliaryRenderers, false);

            Sprite sprite;
            if (TryResolveCharmVisualSprite(definition.Key, definition.OriginalId, slot, out sprite))
                slot.Icon.sprite = sprite;

            ApplyNativeIconRendererTemplate(slot.Icon);
            slot.Icon.enabled = owned && slot.Icon.sprite != null;
            slot.PageColor = equipped ? ResolveNativeEquippedColor(slot) : ResolveNativeUnequippedColor(slot);
            slot.Icon.color = slot.PageColor;
            SetMarkers(slot.EquippedMarkers, equipped);
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
            slot.PageColor = slot.OriginalColor;
            slot.Icon.color = slot.OriginalColor;
            slot.Icon.enabled = slot.OriginalEnabled;
            slot.Icon.sharedMaterial = slot.OriginalMaterial;
            slot.Icon.sortingLayerID = slot.OriginalSortingLayerId;
            slot.Icon.sortingOrder = slot.OriginalSortingOrder;
            RestoreMarkers(slot.EquippedMarkers);
            RestoreMarkers(slot.LockedMarkers);
            RestoreAuxiliaryRenderers(slot.AuxiliaryRenderers);
            if (slot.Root != null && slot.Root.activeSelf != slot.OriginalRootActive)
                slot.Root.SetActive(slot.OriginalRootActive);
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

        private static void SetAuxiliaryRenderers(List<RendererState> renderers, bool enabled)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                RendererState state = renderers[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = enabled;
            }
        }

        private static void RestoreAuxiliaryRenderers(List<RendererState> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                RendererState state = renderers[i];
                if (state.Renderer == null)
                    continue;
                state.Renderer.enabled = state.OriginalEnabled;
                state.Renderer.sharedMaterial = state.OriginalMaterial;
                state.Renderer.sortingLayerID = state.OriginalSortingLayerId;
                state.Renderer.sortingOrder = state.OriginalSortingOrder;
            }
        }

        private void ApplyNativeIconRendererTemplate(SpriteRenderer renderer)
        {
            if (renderer == null || _nativeIconRendererTemplate == null ||
                renderer == _nativeIconRendererTemplate)
                return;

            // The copied Sprite is valid, but some inactive collection renderers use a
            // glow/blur material. Reuse the material and probe settings from a visible
            // equipped charm, which is known to render sharply in the same HUD.
            renderer.sharedMaterial = _nativeIconRendererTemplate.sharedMaterial;
            renderer.lightProbeUsage = _nativeIconRendererTemplate.lightProbeUsage;
            renderer.reflectionProbeUsage = _nativeIconRendererTemplate.reflectionProbeUsage;
        }

        private CopyCharmDefinition GetDefinition(Slot slot)
        {
            if (slot == null || _page <= 0)
                return null;

            int pageIndex = Mathf.Clamp(_page - 1, 0, 2);
            int definitionId = slot.OriginalId;
            if (definitionId == 36 && _showKingsoul[pageIndex])
                definitionId = 42;
            else if (definitionId == 40 && _showCarefreeMelody[pageIndex])
                definitionId = 41;

            CopyKind kind = (CopyKind)pageIndex;
            return CharmDatabase.GetCopy(CharmKey.For(definitionId, kind));
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

            // Some Unity 6 menu FSMs keep the selection on a slot-local CharmItem
            // without exposing a global Item variable. Preserve the last grid slot
            // only while the dedicated page selector is not selected.
            return !_pageSelectorSelected ? _lastGridSlot : null;
        }

        private Slot GetSlotFromEventOwner(object eventOwner)
        {
            GameObject owner = CharmUtil.GetOwnerGameObject(eventOwner);
            if (owner == null)
                return null;

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot != null && slot.Contains(owner))
                    return slot;
            }

            Transform current = owner.transform;
            for (int depth = 0; current != null && depth < 8; depth++, current = current.parent)
            {
                int id;
                Slot slot;
                if (int.TryParse(current.gameObject.name, out id) &&
                    _slotByOriginalId.TryGetValue(id, out slot))
                    return slot;
            }
            return null;
        }

        private bool IsEquipmentSelection(GameObject selected)
        {
            return FindAncestorNamed(selected, "Equipped Charms") != null;
        }

        private bool IsRightmostEquipmentSelection(GameObject selected)
        {
            GameObject rightmost = FindRightmostEquipmentTarget();
            if (rightmost == null)
                return selected == null;

            GameObject current = ResolveEquipmentSelectable(selected);
            if (current == null)
                return selected == null;
            return current == rightmost ||
                   current.transform.IsChildOf(rightmost.transform) ||
                   rightmost.transform.IsChildOf(current.transform);
        }

        private GameObject ResolveEquipmentSelectable(GameObject selected)
        {
            Transform equipmentRoot = FindAncestorNamed(selected, "Equipped Charms");
            if (equipmentRoot == null || selected == null)
                return null;

            Transform current = selected.transform;
            while (current.parent != null && current.parent != equipmentRoot)
                current = current.parent;
            return current.gameObject;
        }

        private GameObject FindRightmostEquipmentTarget()
        {
            Transform equipmentRoot = FindNamedTransform(
                _pane == null ? null : _pane.transform, "Equipped Charms", 8);
            if (equipmentRoot == null)
                return null;

            GameObject best = null;
            float bestX = float.NegativeInfinity;
            SpriteRenderer[] renderers = equipmentRoot.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer.sprite == null || !renderer.gameObject.activeInHierarchy)
                    continue;

                string name = renderer.gameObject.name ?? string.Empty;
                string spriteName = renderer.sprite.name ?? string.Empty;
                if (name.IndexOf("dot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("notch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("prompt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    spriteName.IndexOf("dot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    spriteName.IndexOf("notch", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                Transform current = renderer.transform;
                while (current.parent != null && current.parent != equipmentRoot)
                    current = current.parent;

                GameObject candidate = current.gameObject;
                if (candidate == _pageSelector)
                    continue;

                float x = candidate.transform.position.x;
                if (x > bestX)
                {
                    bestX = x;
                    best = candidate;
                }
            }

            return best;
        }

        private Slot FindGridEntryBelowSelector()
        {
            Slot best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot == null || slot.Root == null || slot.Row != 0)
                    continue;

                Vector3 position = slot.Root.transform.position;
                float score = Mathf.Abs(position.x - _resolvedPageSelectorPosition.x) +
                              Mathf.Abs(position.y - _resolvedPageSelectorPosition.y) * 0.15f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = slot;
                }
            }
            return best;
        }

        private static Transform FindAncestorNamed(GameObject candidate, string name)
        {
            Transform current = candidate == null ? null : candidate.transform;
            while (current != null)
            {
                if (string.Equals(current.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return current;
                current = current.parent;
            }
            return null;
        }

        private Vector3 ResolvePageSelectorPosition()
        {
            // The live "Next Dot" is the Unity 6 anchor occupying the same visual
            // location that CharmPreset uses: immediately right of the notch count.
            // Its sprite is tiny, but its transform remains the most reliable position.
            GameObject anchor = FindNativePageSelectorTemplate();
            if (anchor != null)
            {
                Vector3 position = anchor.transform.position;
                Plugin.Log.LogInfo(
                    "Using live Next Dot as charm page selector anchor: " +
                    CharmUtil.GetHierarchyPath(anchor) + " @ " + position + ".");
                return position;
            }

            Plugin.Log.LogInfo("Using CharmPreset fallback page selector position: " + PageSelectorPosition + ".");
            return PageSelectorPosition;
        }

        private static void AddDistinctCoordinate(List<float> values, float value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Mathf.Abs(values[i] - value) < 0.08f)
                    return;
            }
            values.Add(value);
        }

        private static float FindSmallestCoordinateStep(List<float> values, float fallback)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < values.Count; i++)
            {
                float delta = Mathf.Abs(values[i] - values[i - 1]);
                if (delta > 0.08f && delta < best)
                    best = delta;
            }
            return float.IsInfinity(best) ? fallback : best;
        }

        private void BuildPageSelector()
        {
            Transform existing = FindNamedTransform(_pane.transform, "CharmsEvolve Page Selector", 8);
            if (existing != null)
                _pageSelector = existing.gameObject;
            else
            {
                // Match CharmPreset: a plain HUD object containing one SpriteRenderer
                // and one BoxCollider2D, rather than cloning the tiny native Next Dot.
                _pageSelector = new GameObject("CharmsEvolve Page Selector");
                _pageSelector.transform.SetParent(_pane.transform, true);
                Plugin.Log.LogInfo("Created CharmPreset-style charm page selector.");
            }

            _pageSelector.SetActive(true);
            _pageSelector.layer = ResolveUiLayer();
            _pageSelector.transform.position = _resolvedPageSelectorPosition;
            _pageSelector.transform.localScale = Vector3.one;

            SpriteRenderer[] renderers = _pageSelector.GetComponentsInChildren<SpriteRenderer>(true);
            _pageSelectorRenderer = _pageSelector.GetComponent<SpriteRenderer>();
            if (_pageSelectorRenderer == null)
                _pageSelectorRenderer = _pageSelector.AddComponent<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i] != _pageSelectorRenderer)
                    renderers[i].enabled = false;
            }

            Type colliderType = ResolveBoxCollider2DType();
            if (colliderType != null)
            {
                _pageSelectorCollider = _pageSelector.GetComponent(colliderType);
                if (_pageSelectorCollider == null)
                    _pageSelectorCollider = _pageSelector.AddComponent(colliderType);
                CharmUtil.TrySetMember(_pageSelectorCollider, "size", PageSelectorColliderSize);
            }
            else
            {
                Plugin.Log.LogError("UnityEngine.BoxCollider2D could not be resolved; the charm page selector cannot receive native cursor navigation.");
            }

            Sprite firstResolved = null;
            for (int i = 0; i < PageCount; i++)
            {
                int charmId = i + 1;
                Sprite sprite = FindInventoryCharmSprite(charmId);
                if (!IsUsableInventoryCharmSprite(sprite, charmId))
                {
                    Slot slot;
                    if (_slotByOriginalId.TryGetValue(charmId, out slot) && slot != null &&
                        IsUsableInventoryCharmSprite(slot.OriginalSprite, charmId))
                        sprite = slot.OriginalSprite;
                }
                if (sprite == null)
                {
                    Sprite candidate;
                    if (_textures.TryGetSprite(null, charmId, out candidate) &&
                        candidate != null && !IsBlurOrEffectName(candidate.name))
                        sprite = candidate;
                }
                if (sprite == null && _nativeIconRendererTemplate != null)
                    sprite = _nativeIconRendererTemplate.sprite;
                if (sprite == null && _detailIcon != null)
                    sprite = _detailIcon.sprite;

                _pageSelectorSprites[i] = sprite;
                if (firstResolved == null && sprite != null)
                    firstResolved = sprite;
            }

            for (int i = 0; i < PageCount; i++)
            {
                if (_pageSelectorSprites[i] == null)
                    _pageSelectorSprites[i] = firstResolved;
            }

            _pageSelectorRenderer.sortingLayerName = "HUD";
            _pageSelectorRenderer.sortingOrder = GetHighestHudSortingOrder() + 10;
            _pageSelectorRenderer.gameObject.layer = ResolveUiLayer();
            _pageSelectorRenderer.color = Color.white;
            ApplyNativeIconRendererTemplate(_pageSelectorRenderer);
            UpdatePageSelectorSprite();

            Plugin.Log.LogInfo(
                "Charm page selector visual: sprite=" +
                (_pageSelectorRenderer.sprite == null ? "<none>" : _pageSelectorRenderer.sprite.name) +
                ", world=" + _pageSelector.transform.position +
                ", local=" + _pageSelector.transform.localPosition +
                ", sorting=" + _pageSelectorRenderer.sortingLayerName + "/" +
                _pageSelectorRenderer.sortingOrder +
                ", collider=" + (_pageSelectorCollider == null ? "<none>" : _pageSelectorCollider.GetType().Name) + ".");
        }

        private int GetHighestHudSortingOrder()
        {
            if (_pane == null)
                return 0;

            int highest = 0;
            SpriteRenderer[] renderers = _pane.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer == _pageSelectorRenderer)
                    continue;
                if (string.Equals(renderer.sortingLayerName, "HUD", StringComparison.OrdinalIgnoreCase))
                    highest = Mathf.Max(highest, renderer.sortingOrder);
            }
            return highest;
        }

        private static Transform FindNamedTransform(Transform root, string name, int maxDepth)
        {
            if (root == null || maxDepth < 0)
                return null;
            if (string.Equals(root.gameObject.name, name, StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamedTransform(root.GetChild(i), name, maxDepth - 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static GameObject FindNativePageSelectorTemplate()
        {
            Component[] items = CharmUtil.FindAllCharmItems();
            GameObject best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < items.Length; i++)
            {
                Component item = items[i];
                if (item == null || item.gameObject == null)
                    continue;

                int numericId;
                if (int.TryParse(item.gameObject.name, out numericId))
                    continue;

                string name = item.gameObject.name ?? string.Empty;
                string path = CharmUtil.GetHierarchyPath(item.gameObject);
                int score = 0;
                if (name.IndexOf("Next Dot", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 500;
                if (name.IndexOf("dot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 180;
                if (path.IndexOf("Equipped Charms", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 100;
                if (item.gameObject.GetComponent<SpriteRenderer>() != null ||
                    item.gameObject.GetComponentInChildren<SpriteRenderer>(true) != null)
                    score += 40;
                Component[] components = item.gameObject.GetComponentsInChildren<Component>(true);
                for (int j = 0; j < components.Length; j++)
                {
                    Component component = components[j];
                    if (component != null && string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                    {
                        score += 80;
                        break;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = item.gameObject;
                }
            }
            return bestScore >= 180 ? best : null;
        }

        private static void PrepareClonedPageSelector(GameObject clone)
        {
            if (clone == null)
                return;

            Component[] components = clone.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;

                string typeName = component.GetType().Name;
                if ((string.Equals(typeName, "CharmItem", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(typeName, "CharmDisplay", StringComparison.OrdinalIgnoreCase)) &&
                    component is Behaviour)
                    ((Behaviour)component).enabled = false;
            }

            SpriteRenderer primary = ResolveSelectorRenderer(clone);
            SpriteRenderer[] renderers = clone.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i] != primary)
                    renderers[i].enabled = false;
            }
        }

        private static SpriteRenderer ResolveSelectorRenderer(GameObject selector)
        {
            if (selector == null)
                return null;
            SpriteRenderer direct = selector.GetComponent<SpriteRenderer>();
            if (direct != null)
                return direct;

            SpriteRenderer[] renderers = selector.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sprite != null)
                    return renderers[i];
            }
            return renderers.Length > 0 ? renderers[0] : null;
        }

        private static Type ResolveBoxCollider2DType()
        {
            Type type = AccessTools.TypeByName("UnityEngine.BoxCollider2D");
            if (type != null)
                return type;

            return Type.GetType("UnityEngine.BoxCollider2D, UnityEngine.Physics2DModule", false);
        }

        private static Component FindComponentInChildrenByType(GameObject root, Type type)
        {
            if (root == null || type == null)
                return null;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && type.IsInstanceOfType(component))
                    return component;
            }
            return null;
        }

        private static bool IsCollider2DComponent(Component component)
        {
            if (component == null)
                return false;

            Type type = component.GetType();
            while (type != null)
            {
                if (string.Equals(type.Name, "Collider2D", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Collider2D", StringComparison.Ordinal))
                    return true;
                type = type.BaseType;
            }
            return false;
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
            _pageSelectorRenderer.sprite = sprite;
            _pageSelectorRenderer.enabled = sprite != null;
            _pageSelectorRenderer.color = Color.white;
        }

        private bool TryResolveCharmVisualSprite(string key, int originalId, Slot slot, out Sprite sprite)
        {
            if (_nativeSpriteFallbacks.TryGetValue(originalId, out sprite) &&
                IsUsableInventoryCharmSprite(sprite, originalId))
                return true;

            sprite = FindInventoryCharmSprite(originalId);
            if (IsUsableInventoryCharmSprite(sprite, originalId))
            {
                _nativeSpriteFallbacks[originalId] = sprite;
                return true;
            }

            if (slot != null && IsUsableInventoryCharmSprite(slot.OriginalSprite, originalId))
            {
                sprite = slot.OriginalSprite;
                _nativeSpriteFallbacks[originalId] = sprite;
                return true;
            }

            // Keep API-provided textures as a final fallback. The Unity 6 built-in
            // texture scan can resolve glow atlas entries for inactive slots, so native
            // inventory Sprites must be preferred whenever they exist.
            if (_textures.TryGetSprite(key, originalId, out sprite) && sprite != null &&
                !IsBlurOrEffectName(sprite.name))
                return true;

            sprite = null;
            return false;
        }

        private Sprite FindInventoryCharmSprite(int charmId)
        {
            EnsureNativeInventorySpriteCache();
            Sprite sprite;
            return _nativeSpriteFallbacks.TryGetValue(charmId, out sprite) ? sprite : null;
        }

        private void EnsureNativeInventorySpriteCache()
        {
            if (_nativeSpriteScanComplete)
                return;
            _nativeSpriteScanComplete = true;

            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            Dictionary<int, int> bestScores = new Dictionary<int, int>();
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite candidate = sprites[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.name) ||
                    IsBlurOrEffectName(candidate.name))
                    continue;

                int charmId;
                if (!TryParseCharmSpriteId(candidate.name, out charmId) ||
                    charmId < 1 || charmId > 42 ||
                    !IsUsableInventoryCharmSprite(candidate, charmId))
                    continue;

                int score = ScoreInventoryCharmSprite(candidate, charmId);
                int previous;
                if (bestScores.TryGetValue(charmId, out previous) && previous >= score)
                    continue;

                bestScores[charmId] = score;
                _nativeSpriteFallbacks[charmId] = candidate;
            }

            Plugin.Log.LogInfo(
                "Native inventory Sprite cache: " + _nativeSpriteFallbacks.Count +
                " charm ids resolved from " + sprites.Length + " loaded Sprites.");
        }

        private static int ScoreInventoryCharmSprite(Sprite candidate, int charmId)
        {
            int score = 0;
            string exact = charmId >= 1 && charmId <= 4
                ? "Inv_" + (14 - charmId).ToString("D4") + "_charm" + charmId
                : string.Empty;

            if (!string.IsNullOrEmpty(exact) &&
                string.Equals(candidate.name, exact, StringComparison.Ordinal))
                score += 4000;
            if (candidate.name.StartsWith("Inv_", StringComparison.OrdinalIgnoreCase))
                score += 1200;
            if (candidate.name.EndsWith("charm" + charmId, StringComparison.OrdinalIgnoreCase))
                score += 500;

            Rect rect = candidate.rect;
            float smaller = Mathf.Min(rect.width, rect.height);
            float larger = Mathf.Max(rect.width, rect.height);
            if (smaller >= 32f)
                score += 200;
            if (larger <= 512f)
                score += 100;
            score += Mathf.RoundToInt(Mathf.Min(300f, smaller));
            return score;
        }

        private static bool IsUsableInventoryCharmSprite(Sprite sprite, int charmId)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name) ||
                IsBlurOrEffectName(sprite.name))
                return false;

            int parsedId;
            if (!TryParseCharmSpriteId(sprite.name, out parsedId) || parsedId != charmId)
                return false;

            Rect rect = sprite.rect;
            return rect.width >= 8f && rect.height >= 8f;
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

        private void ResolveNativeIconRendererTemplate()
        {
            _nativeIconRendererTemplate = null;
            if (_pane == null)
                return;

            SpriteRenderer[] renderers = _pane.GetComponentsInChildren<SpriteRenderer>(true);
            int bestScore = int.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer.sprite == null || IsGridIcon(renderer))
                    continue;

                string objectName = renderer.gameObject.name ?? string.Empty;
                string spriteName = renderer.sprite.name ?? string.Empty;
                string path = CharmUtil.GetHierarchyPath(renderer.gameObject);
                if (IsBlurOrEffectName(objectName) || IsBlurOrEffectName(spriteName) ||
                    objectName.IndexOf("dot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("notch", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                int score = 0;
                if (path.IndexOf("Equipped Charms", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 1200;
                if (renderer.gameObject.activeInHierarchy)
                    score += 200;
                if (spriteName.StartsWith("Inv_", StringComparison.OrdinalIgnoreCase))
                    score += 500;
                int id;
                if (TryParseCharmSpriteId(spriteName, out id) && id >= 1 && id <= 42)
                    score += 400;
                if (renderer.sharedMaterial != null)
                    score += 100;

                if (score > bestScore)
                {
                    bestScore = score;
                    _nativeIconRendererTemplate = renderer;
                }
            }

            if (_nativeIconRendererTemplate == null && _detailIcon != null)
                _nativeIconRendererTemplate = _detailIcon;

            if (_nativeIconRendererTemplate != null)
            {
                Plugin.Log.LogInfo(
                    "Native crisp charm renderer template: " +
                    CharmUtil.GetHierarchyPath(_nativeIconRendererTemplate.gameObject) +
                    ", sprite=" +
                    (_nativeIconRendererTemplate.sprite == null
                        ? "<none>"
                        : _nativeIconRendererTemplate.sprite.name) +
                    ", material=" +
                    (_nativeIconRendererTemplate.sharedMaterial == null
                        ? "<none>"
                        : _nativeIconRendererTemplate.sharedMaterial.name) + ".");
            }
        }

        private static bool IsBlurOrEffectName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("blur", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("shine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("halo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("royal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CaptureDescriptionTypography()
        {
            if (_descriptionText == null || _descriptionTypographyCaptured)
                return;

            object value;
            _originalDescriptionFontSize = ReadFloatMember(_descriptionText, "fontSize", 0f);
            _originalDescriptionFontSizeMin = ReadFloatMember(_descriptionText, "fontSizeMin", 0f);
            _originalDescriptionFontSizeMax = ReadFloatMember(_descriptionText, "fontSizeMax", _originalDescriptionFontSize);
            if (CharmUtil.TryGetMember(_descriptionText, "enableAutoSizing", out value) && value != null)
            {
                try { _originalDescriptionAutoSizing = Convert.ToBoolean(value); }
                catch { _originalDescriptionAutoSizing = false; }
            }
            _descriptionTypographyCaptured = true;
            Plugin.Log.LogInfo(
                "Charm description typography captured: size=" + _originalDescriptionFontSize +
                ", min=" + _originalDescriptionFontSizeMin +
                ", max=" + _originalDescriptionFontSizeMax +
                ", auto=" + _originalDescriptionAutoSizing + ".");
        }

        private static float ReadFloatMember(object target, string name, float fallback)
        {
            object value;
            if (!CharmUtil.TryGetMember(target, name, out value) || value == null)
                return fallback;
            try { return Convert.ToSingle(value); }
            catch { return fallback; }
        }

        private void ApplyExtendedDescriptionTypography(string text)
        {
            if (_descriptionText == null)
                return;
            CaptureDescriptionTypography();
            if (!_descriptionTypographyCaptured || _originalDescriptionFontSize <= 0f)
                return;

            int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
            float scale = 0.78f;
            if (length > 320)
                scale = 0.44f;
            else if (length > 220)
                scale = 0.50f;
            else if (length > 140)
                scale = 0.58f;
            else if (length > 80)
                scale = 0.68f;

            // Hollow Knight's world-space TMP font sizes are commonly below 7.5.
            // The old absolute floor therefore enlarged text instead of shrinking it.
            float size = Mathf.Max(0.1f, _originalDescriptionFontSize * scale);
            float requestedMinimum = Mathf.Max(0.1f, size * 0.62f);
            float minimum = _originalDescriptionFontSizeMin > 0f
                ? Mathf.Min(_originalDescriptionFontSizeMin, requestedMinimum)
                : requestedMinimum;

            CharmUtil.TrySetMember(_descriptionText, "enableAutoSizing", true);
            CharmUtil.TrySetMember(_descriptionText, "fontSizeMax", size);
            CharmUtil.TrySetMember(_descriptionText, "fontSizeMin", minimum);
            CharmUtil.TrySetMember(_descriptionText, "fontSize", size);
        }

        private void RestoreDescriptionTypography()
        {
            if (_descriptionText == null || !_descriptionTypographyCaptured)
                return;

            CharmUtil.TrySetMember(_descriptionText, "enableAutoSizing", _originalDescriptionAutoSizing);
            if (_originalDescriptionFontSize > 0f)
                CharmUtil.TrySetMember(_descriptionText, "fontSize", _originalDescriptionFontSize);
            if (_originalDescriptionFontSizeMin > 0f)
                CharmUtil.TrySetMember(_descriptionText, "fontSizeMin", _originalDescriptionFontSizeMin);
            if (_originalDescriptionFontSizeMax > 0f)
                CharmUtil.TrySetMember(_descriptionText, "fontSizeMax", _originalDescriptionFontSizeMax);
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
            int resolvedCost = CharmsEvolveApi.GetCharmCost(definition.Key);
            string description = "花费：" + resolvedCost + " 槽";

            if (!string.IsNullOrEmpty(definition.SourceEffect))
                description += "\n\n基础能力：" + definition.SourceEffect;

            if (!string.IsNullOrEmpty(definition.Description) &&
                !string.Equals(definition.Description, definition.SourceEffect, StringComparison.Ordinal))
                description += "\n\n同名原版 + 复制护符：" + definition.Description;

            if (!string.IsNullOrEmpty(definition.VanillaSynergy))
                description += "\n\n原版联动：" + definition.VanillaSynergy;

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

            if (slot != null && slot.OriginalId == 36)
                description += "\n\n操作：轻按确认键装卸；按住确认键切换虚空之心／国王之魂。";
            else if (slot != null && slot.OriginalId == 40)
                description += "\n\n操作：轻按确认键装卸；按住确认键切换格林之子／无忧旋律。";

            if (Time.unscaledTime < _statusUntil && !string.IsNullOrEmpty(_status))
                description += "\n\n" + _status;

            CharmDescriptionContext descriptionContext = new CharmDescriptionContext(
                definition.Key, definition.OriginalId, false, title, description);
            CharmsEvolveApi.RaiseBuildCharmDescription(descriptionContext);
            title = descriptionContext.Title;
            description = descriptionContext.Description;

            SetText(_nameText, title);
            SetText(_descriptionText, description);
            ApplyExtendedDescriptionTypography(description);

            if (_detailIcon != null)
            {
                Sprite sprite;
                if (TryResolveCharmVisualSprite(definition.Key, definition.OriginalId, slot, out sprite))
                    _detailIcon.sprite = sprite;
                _detailIcon.color = Color.white;
                _detailIcon.enabled = _state.IsOwned(definition.Key);
            }
        }

        private void UpdateVanillaDetails(Slot slot)
        {
            if (slot == null || _descriptionText == null)
                return;

            BaseCharmDefinition definition = CharmDatabase.GetBase(slot.OriginalId);
            if (definition == null)
                return;

            string current = GetText(_descriptionText);
            int markerIndex = current.IndexOf(VanillaDescriptionMarker, StringComparison.Ordinal);
            string nativeDescription = markerIndex >= 0 ? current.Substring(0, markerIndex) : current;

            string addition = BuildVanillaAdjustmentText(definition);
            if (string.IsNullOrEmpty(addition))
            {
                RestoreDescriptionTypography();
                return;
            }

            string title = GetText(_nameText);
            string merged = nativeDescription + VanillaDescriptionMarker + "\n" + addition;
            CharmDescriptionContext context = new CharmDescriptionContext(
                null, slot.OriginalId, true, title, merged);
            CharmsEvolveApi.RaiseBuildCharmDescription(context);
            SetText(_nameText, context.Title);
            SetText(_descriptionText, context.Description);
            ApplyExtendedDescriptionTypography(context.Description);
        }

        private static string BuildVanillaAdjustmentText(BaseCharmDefinition definition)
        {
            List<string> lines = new List<string>();
            if (!string.IsNullOrEmpty(definition.EnhancedSynergy))
                lines.Add("（与相关护符联动：" + definition.EnhancedSynergy + "）");
            if (!string.IsNullOrEmpty(definition.VoidKnight))
                lines.Add("（形态联动：" + definition.VoidKnight + "）");
            if (!string.IsNullOrEmpty(definition.LegacyEnhancement))
                lines.Add("（Charms Evolve 调整：" + definition.LegacyEnhancement + "）");
            if (definition.StackableSynergies != null)
            {
                for (int i = 0; i < definition.StackableSynergies.Length; i++)
                {
                    if (!string.IsNullOrEmpty(definition.StackableSynergies[i]))
                        lines.Add("（可叠加联动：" + definition.StackableSynergies[i] + "）");
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        private void CaptureNativeDetails()
        {
            CaptureDescriptionTypography();
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
            RestoreDescriptionTypography();
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

            bool replayed = TryInvokeNativeCharmVisualMethod(slot, equipping);
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

        private bool TryInvokeNativeCharmVisualMethod(Slot slot, bool equipping)
        {
            if (slot == null || slot.Root == null)
                return false;

            Component[] components = slot.Root.GetComponentsInChildren<Component>(true);
            string verb = equipping ? "equip" : "unequip";
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;

                string componentName = component.GetType().Name;
                if (!string.Equals(componentName, "CharmDisplay", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(componentName, "CharmItem", StringComparison.OrdinalIgnoreCase))
                    continue;

                MethodInfo[] methods = component.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int j = 0; j < methods.Length; j++)
                {
                    MethodInfo method = methods[j];
                    string methodName = method.Name ?? string.Empty;
                    if (method.GetParameters().Length != 0 || method.ReturnType != typeof(void))
                        continue;
                    if (methodName.IndexOf(verb, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (methodName.IndexOf("play", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("anim", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("effect", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("feedback", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        method.Invoke(component, null);
                        Plugin.Log.LogDebug("Invoked native " + componentName + "." + methodName + " for " + verb + " feedback.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogDebug("Native charm visual method " + methodName + " failed: " + ex.Message);
                    }
                }
            }
            return false;
        }

        private bool TryInvokeNativeFeedbackActions(string stateName)
        {
            List<Component> fsms = GetFeedbackFsms();
            bool invoked = false;
            for (int f = 0; f < fsms.Count; f++)
            {
                IList actions = GetStateActions(fsms[f], stateName);
                if (actions == null || actions.Count == 0)
                    continue;

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
            }
            return invoked;
        }

        private bool TryInvokeNativeFeedbackStateByFragments(string[] fragments)
        {
            if (fragments == null || fragments.Length == 0)
                return false;

            List<Component> fsms = GetFeedbackFsms();
            for (int f = 0; f < fsms.Count; f++)
            {
                List<string> names = GetStateNames(fsms[f]);
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    for (int j = 0; j < fragments.Length; j++)
                    {
                        if (name.IndexOf(fragments[j], StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (TryInvokeNativeFeedbackActions(name))
                            return true;
                    }
                }
            }
            return false;
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
                   typeName.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("SpawnObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Pool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(typeName, "ActivateGameObject", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryInvokeNativeAudioAction(string stateName, int preferredIndex)
        {
            List<Component> fsms = GetFeedbackFsms();
            for (int f = 0; f < fsms.Count; f++)
            {
                IList actions = GetStateActions(fsms[f], stateName);
                if (actions == null || actions.Count == 0)
                    continue;

                if (preferredIndex >= 0 && preferredIndex < actions.Count &&
                    TryInvokeActionIfAudio(actions[preferredIndex]))
                    return true;

                for (int i = 0; i < actions.Count; i++)
                {
                    if (TryInvokeActionIfAudio(actions[i]))
                        return true;
                }
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

        private List<Component> GetFeedbackFsms()
        {
            List<Component> result = new List<Component>();
            AddUniqueFsm(result, _uiCharmsFsm);
            AddUniqueFsm(result, _updateCursorFsm);

            GameObject root = _pane ?? _gridRoot;
            if (root != null)
            {
                Component[] components = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                    AddUniqueFsm(result, components[i]);
            }
            return result;
        }

        private static void AddUniqueFsm(List<Component> result, Component component)
        {
            if (component == null || result.Contains(component) ||
                !string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                return;
            result.Add(component);
        }

        private static IList GetStateActions(Component fsmComponent, string stateName)
        {
            if (fsmComponent == null || string.IsNullOrEmpty(stateName))
                return null;

            object fsm;
            if (!CharmUtil.TryGetMember(fsmComponent, "Fsm", out fsm) || fsm == null)
                fsm = fsmComponent;

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
                if (!(CharmUtil.TryGetMember(state, "Name", out nameValue) ||
                      CharmUtil.TryGetMember(state, "name", out nameValue)) ||
                    !string.Equals(nameValue == null ? string.Empty : nameValue.ToString(), stateName, StringComparison.Ordinal))
                    continue;

                object actionsValue;
                if (CharmUtil.TryGetMember(state, "Actions", out actionsValue) ||
                    CharmUtil.TryGetMember(state, "actions", out actionsValue))
                    return actionsValue as IList;
            }
            return null;
        }

        private static List<string> GetStateNames(Component fsmComponent)
        {
            List<string> result = new List<string>();
            if (fsmComponent == null)
                return result;

            object fsm;
            if (!CharmUtil.TryGetMember(fsmComponent, "Fsm", out fsm) || fsm == null)
                fsm = fsmComponent;

            object statesValue;
            if (!(CharmUtil.TryGetMember(fsm, "States", out statesValue) ||
                  CharmUtil.TryGetMember(fsm, "states", out statesValue)))
                return result;

            IEnumerable states = statesValue as IEnumerable;
            if (states == null)
                return result;

            foreach (object state in states)
            {
                object nameValue;
                if (!(CharmUtil.TryGetMember(state, "Name", out nameValue) ||
                      CharmUtil.TryGetMember(state, "name", out nameValue)) || nameValue == null)
                    continue;
                string name = nameValue.ToString();
                if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                    result.Add(name);
            }
            return result;
        }

        private static Component FindFsmAround(GameObject root, string fsmName)
        {
            if (root == null)
                return null;

            Component found = FindFsmInChildren(root, fsmName);
            if (found != null)
                return found;

            Transform current = root.transform.parent;
            for (int depth = 0; current != null && depth < 6; depth++, current = current.parent)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null || !string.Equals(component.GetType().Name, "PlayMakerFSM", StringComparison.Ordinal))
                        continue;
                    if (string.Equals(CharmUtil.GetFsmName(component), fsmName, StringComparison.Ordinal))
                        return component;
                }
            }
            return null;
        }

        private static object FindComponentByTypeNameAround(GameObject root, string typeName)
        {
            if (root == null)
                return null;

            Transform current = root.transform;
            for (int depth = 0; current != null && depth < 8; depth++, current = current.parent)
            {
                object direct = FindComponentByTypeName(current.gameObject, typeName);
                if (direct != null)
                    return direct;

                if (depth == 0)
                {
                    Component[] descendants = current.gameObject.GetComponentsInChildren<Component>(true);
                    for (int i = 0; i < descendants.Length; i++)
                    {
                        Component component = descendants[i];
                        if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                            return component;
                    }
                }
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
            _nativeSpriteFallbacks.Clear();
            _nativeSpriteScanComplete = false;
            _pane = null;
            _gridRoot = null;
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
            _lastGridSlot = null;
            _lastEquipmentTarget = null;
            _nativeIconRendererTemplate = null;
            _nextSelectorCursorRefresh = 0f;
            _descriptionTypographyCaptured = false;
            _originalDescriptionFontSize = 0f;
            _originalDescriptionFontSizeMin = 0f;
            _originalDescriptionFontSizeMax = 0f;
            _originalDescriptionAutoSizing = false;
            _detailSnapshotValid = false;
            _resolvedPageSelectorPosition = PageSelectorPosition;
        }
    }
}
