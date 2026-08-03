using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CharmsEvolve.Interop;

namespace CharmsEvolve.Data
{
    /// <summary>
    /// 由 Design/hollowKnight_CharmsEvolve.xlsx 生成。
    /// 40 个原版槽位 × 3 个复制形态 = 120 个自定义护符。
    /// </summary>
    public static class CharmDatabase
    {
        private static readonly BaseCharmDefinition[] BaseDefinitions =
        {
            new BaseCharmDefinition(1, "聚集蜂群", "Gathering Swarm", "掉落 Geo 自动飞向骑士", "", 1, 1, "蜜蜂和蜘蛛召唤物伤害+100%", "", "任性的指南针：即时获得金钱", "蜜蜂和蜘蛛召唤物伤害+100%", "蜜蜂和蜘蛛召唤物伤害+100%", "蜜蜂和蜘蛛召唤物伤害+100%", new string[] {  }),
            new BaseCharmDefinition(2, "任性的指南针", "Wayward Compass", "地图显示玩家位置", "", 1, 1, "减少大部分护符需要的槽位数量", "", "1", "减少易碎仿制品护符槽位", "减少白王复制品护符槽位", "减少梦境印刻物护符槽位", new string[] {  }),
            new BaseCharmDefinition(3, "幼虫之歌", "Grubsong", "受伤获得 15 灵魂", "编织者之歌：织布者会在攻击敌人时为骑士提供灵魂（每次命中可获得3点灵魂），甚至还能从通常不会掉落灵魂的敌人身上收集灵魂", 1, 1, "法术扭曲者：释放法术时受伤＋8灵魂", "", "蜂巢之血：蜂巢之血再生期间受伤获得灵魂（阶段 1 得 5，阶段 2 得 10）；搭配蜕变挽歌额外 +5", "受伤获得 15 灵魂", "受伤获得 15 灵魂", "受伤获得 15 灵魂", new string[] { "编织者之歌：织布者会在攻击敌人时为骑士提供灵魂（每次命中可获得3点灵魂），甚至还能从通常不会掉落灵魂的敌人身上收集灵魂" }),
            new BaseCharmDefinition(4, "坚硬外壳", "Stalwart Shell", "受伤无敌时间 +0.5s", "", 2, 1, "2稳定之体+虚空之心：受到的所有2血伤害变为1血伤害", "", "巴德尔之壳：（造成壳受损时）受伤后的无敌时间 +0.35 秒", "受伤无敌时间 +0.5s", "受伤无敌时间 +0.5s", "受伤无敌时间 +0.5s", new string[] { "巴德尔之壳：造成壳受损时无敌时间 +0.35 秒" }),
            new BaseCharmDefinition(5, "巴德尔之壳", "Baldur Shell", "专注时生成护盾，最多 4 次", "", 2, 1, "亡者之怒：完全破碎后，亡怒状态下击杀会修复壳", "", "蜂巢之血：只要未受伤害，外壳会随时间恢复(4级外壳15秒，3级20秒，2级24秒，1级27秒）", "加多一个盾，再多抗4次", "加多一个盾，再多抗4次", "加多一个盾，再多抗4次", new string[] { "亡者之怒+Z：碎一个壳进入亡怒状态，加血扣血会中断", "生命血核心+Y：碎1个壳后加4生命血", "生命血之心+X：碎1个壳后加2生命血" }),
            new BaseCharmDefinition(6, "亡者之怒", "Fury of the Fallen", "生命 ≤1 时，钉子伤害 ×1.75", "发光子宫/蜕变挽歌", 2, 1, "27", "5//28Z", "无忧旋律：王者之怒未激活时骨钉攻击有 1% 暴击几率（1.25 倍伤害）；每缺失 1 格面具，几率提升", "生命 ≤1 时，钉子伤害 +75%", "生命 ≤1 时，钉子伤害 +75%", "生命 ≤1 时，钉子伤害 +75%", new string[] {  }),
            new BaseCharmDefinition(7, "快速聚集", "Quick Focus", "专注时间 -33%", "", 3, 1, "29+34", "", "超冲cd-0.3s", "专注时间*(1-33%)", "专注时间*(1-33%)", "专注时间*(1-33%)", new string[] { "超冲cd-0.3s" }),
            new BaseCharmDefinition(8, "生命血之心", "Lifeblood Heart", "多2 生命血", "", 2, 1, "易碎生命：3生命血", "27", "", "多2 生命血", "多2 生命血", "多2 生命血", new string[] { "易碎生命：3生命血", "乔尼的祝福" }),
            new BaseCharmDefinition(9, "生命血核心", "Lifeblood Core", "多4 生命血", "", 3, 2, "易碎生命：7生命血", "27", "", "多4 生命血", "多4 生命血", "多4 生命血", new string[] { "易碎生命：7生命血", "乔尼的祝福" }),
            new BaseCharmDefinition(10, "防御者纹章", "Defender’s Crest", "持续毒云，约 1 伤害/帧区间", "吸虫之巢/蘑菇孢子/22", 1, 1, "吸虫之巢：毒气持续时间+50%", "", "亡者之怒：在亡怒状态下增强防御者纹章的效果,毒云伤害 +2 且生成频率翻倍", "伤害＋100%", "伤害＋100%", "伤害＋100%", new string[] { "其他毒气相关伤害也叠加", "吸虫之巢：毒气持续时间+50%" }),
            new BaseCharmDefinition(11, "吸虫之巢", "Flukenest", "复仇之魂 → 吸虫（单只 9 伤害 × 多段）", "10", 3, 1, "10", "", "伤害提升至 5", "吸虫数量＋100%", "吸虫数量＋100%", "吸虫数量＋100%", new string[] { "10" }),
            new BaseCharmDefinition(12, "荆棘之怒", "Thorns of Agony", "受伤反击，约 16 伤害", "", 1, 1, "蘑菇孢子：荆棘伤害×150%", "骄傲印记：荆棘判定范围*120%", "", "给反伤过程中多加一段伤害判定", "给反伤过程中多加一段伤害判定", "给反伤过程中多加一段伤害判定", new string[] { "蘑菇孢子：荆棘伤害×150%", "13" }),
            new BaseCharmDefinition(13, "骄傲印记", "Mark of Pride", "钉子长度 +25%", "", 3, 1, "12", "", "", "钉子长度 +25%", "钉子长度 +25%", "钉子长度 +25%", new string[] { "荆棘之怒：荆棘判定范围*120%", "蜕变挽歌：增加35%剑气长度" }),
            new BaseCharmDefinition(14, "稳定之体", "Steady Body", "攻击无后坐力", "", 1, 1, "4+36", "", "消除硬着陆效果", "国王之魂+飞毛腿：空中按住空格缓降", "虚空之心＋冲刺大师：空中多一次冲刺", "国王之魂+虚空之心：空中多一次跳跃", new string[] {  }),
            new BaseCharmDefinition(15, "沉重之击", "Heavy Blow", "击退 +50%", "", 2, 1, "骨钉大师荣耀：蓄力斩在时间刚好时释放+50%伤害", "30+38", "快速劈砍：使骨钉伤害提升 20%", "击退 +50%", "击退 +50%", "击退 +50%", new string[] { "骨钉大师荣耀：蓄力斩在时间刚好时释放+50%伤害", "30+38" }),
            new BaseCharmDefinition(16, "锋利之影", "Sharp Shadow", "暗影冲刺造成 相当于钉伤的法伤", "冲刺大师：将暗影造成的伤害提升至钉子伤害的1.5倍。不影响充能或冷却时间。", 2, 1, "冲刺大师+飞毛腿：暗影冲刺cd*80%", "", "飞毛腿：+无敌帧0.25s", "伤害＋100%", "伤害＋100%", "伤害＋100%", new string[] { "冲刺大师：伤害+50%", "冲刺大师+飞毛腿：暗影冲刺cd*80%", "虚空之心 + 灵魂捕手：使用锋利之影击中敌人时获得 8 灵魂" }),
            new BaseCharmDefinition(17, "蘑菇孢子", "Spore Shroom", "专注时毒云，约 2 DPS", "深度聚集：孢子云半径增加35%", 1, 1, "乌恩之形：孢子持续时间+50%", "12", "", "聚集时多释放一团孢子云", "聚集时多释放一团孢子云", "聚集时多释放一团孢子云", new string[] { "乌恩之形：孢子持续时间+50%", "深度聚集：孢子云半径增加35%" }),
            new BaseCharmDefinition(18, "修长之钉", "Longnail", "钉子长度 +15%", "", 2, 1, "35", "", "", "钉子长度 +15%", "钉子长度 +15%", "钉子长度 +15%", new string[] { "35" }),
            new BaseCharmDefinition(19, "萨满之石", "Shaman Stone", "法术伤害 +33%", "", 3, 1, "26+36（国王之魂）", "", "国王之魂：灵魂全满时左右挥击骨钉有 11% 几率释放消耗 11 灵魂的复仇之魂；每填满 1 格灵魂容器，几率 +11%", "法术伤害 +33%", "法术伤害 +33%", "法术伤害 +33%", new string[] {  }),
            new BaseCharmDefinition(20, "灵魂捕手", "Soul Catcher", "钉子命中 +3 灵魂", "", 2, 1, "发光子宫:子宫生成冷却时间 -1 秒", "梦之盾：梦之盾攻击+6点灵魂", "国王之魂：每两秒获得的灵魂 +1", "钉子命中 +3 灵魂", "钉子命中 +3 灵魂", "钉子命中 +3 灵魂", new string[] { "国王之魂：每两秒获得的灵魂 +1", "梦之盾：梦之盾攻击+6点灵魂", "36+16" }),
            new BaseCharmDefinition(21, "噬魂者", "Soul Eater", "钉子命中 +8 灵魂", "", 4, 2, "虚空之心 + 锋利之影：使用锋利之影击中敌人时获得 20 灵魂\n", "梦之盾：盾牌击中回复16点灵魂", "国王之魂：每两秒获得的灵魂+3", "钉子命中 +8 灵魂", "钉子命中 +8 灵魂", "钉子命中 +8 灵魂", new string[] { "国王之魂：每两秒获得的灵魂+3", "梦之盾：盾牌击中回复16点灵魂", "36+16" }),
            new BaseCharmDefinition(22, "发光子宫", "Glowing Womb", "消耗灵魂生成幼体（9 伤害/只）", "防御者纹章", 2, 1, "1", "", "伤害 +1（搭配守护者纹章时 +4）", "数量＋4", "数量＋4", "数量＋4", new string[] { "亡者之怒：进入亡怒状态，幼体伤害增加", "33" }),
            new BaseCharmDefinition(23, "易碎心脏", "Fragile Heart", "+2 生命", "", 2, 1, "24", "8//9", "", "+2 生命", "+2 生命", "+2 生命", new string[] {  }),
            new BaseCharmDefinition(24, "易碎贪婪", "Fragile Greed", "Geo 获取 ×1.2", "", 2, 1, "原倍率变为2倍//搭配易碎/坚固力量geo再×（1+geo倍率×2）", "搭配易碎/坚固血量再×（1+geo倍率×2）", "噬魂者：获得的吉欧量等额转化为灵魂", "Geo 获取 ×1.2", "Geo 获取 ×1.2", "Geo 获取 ×1.2", new string[] {  }),
            new BaseCharmDefinition(25, "易碎力量", "Fragile Strength", "钉子伤害 ×1.5", "", 3, 1, "24", "易碎心脏：伤害再提高0.25", "", "钉子伤害 +50%", "钉子伤害 +50%", "钉子伤害 +50%", new string[] {  }),
            new BaseCharmDefinition(26, "骨钉大师荣耀", "Nailmaster’s Glory", "蓄力时间 -44%", "", 1, 1, "萨满之石+国王之魂：旋风劈砍和冲刺劈砍造成法术伤害,不会产生灵魂可穿透护盾", "15", "", "蓄力时间*(1-44%)", "蓄力时间*(1-44%)", "蓄力时间*(1-44%)", new string[] {  }),
            new BaseCharmDefinition(27, "乔尼的祝福", "Joni’s Blessing", "生命 → 生命血，血量 ×1.4", "", 4, 2, "搭配生命血之心，血量乘数+0.4//搭配生命血核心，血量乘数+0.7", "亡者之怒伤害加成全程触发", "生命值倍率提升至1.5倍", "生命乘数加1", "生命乘数加1", "生命乘数加1", new string[] { "8", "9" }),
            new BaseCharmDefinition(28, "乌恩之形", "Shape of Unn", "专注时可移动", "", 2, 1, "17", "", "", "蜂巢之血：蜂巢血持续恢复", "蜂巢之血：蜂巢血恢复过程不会被打断", "亡者之怒：亡怒状态下的乌恩状态不可选中", new string[] { "17" }),
            new BaseCharmDefinition(29, "蜂巢之血", "Hiveblood", "10s 回复 1 点生命", "", 4, 1, "快速聚集+深度聚集:速度×2,即5s恢复1点生命（乔尼祝福为10s）", "28X//28Y", "3", "同时多恢复的生命值*2", "同时多恢复的生命值*2", "同时多恢复的生命值*2", new string[] { "快速聚集+深度聚集:恢复再速度×2" }),
            new BaseCharmDefinition(30, "舞梦者", "Dream Wielder", "梦钉挥动时间*50%，命中 +33 灵魂", "", 1, 1, "15+38", "32+38", "1+24：在梦境中获取geo", "梦钉挥动时间*50%，命中 +33 灵魂", "梦钉挥动时间*50%，命中 +33 灵魂", "梦钉挥动时间*50%，命中 +33 灵魂", new string[] { "15+38", "32+38" }),
            new BaseCharmDefinition(31, "冲刺大师", "Dashmaster", "冲刺冷却 -33%", "16", 2, 1, "16+37", "", "", "冲刺冷却*（1-33%）", "冲刺冷却*（1-33%）", "冲刺冷却*（1-33%）", new string[] {  }),
            new BaseCharmDefinition(32, "快速劈砍", "Quick Slash", "攻击间隔 -39%", "", 3, 1, "30+38", "", "15", "攻击间隔 *（1-39%）", "攻击间隔 *（1-39%）", "攻击间隔 *（1-39%）", new string[] { "30+38" }),
            new BaseCharmDefinition(33, "法术扭曲者", "Spell Twister", "法术消耗 -9", "", 2, 1, "3", "发光子宫：制造一个幼体的灵魂消耗-2", "虚空之心：已升级法术的消耗额外 -4", "法术消耗 -9", "法术消耗 -9", "法术消耗 -9", new string[] { "发光子宫：制造一个幼体的灵魂消耗-2" }),
            new BaseCharmDefinition(34, "深度聚集", "Deep Focus", "专注回血 ×2，时间 ×2", "", 4, 1, "7+29", "", "使水晶之心的伤害翻3倍，cd+0.2s", "专注回血×2，时间＋100%", "专注回血×2，时间＋100%", "专注回血×2，时间＋100%", new string[] { "17" }),
            new BaseCharmDefinition(35, "蜕变挽歌", "Grubberfly’s Elegy", "满血时发射光波（钉伤50%）", "骄傲印记增长35%剑气长度", 3, 1, "修长之钉：剑气击中也回复灵魂", "", "27", "多一段剑气", "多一段剑气", "多一段剑气", new string[] { "修长之钉：第n剑气击中也回复灵魂", "13" }),
            new BaseCharmDefinition(36, "虚空之心", "Void Heart", "剧情 / 虚空判定", "", 0, 0, "4+14", "", "33", "1级格林之子：所有格林之子攻速*2", "国王之魂 Kingsoul", "无忧旋律 Carefree Melody", new string[] {  }),
            new BaseCharmDefinition(37, "飞毛腿", "Sprintmaster", "移动速度 +20%", "31：进一步将移动速度加成提升至39%", 1, 1, "16+31", "", "行走速度增加0.85f", "移动速度 +20%", "移动速度 +20%", "移动速度 +20%", new string[] { "冲刺大师：进一步将移动速度加成提升至39%" }),
            new BaseCharmDefinition(38, "梦之盾", "Dreamshield", "旋转盾，约 1.5s/次，13伤害", "30：大小提升15%", 3, 1, "沉重之击+舞梦者：盾伤害*1.5//快速劈砍＋舞梦者：盾攻击后恢复时间-20%", "20//21", "", "加多一个盾", "加多一个盾", "加多一个盾", new string[] { "沉重之击+舞梦者：盾伤害*1.5", "快速劈砍＋舞梦者：盾攻击后恢复时间-20%", "20//21" }),
            new BaseCharmDefinition(39, "编织者之歌", "Weaversong", "蜘蛛攻击 3 伤害/次", "飞毛腿：织网者移动速度提升50%", 2, 1, "1", "", "将编织者之巢伤害提升至5/7", "加3个编织者", "加3个编织者", "加3个编织者", new string[] { "1" }),
            new BaseCharmDefinition(40, "格林之子", "Grimmchild", "火球攻击，5 → 11 伤害（随阶段）", "", 2, 1, "41", "", "", "2级格林之子", "无忧旋律 Carefree Melody", "3级格林之子", new string[] {  })
        };

