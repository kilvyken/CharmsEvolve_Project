# CharmsEvolve 0.3.1 覆盖补丁

本包仅包含覆盖/新增文件，不包含 `.csproj`、`.sln`、游戏 DLL 或完整项目。

## 编译错误修复

错误 `CS1069 BoxCollider2D/Collider2D 已转发到 UnityEngine.Physics2DModule` 的根因是
`CharmPageController.cs` 直接声明了 `BoxCollider2D` 与 `Collider2D`，但当前项目没有引用
`UnityEngine.Physics2DModule.dll`。

本补丁没有修改项目引用，而是改为运行时反射解析：

- `UnityEngine.BoxCollider2D, UnityEngine.Physics2DModule`
- `GameObject.GetComponent(Type)`
- `GameObject.AddComponent(Type)`
- 通过 `Component` 扫描 Collider2D 派生类型

因此覆盖后不需要手动添加 `UnityEngine.Physics2DModule.dll` 引用。

## UI 与状态调整

- 自定义页护符装备后使用从原版已装备护符采样到的灰暗色。
- 如果打开护符页时没有任何原版护符处于装备状态，使用原版未装备颜色计算灰暗回退色，并在日志中警告。
- 装卸反馈继续调用 `UI Charms` 原版状态动作；白名单新增 Particle、SpawnObject、Pool、ActivateGameObject 类动作。
- 原版页右侧描述追加 `【Charms Evolve】` 调整、联动、形态和可叠加内容。
- 复制护符描述明确区分：基础能力、同名原版+复制护符效果、原版联动和当前激活联动。

## 新增接口

`CharmsEvolveApi` 新增：

- `RegisterSprite(string charmKey, Sprite sprite)`
- `GetCharmCost(string key)`
- `GetEffectiveCharmCount(int originalCharmId)`
- `IsOriginalOrCopyEquipped(int originalCharmId)`
- `ResolveNotchCost` 事件
- `RuntimeModifiersBuilding` 事件
- `BuildCharmDescription` 事件
- `EvaluateSynergy` 事件
- `EffectTick` 事件
- `ReportEffectError(...)`

槽位计算、装备检查和右侧显示均使用 `ResolveNotchCost` 的最终结果。

## BepInEx 日志审计

新增 `ImplementationDiagnostics.cs`。启动时会明确打印：

- 已有实际运行时补丁的基础效果编号；
- 已有手写运行时补丁的联动编号；
- 只计算了数值但尚未接入游戏方法的效果编号；
- 尚无内置实现的效果编号；
- 原版/复制护符等价判断的当前边界。

Harmony 找不到目标类型或方法时现在使用 `LogError`，不会再静默失效。

## 当前实现边界

本补丁修复编译并补齐接口与日志，但不能把表格中的全部 120 个效果自动变成游戏逻辑。
当前完整效果覆盖仍是部分实现；具体状态以启动日志中的 `Effect audit` 为准。

原版装卸飞行动画和粒子动作仍依赖当前游戏版本中 `UI Charms` FSM 的实际状态与动作名称，
本环境没有对应游戏程序集和运行时，必须实机验证。
