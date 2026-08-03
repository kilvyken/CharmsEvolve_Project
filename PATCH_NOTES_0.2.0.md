# CharmsEvolve 0.2.0 — 原版护符面板覆盖包

本包只包含需要覆盖/新增的源码文件，不包含 `.csproj`、解决方案、引用或数据文件。
直接解压到现有项目根目录并覆盖即可。

## 文件

- 覆盖 `src/CharmsEvolve/Plugin.cs`
- 覆盖 `src/CharmsEvolve/Core/ModRuntime.cs`
- 覆盖 `src/CharmsEvolve/UI/CharmPageController.cs`
- 覆盖 `src/CharmsEvolve/Icons/VanillaCharmIconProvider.cs`
- 覆盖 `src/CharmsEvolve/Icons/CharmTextureRegistry.cs`
- 新增 `src/CharmsEvolve/Interop/CharmUtil.cs`

## 根因

旧版入口：

- `Plugin.cs` 第 79–82 行：`OnGUI()` 调用 `_runtime.DrawGui()`。
- `CharmPageController.cs` 第 128–165 行：用 `GUI.Box` 绘制独立覆盖页。
- `CharmPageController.cs` 第 247–249、344–355 行：用 `GUI.Button` 和鼠标点击选择、装备。
- `CharmPageController.cs` 第 448–479 行：用 `Font.CreateDynamicFontFromOSFont` 创建系统字体。
- `CharmPageController.cs` 第 258–264 行：把 Texture/UV 直接交给 IMGUI 绘制，因此可能命中非护符 atlas 项。

0.2.0 删除整个 OnGUI 绘制路径。

## 原版参照

- 面板：查找带 `UI Charms` FSM 的 GameObject，等价于 `CharmUtil.CharmsPane`。
- 光标：读取同一面板的 `Update Cursor` FSM 中 `Item` 变量。
- 导航：保留原版 `UI UP / UI DOWN / UI LEFT / UI RIGHT`；仅在网格左右边界拦截 `UI LEFT / UI RIGHT` 翻页。
- 装备：自定义页拦截同一个 `UI CONFIRM` 事件，调用现有 `CharmStateService.Toggle`。
- 提示音：尝试调用原 `UI Charms` FSM 的 `Tween Up` 状态第 1 个音频动作。
- 详情：复用护符面板现有名称、描述和详情图标组件；不创建 Canvas 或 IMGUI。
- 精灵：`Resources.FindObjectsOfTypeAll<Sprite>()`，按 `_charm1` 至 `_charm40` 名称匹配，并优先 `Inv_` 精灵。
- 渲染层：所有使用到的 SpriteRenderer 强制 `sortingLayerName = "HUD"`；UI 层通过运行时解析 `(int)PhysLayers.UI`。
- 淡入淡出：把相关 SpriteRenderer 合并进 `FadeGroup.spriteRenderers`。

## 翻页冲突处理

原版 `UI LEFT / UI RIGHT` 已用于网格横向移动，不能把它们无条件改成翻页键。
本包采用最接近原版的处理：

- 光标在最左列时按 `UI LEFT`：上一页。
- 光标在最右列时按 `UI RIGHT`：下一页。
- 其他位置仍按原版方式移动光标。
- 页序循环：原版 → X → Y → Z → 原版。

因此不再使用旧版 Q/E、F7、鼠标按钮，也不会新建按键 UI。

## 当前实现边界

- 顶部原版已装备区域不重建、不克隆，保持游戏原结构；自定义护符仍由独立存档和槽位服务管理。
- 自定义页的“已装备”标记会复用槽位层级中名字包含 `Equipped` 的原组件；若最新版游戏改了该对象名，日志不会报错，但标记可能需要根据实际层级补一个候选名。
- 名称/描述组件通过原对象名称和现有文本长度识别。若日志出现 `Native charm detail text was not fully resolved`，请提供护符面板层级或日志，我只更新识别表，不改回覆盖层。
- 没有加入新的程序集引用；现有引用无需重做。

## 覆盖后

1. 关闭游戏和 Visual Studio。
2. 将本包解压到项目根目录并覆盖。
3. 删除 `src/CharmsEvolve/bin` 和 `src/CharmsEvolve/obj`。
4. 重新生成解决方案。
5. 打开护符页，在网格最左/最右列使用原版方向输入翻页。
