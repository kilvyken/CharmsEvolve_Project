# Charms Evolve（护符演化）

基于 **BepInEx 5 + HarmonyX** 的《空洞骑士》护符扩展项目。

本项目从 `Design/hollowKnight_CharmsEvolve.xlsx` 导入设计数据，以 40 个原版护符槽位为基础，为每个槽位生成三种复制品：

- `X-01` ~ `X-42`：易碎仿制品
- `Y-01` ~ `Y-42：白王复制品
- `Z-01` ~ `Z-42：梦境印刻物

合计 **120 个复制护符**。原版 36 号和 40 号槽位会根据存档状态切换显示：

- 36：国王之魂 / 虚空之心
- 40：格林之子 / 无忧旋律

### 1. 护符切换
页面结构：

1. 原版护符(42个)
2. X 易碎仿制品（42 个）
3. Y 白王复制品（42 个）
4. Z 梦境印刻物（42 个）

在按钮上按左右的同时，整个护符页都不动，但是护符槽上的护符改变,原版X Y Z 轮流切换

复制页使用原版护符贴图

在
### 2. 独立存档和槽位系统

复制护符不会写入 `gotCharm_XX` / `equippedCharm_XX` 等原版固定字段，而是保存在：

```text
BepInEx/config/CharmsEvolve/slot-N.json
```

项目会把原版已装备护符的槽位与复制护符槽位相加，并同步到 `PlayerData.charmSlotsFilled`。默认只能在长椅处装备，可在配置中改为任意地点装备。

### 3. 数据驱动组合系统

表格中的以下字段已经进入 `CharmDatabase.Generated.cs`：

- 具体效果
- 原版联动
- 原始消耗 / 修改后消耗
- 联动加强
- VoidKnight
- 原 Mod 增强效果
- X / Y / Z 复制品效果
- 三列可叠加联动

`ComboEngine` 会从文字中的护符编号和中英文名称提取依赖，并在对应护符同时装备时生成 `ActiveSynergy`。

### 4. 已落地的通用效果与示例联动

项目内已实现：

- 易碎力量复制品：额外骨钉伤害倍率
- 亡者之怒复制品：1 血时额外骨钉伤害；乔尼的祝福存在时常驻
- 萨满之石复制品：额外法术伤害倍率
- 易碎贪婪复制品：额外 Geo 倍率
- 幼虫之歌复制品：受伤回魂
- 修长之钉 / 骄傲印记、快速聚集、冲刺大师、快速劈砍等：生成统一运行时倍率，供补丁或其他 DLL 调用
- 聚集蜂群 + 易碎贪婪：每 10 秒生成 1~25 Geo
- 上述组合再加任性的指南针：周期缩短为 8 秒
- 上述组合再加防御者纹章：生成量提高
- 坚硬外壳 + 稳定之体 + 虚空之心：2 点及以上伤害压为 1 点
- 锋利之影 + 虚空之心 + 灵魂捕手/噬魂者：暗影冲刺命中回魂

复杂的 FSM、召唤物、护盾层数、动画和判定框效果已经保留完整数据与公开事件接口，但仍需要针对游戏具体 FSM/方法编写专用适配器。参见 `docs/IMPLEMENTATION_STATUS.md`。

## 编译

需要安装 BepInEx 5，并确保以下文件存在：

```text
Hollow Knight/
├─ BepInEx/core/BepInEx.dll
├─ BepInEx/core/0Harmony.dll
└─ hollow_knight_Data/Managed/
   ├─ UnityEngine.dll
   ├─ UnityEngine.CoreModule.dll
   ├─ UnityEngine.IMGUIModule.dll
   ├─ UnityEngine.InputLegacyModule.dll
   ├─ UnityEngine.ImageConversionModule.dll
   ├─ UnityEngine.TextRenderingModule.dll
   └─ UnityEngine.JSONSerializeModule.dll
```

编译命令：

```powershell
dotnet build .\src\CharmsEvolve\CharmsEvolve.csproj `
  -c Release `
  -p:HollowKnightDir="D:\Steam\steamapps\common\Hollow Knight"
```

生成的 DLL 会自动复制到：

```text
Hollow Knight/BepInEx/plugins/CharmsEvolve/CharmsEvolve.dll
```

也可以只编译，然后手动复制 `bin/Release/net472/CharmsEvolve.dll`。

## 更改护符贴图

### 从 PNG 文件加载

```csharp
using CharmsEvolve.Api;

CharmsEvolveApi.RegisterTextureFromPng(
    "X-01",
    @"D:\MyCharmTextures\gathering_swarm_x.png");
```

### 使用已经加载的 Unity Texture

```csharp
using CharmsEvolve.Api;
using UnityEngine;

Texture2D texture = /* 你的纹理 */;
CharmsEvolveApi.RegisterTexture(
    "Y-19",
    texture,
    new Rect(0f, 0f, 1f, 1f));
```

恢复原版贴图：

```csharp
CharmsEvolveApi.ResetTexture("Y-19");
```

所有贴图键都遵循 `X-01`、`Y-01`、`Z-01` 格式。

## 对接其他效果 DLL

公开事件：

```csharp
CharmsEvolveApi.BeforeOutgoingDamage += context =>
{
    if (CharmsEvolveApi.IsEquipped("Z-25"))
        context.Multiplier *= 1.1f;
};

CharmsEvolveApi.HeroDamaged += context =>
{
    // 可修改受伤后要补充的灵魂
    context.SoulToGrant += 3;
};

CharmsEvolveApi.BeforeGeoGain += context =>
{
    context.Multiplier *= 1.05f;
};
```

其他可用接口：

- `GetAllCharms()`
- `GetCharm(key)`
- `IsOwned(key)`
- `IsEquipped(key)`
- `SetOwned(key, value)`
- `SetEquipped(key, value, out reason)`
- `GetCopyCount(originalCharmId)`
- `GetRuntimeModifiers()`
- `GetActiveSynergies()`

## 项目结构

```text
src/CharmsEvolve/
├─ Api/             公开接口
├─ Core/            生命周期
├─ Data/            120 个护符的数据模型和生成数据
├─ Gameplay/        装备状态、槽位、组合与 Harmony 补丁
├─ Icons/           原版贴图读取与覆盖接口
├─ Interop/         对 Assembly-CSharp 的反射访问
├─ Save/            独立存档
└─ UI/              原版页 + X/Y/Z 翻页 UI
```

## 兼容性说明

该实现刻意不直接引用 `Assembly-CSharp.dll`、`PlayMaker.dll` 或 Modding API，避免编译时绑定具体游戏版本；游戏对象通过反射和 Harmony 候选方法查找。这样更适合 BepInEx 项目，但不同《空洞骑士》版本或其他大型 UI/FSM Mod 仍可能需要调整 `GameReflection` 与 `HarmonyBridge` 中的候选名称。

建议先在备份存档上测试。