        private static readonly Dictionary<int, BaseCharmDefinition> AlternateForms =
            new Dictionary<int, BaseCharmDefinition>()
        {
            { 36, new BaseCharmDefinition(36, "国王之魂", "Kingsoul", "每两秒恢复4点灵魂", "", 5, 2, "33", "19+26", "20/21", "每两秒恢复4点灵魂", "每两秒恢复4点灵魂", "每两秒恢复4点灵魂", new string[] { "20", "21" }) },
            { 40, new BaseCharmDefinition(40, "无忧旋律", "Carefree Melody", "在受伤时按照概率抵挡伤害一次，没受伤0％10％20％30％50％70％80％，第7次90％。触发效果重置受伤害次数，但回血、坐椅子和进出梦境不重置该次数", "", 2, 1, "无忧旋律：所有格林之子伤害*2", "", "乔尼的祝福 ：抵消伤害时获得 1 点生命血；若同时装备易碎心脏/不屈心脏且已获得生命血核心，额外获得 1 点生命血", "在受伤时按照概率抵挡伤害一次，没受伤0％10％20％30％50％70％80％，第7次90％。触发效果重置受伤害次数，但回血、坐椅子和进出梦境不重置该次数", "在受伤时按照概率抵挡伤害一次，没受伤0％10％20％30％50％70％80％，第7次90％。触发效果重置受伤害次数，但回血、坐椅子和进出梦境不重置该次数", "在受伤时按照概率抵挡伤害一次，没受伤0％10％20％30％50％70％80％，第7次90％。触发效果重置受伤害次数，但回血、坐椅子和进出梦境不重置该次数", new string[] { "多一段判定，叠加判断", "概率叠加，第一次受伤概率=0.81，第二次＝0.64，第三次=0.49，第四次=0.25，第5次=0.16，第6次=0.09，第7次=0.01" }) }
        };

