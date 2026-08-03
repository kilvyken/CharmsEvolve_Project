# CharmsEvolve 0.3.0 - 原生护符分页选择器覆盖包

只覆盖源码，不包含 `.csproj`、解决方案、引用设置或完整项目。

## 覆盖文件

- `src/CharmsEvolve/Plugin.cs`
- `src/CharmsEvolve/Interop/CharmUtil.cs`
- `src/CharmsEvolve/UI/CharmPageController.cs`

## 当前 0.2.0 的根因

`CharmPageController.cs` 第 249-259 行在原版网格最左/最右格拦截 `UI LEFT` / `UI RIGHT` 并直接调用 `SetPage`。
这截断了原版从网格边缘进入左右库存箭头的路径，所以无法再自然进入装备页和日志页。

0.3.0 删除了这两个边缘拦截。网格中的左右移动全部交回原版 `UI Charms` FSM。

## 原版参照

参考 CharmPreset：

- `CharmUtil.CharmsPane`
- `LocateMyFSM("UI Charms")`
- 选择器位置 `(0.6, 1.4, -3.33)`
- `BoxCollider2D.size = (1.3, 1.3)`
- `PhysLayers.UI`
- `sortingLayerName = "HUD"`
- 加入 `FadeGroup.spriteRenderers`
- 选择器向上进入 `To Equipment`
- 选择器向下进入 `To Bot`
- 左右键只在选择器上切页
- 翻页音效复用 `UI Charms/Tween Up` 中的原生 Audio action

## 行为

1. 在原版已装备区与 40 格护符网格之间建立一个原生 SpriteRenderer 选择器。
2. 网格顶行按上，或已装备区按下，光标进入选择器。
3. 选择器上按左右切换：原版 / X / Y / Z。
4. 选择器上按上返回已装备区，按下返回原版护符网格。
5. 网格边缘左右移动不再切页，继续使用原版左右箭头，允许进入装备页/日志页。
6. 翻页只替换 40 个原版格子的图标、拥有态和装备标记；白线上方、右侧说明框和左右库存箭头不重建。
7. 自定义页确认键由现有独立护符状态系统处理，同时重放原版 `Tween Up/Tween Down` 中安全的 Audio/Tween/Animation action。
8. 页面切换使用原生格子 SpriteRenderer 的短淡出/淡入，不创建 Canvas、IMGUI、鼠标按钮或自定义字体。
9. 新护符详情显示名称、花费、功能、当前激活联动、潜在/可叠加联动。
10. 原有 PNG/Texture 覆盖 API 保留；未注册覆盖时使用 `Resources.FindObjectsOfTypeAll<Sprite>()` 找到的原版护符 Sprite。

## 覆盖与编译

把压缩包直接解压到现有 CharmsEvolve 项目根目录并选择覆盖，然后删除：

- `src/CharmsEvolve/bin`
- `src/CharmsEvolve/obj`

重新生成解决方案。

## 实机验证说明

该覆盖包不包含用户当前版本的 `Assembly-CSharp.dll` 和 PlayMaker FSM 资源，无法在此环境完成游戏内状态名验证。
代码优先使用 CharmPreset 已验证的 `To Equipment`、`To Bot`、`Tween Up`、`Tween Down`；若最新版游戏重命名了这些状态，日志会输出一次警告，而不会创建替代 Canvas 或自定义动画。
