# Charms Evolve 0.4.0 — Unity 6 CharmItem UI compatibility overlay

This archive is an overlay patch only. It intentionally contains no `.csproj`, `.sln`, DLL, NuGet package, or game assembly reference.

## Root bug removed

The old controller required a PlayMaker FSM named `UI Charms`, then identified the 40 collection slots by matching each `SpriteRenderer.sprite.name` against `charmNN`. Unity 6 no longer guarantees either structure.

The new discovery order is:

1. Resolve the `CharmItem` component type by scanning loaded assemblies without a compile-time reference.
2. Call `Resources.FindObjectsOfTypeAll(Type)` and keep live scene objects whose numeric item is under a parent named `Charms`.
3. Resolve the live `Charms` grid from those items, explicitly excluding `Equipped Charms`.
4. Enumerate numeric GameObject names `1` through `40` under that live grid and use their `CharmItem` / `CharmDisplay` / `SpriteRenderer` components.
5. Fall back to the old `UI Charms` FSM and sprite-name regex only for older Unity builds.

Resource/prefab objects with an invalid or unloaded scene are not allowed to win the live-grid selection.

## UI and test-stage behavior

- The custom pager now tolerates a missing global `UI Charms` FSM.
- A native non-numeric `CharmItem` such as `Next Dot` is used as the page-button template when available. The fallback still uses only a SpriteRenderer and a reflected Collider2D type; it does not add a Canvas or IMGUI.
- The selector position is calculated from the live 40-slot grid bounds instead of the Unity-5 world coordinate.
- Grid construction no longer depends on sprite names. Expanded sprite regex remains only as a Unity-5 fallback.
- All custom pages keep the original 40 physical grid positions.
- All 126 custom forms are owned when `UnlockAllCopies=true`:
  - X/Y/Z forms for source IDs 1–40 = 120
  - X/Y/Z Carefree Melody (41) and Kingsoul (42) = 6
- Slot 36: tap CONFIRM equips/unequips; hold CONFIRM for 0.70 s toggles Void Heart ↔ Kingsoul.
- Slot 40: tap CONFIRM equips/unequips; hold CONFIRM for 0.70 s toggles Grimmchild ↔ Carefree Melody.
- When a displayed form is equipped, a form switch transfers the equipped state to the other form and recalculates notch cost. A rejected transfer rolls the visual form back.
- Long-hold feedback searches the live charm UI FSMs for overcharm/overload/bound/shake states and events. Exact Unity 6 state names are logged for validation.
- IDs 41 and 42 use matching native Sprite names when found and otherwise fall back to the shared 40/36 slot art. `CharmsEvolveApi.RegisterSprite` remains the future custom-art override.
- Extended original/custom descriptions enable native TextMeshPro auto-sizing and reduce the native font size according to text length, then restore the original typography when leaving the page.
- Original charm descriptions append parenthesized Charms Evolve adjustments and synergy text below the native description.

## Expected BepInEx log on success

Look for:

```text
Loading [Charms Evolve 0.4.0]
Test ownership enabled: all 126 custom charm forms are owned ...
CharmItem probe: resources=..., under selected Charms root=..., resolved slots=40, missing=<none>.
Native charm pager ready. Grid slots=40, grid=..., pane=..., navigationFSM=....
Charm UI FSM probe: candidates=..., relevant=....
```

If fewer than 40 slots are found, the warning is throttled and followed by `CharmItem sample`, hierarchy, Sprite, missing-ID, FSM-state, and CharmItem/CharmDisplay method diagnostics.

## UnityExplorer C# Console probes

Run these after opening the charm page.

### 1. Live CharmItem hierarchy, scene and Sprite