        private static readonly List<CopyCharmDefinition> Copies = BuildCopies();
        private static readonly Dictionary<string, CopyCharmDefinition> CopyByKey = BuildCopyMap();

        public static ReadOnlyCollection<CopyCharmDefinition> AllCopies
        {
            get { return Copies.AsReadOnly(); }
        }

        public static BaseCharmDefinition GetBase(int originalId)
        {
            if (originalId < 1 || originalId > BaseDefinitions.Length)
                throw new ArgumentOutOfRangeException("originalId");
            return ResolveCurrentForm(BaseDefinitions[originalId - 1]);
        }

        public static CopyCharmDefinition GetCopy(string key)
        {
            CopyCharmDefinition result;
            return key != null && CopyByKey.TryGetValue(key, out result)
                ? RefreshForm(result)
                : null;
        }

        public static IList<CopyCharmDefinition> GetPage(CopyKind kind)
        {
            List<CopyCharmDefinition> result = new List<CopyCharmDefinition>(40);
            for (int i = 0; i < Copies.Count; i++)
                if (Copies[i].Kind == kind)
                    result.Add(RefreshForm(Copies[i]));
            return result;
        }

        private static BaseCharmDefinition ResolveCurrentForm(BaseCharmDefinition source)
        {
            BaseCharmDefinition alternate;
            if (source.OriginalId == 36 && AlternateForms.TryGetValue(36, out alternate))
            {
                return GameReflection.IsVoidHeartForm() ? source : alternate;
            }

            if (source.OriginalId == 40 && AlternateForms.TryGetValue(40, out alternate))
            {
                return GameReflection.IsCarefreeMelodyForm() ? alternate : source;
            }

            return source;
        }

