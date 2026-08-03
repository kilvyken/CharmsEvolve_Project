# 添加专用效果

推荐每个复杂效果使用一个适配器类：

```csharp
public interface ICharmEffectAdapter
{
    void Install(Harmony harmony);
    void Refresh();
    void Uninstall(Harmony harmony);
}
```

装备判断：

```csharp
bool x = CharmsEvolveApi.IsEquipped("X-38");
bool y = CharmsEvolveApi.IsEquipped("Y-38");
bool z = CharmsEvolveApi.IsEquipped("Z-38");
int copies = CharmsEvolveApi.GetCopyCount(38);
```

组合判断：

```csharp
bool dreamShield = CharmsEvolveApi.GetCopyCount(38) > 0;
bool heavyBlow = CharmsEvolveApi.GetCopyCount(15) > 0;
bool dreamWielder = CharmsEvolveApi.GetCopyCount(30) > 0;

if (dreamShield && heavyBlow && dreamWielder)
{
    // 盾伤害 ×1.5
}
```

需要重新应用状态时：

```csharp
CharmsEvolveApi.EquipmentChanged += Refresh;
```

修改通用攻击伤害：

```csharp
CharmsEvolveApi.BeforeOutgoingDamage += ctx =>
{
    if (CharmsEvolveApi.IsEquipped("Z-19") &&
        ctx.AttackType.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        ctx.Multiplier *= 1.15f;
    }
};
```