```csharp
var items = UnityEngine.Resources.FindObjectsOfTypeAll<CharmItem>();
foreach (var item in items)
{
    var go = item.gameObject;
    var path = go.name;
    for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
    var sr = go.GetComponent<UnityEngine.SpriteRenderer>() ?? go.GetComponentInChildren<UnityEngine.SpriteRenderer>(true);
    UnityEngine.Debug.Log("CE ITEM name=" + go.name +
        " scene=" + go.scene.name + " valid=" + go.scene.IsValid() + " loaded=" + go.scene.isLoaded +
        " active=" + go.activeInHierarchy + " path=" + path +
        " sprite=" + (sr == null || sr.sprite == null ? "<none>" : sr.sprite.name));
}
```

### 2. PlayMaker FSM names and all state names near the live Charms parent

```csharp
var live = UnityEngine.Object.FindObjectsOfType<CharmItem>();
var first = System.Array.Find(live, x => x != null && int.TryParse(x.gameObject.name, out _));
var root = first == null ? null : first.transform.parent;
if (root != null)
{
    foreach (var f in root.GetComponentsInParent<PlayMakerFSM>(true))
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var st in f.Fsm.States) names.Add(st.Name);
        UnityEngine.Debug.Log("CE FSM owner=" + f.gameObject.name + " fsm=" + f.FsmName +
            " active=" + f.ActiveStateName + " states=" + string.Join(" | ", names.ToArray()));
    }
    foreach (var f in root.GetComponentsInChildren<PlayMakerFSM>(true))
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var st in f.Fsm.States) names.Add(st.Name);
        UnityEngine.Debug.Log("CE FSM child=" + f.gameObject.name + " fsm=" + f.FsmName +
            " active=" + f.ActiveStateName + " states=" + string.Join(" | ", names.ToArray()));
    }
}
```

### 3. CharmItem/CharmDisplay equip-animation method candidates

```csharp
var sample = System.Array.Find(UnityEngine.Object.FindObjectsOfType<CharmItem>(), x => x != null && x.gameObject.name == "1");
if (sample != null)
{
    foreach (var c in sample.gameObject.GetComponentsInChildren<UnityEngine.Component>(true))
    {
        if (c == null || (c.GetType().Name != "CharmItem" && c.GetType().Name != "CharmDisplay")) continue;
        foreach (var m in c.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            var n = m.Name.ToLowerInvariant();
            if (m.GetParameters().Length == 0 && (n.Contains("equip") || n.Contains("anim") || n.Contains("effect")))
                UnityEngine.Debug.Log("CE METHOD " + c.GetType().FullName + "." + m.Name);
        }
    }
}
```

Send back the `CE ITEM`, `CE FSM`, `CE METHOD`, `CharmItem probe`, and `Native charm pager ready` lines if the page still does not initialize or if native flight/particle/overcharm feedback is incomplete.

## Build

```powershell
dotnet build -c Release -p:HollowKnightDir="C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight"
```

The project target and existing references are not changed by this overlay.

## Overlay manifest

Only the following project files are included:

```text
src/CharmsEvolve/Plugin.cs
src/CharmsEvolve/Api/CharmsEvolveApi.cs
src/CharmsEvolve/Data/CharmDatabase.Generated.cs
src/CharmsEvolve/Gameplay/CharmStateService.cs
src/CharmsEvolve/Gameplay/ImplementationDiagnostics.cs
src/CharmsEvolve/Icons/VanillaCharmIconProvider.cs
src/CharmsEvolve/Interop/CharmUtil.cs
src/CharmsEvolve/Interop/GameReflection.cs
src/CharmsEvolve/UI/CharmPageController.cs
PATCH_NOTES_0.4.0_UNITY6.md
```

The Unity 6 event filter is intentionally restricted to the live `Charms` grid,
its selected charm FSM, and the page-selector object. A broad `UICanvas` or
pause-menu ancestor is not accepted as a charm event owner, preventing the pager
from consuming equipment/journal navigation events.

The extension API remains source-compatible: cost resolution, ownership/equipment,
Sprite/PNG overrides, description hooks, synergy evaluation, gameplay events and
structured BepInEx error reporting are retained. The current built-in UI still has
four pages (vanilla + X/Y/Z); adding a fifth independent family later requires a
new page definition as well as registrations, and is not falsely presented as a
fully dynamic page registry in this patch.