        private static CopyCharmDefinition RefreshForm(CopyCharmDefinition copy)
        {
            BaseCharmDefinition current = GetBase(copy.OriginalId);
            return CreateCopy(current, copy.Kind);
        }

        private static List<CopyCharmDefinition> BuildCopies()
        {
            List<CopyCharmDefinition> result = new List<CopyCharmDefinition>(120);
            for (int kind = 0; kind < 3; kind++)
            {
                for (int i = 0; i < BaseDefinitions.Length; i++)
                    result.Add(CreateCopy(BaseDefinitions[i], (CopyKind)kind));
            }
            return result;
        }

        private static Dictionary<string, CopyCharmDefinition> BuildCopyMap()
        {
            Dictionary<string, CopyCharmDefinition> result =
                new Dictionary<string, CopyCharmDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Copies.Count; i++)
                result[Copies[i].Key] = Copies[i];
            return result;
        }

        private static CopyCharmDefinition CreateCopy(BaseCharmDefinition source, CopyKind kind)
        {
            string description;
            switch (kind)
            {
                case CopyKind.FragileReplica: description = source.CopyX; break;
                case CopyKind.PaleKingReplica: description = source.CopyY; break;
                default: description = source.CopyZ; break;
            }

            if (string.IsNullOrEmpty(description))
                description = source.BaseEffect;

            return new CopyCharmDefinition
            {
                Key = CharmKey.For(source.OriginalId, kind),
                RuntimeId = CharmKey.RuntimeId(source.OriginalId, kind),
                OriginalId = source.OriginalId,
                Kind = kind,
                Cost = source.CopyCost,
                NameZh = source.NameZh,
                NameEn = source.NameEn,
                Description = description,
                SourceEffect = source.BaseEffect,
                VanillaSynergy = source.VanillaSynergy,
                EnhancedSynergy = source.EnhancedSynergy,
                VoidKnight = source.VoidKnight,
                LegacyEnhancement = source.LegacyEnhancement,
                StackableSynergies = source.StackableSynergies
            };
        }
    }
}
