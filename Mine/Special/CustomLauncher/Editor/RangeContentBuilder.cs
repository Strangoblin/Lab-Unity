using System;
using System.Collections.Generic;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using UnityEditor;
using UnityEngine;

namespace CelToon.CustomLauncher.Editor
{
    public static class RangeContentBuilder
    {
        private const string Path = RangeSceneBuilder.RootPath + "/Samples/Definitions/";
        private static readonly List<PartDefinition> parts = new List<PartDefinition>();
        private static EffectStep E(EffectOperation op, float a = 1f, float b = 1f, int count = 1, float delay = 0f) => new EffectStep(op, a, b, count, delay);
        public static PartCatalog Build()
        {
            parts.Clear();
            var standard = Round("StandardRound", "标准弹", "均衡弹丸，伤害 12，速度 38。", 12, 38, .09f, 3, 1, Color.cyan);
            var heavy = Round("HeavyRound", "重型弹", "大弹体、高伤害，飞行较慢。", 30, 22, .17f, 4, 3, new Color(1f, .65f, .12f));
            Round("Needle", "针弹", "小半径、高速，适合精确追踪与穿透。", 8, 65, .035f, 2, .8f, Color.white);
            Round("Slug", "巨型弹", "伤害 48，半径 0.28，速度 18。", 48, 18, .28f, 4, 4, Color.yellow);
            Round("Spark", "火花弹", "轻量橙色弹体，适合多发组合；燃烧由伤害槽配置。", 6, 32, .07f, 2, .5f, new Color(1f, .3f, .05f));
            Round("FrostOrb", "冰晶球", "蓝色慢速球体，适合追踪；减速由伤害槽配置。", 16, 20, .2f, 5, 1.5f, new Color(.2f, .6f, 1f));
            Round("ToxicOrb", "毒液球", "绿色长寿命球体，适合延迟分裂。", 10, 24, .13f, 6, 1.2f, Color.green);
            Round("ArcBolt", "电弧弹", "紫色高速弹体，适合连锁效果。", 14, 48, .075f, 3, 1.8f, new Color(.75f, .25f, 1f));
            Round("Pebble", "碎石", "廉价低伤害，适合密集分布和分裂。", 4, 27, .12f, 3, .25f, new Color(.6f, .6f, .6f));
            Round("Longshot", "远射弹", "高速、长寿命的远程载荷。", 20, 80, .05f, 8, 2.5f, new Color(.8f, .95f, 1f));
            var single = Spread("Single", "单发", "沿瞄准方向发射一颗弹丸。", DistributionMode.Single, 1, 0);
            var fan = Spread("Fan", "五发扇形", "5 发，24° 水平扇形。", DistributionMode.Fan, 5, 24);
            Spread("WideFan", "宽幅散射", "9 发，70° 扇形覆盖。", DistributionMode.Fan, 9, 70);
            Spread("TightFan", "密集三发", "3 发，8° 扇形，集中打击。", DistributionMode.Fan, 3, 8);
            var ring = Spread("Ring", "环形八发", "水平 360°，均匀向八个方向射击。", DistributionMode.Ring, 8, 0);
            Spread("DenseRing", "环形十六发", "水平 360°，16 个方向。", DistributionMode.Ring, 16, 0);
            var cone = Spread("Cone", "立体散射", "12 发，以固定螺旋采样填充 35° 锥形。", DistributionMode.Cone, 12, 35);
            Spread("NarrowCone", "聚焦锥束", "7 发，12° 锥形。", DistributionMode.Cone, 7, 12);
            Spread("Parallel", "平行排射", "5 个并排枪口，方向一致。", DistributionMode.Parallel, 5, 0);
            Spread("Cross", "十字分布", "中心及水平、垂直四方向，共 5 发。", DistributionMode.Cross, 5, 24);
            var gravity = Effect("Gravity", "抛物线", PartKind.Trajectory, "受到向下 9.8 m/s² 加速度。", .4f, E(EffectOperation.Gravity, 9.8f));
            Effect("LowGravity", "轻飘轨迹", PartKind.Trajectory, "较小重力，延长平射距离。", .3f, E(EffectOperation.Gravity, 3));
            var homing = Effect("Homing", "追踪", PartKind.Trajectory, "在 25 m 内寻找目标，以有限转向速度追踪。", 1, E(EffectOperation.Homing, 3, 25));
            Effect("StrongHoming", "强追踪", PartKind.Trajectory, "45 m 搜索范围，快速转向。", 2, E(EffectOperation.Homing, 7, 45));
            Effect("Wave", "波浪", PartKind.Trajectory, "横向振幅 0.65 m，角频率 12。", .5f, E(EffectOperation.Wave, .65f, 12));
            var helix = Effect("Helix", "螺旋", PartKind.Trajectory, "沿飞行轴绕行，半径 0.45 m。", .6f, E(EffectOperation.Helix, .45f, 14));
            Effect("Accelerating", "加速", PartKind.Trajectory, "沿当前速度方向加速 18 m/s²。", .5f, E(EffectOperation.Acceleration, 18));
            Effect("Drag", "阻力", PartKind.Trajectory, "速度指数衰减，可与定时分裂组合。", .2f, E(EffectOperation.Drag, .7f));
            Effect("Return", "回旋", PartKind.Trajectory, "飞行 0.55 s 后转向本次发射位置。", .8f, E(EffectOperation.Return, 6, 1, 1, .55f));
            Effect("SeekingHelix", "螺旋追踪", PartKind.Trajectory, "同一配件叠加螺旋位移与目标追踪。", 1.8f, E(EffectOperation.Homing, 3, 30), E(EffectOperation.Helix, .3f, 12));
            Effect("WaveGravity", "波浪抛物线", PartKind.Trajectory, "同时施加重力和横向波浪。", .9f, E(EffectOperation.Gravity, 6), E(EffectOperation.Wave, .5f, 10));
            var burn = Effect("Burn", "燃烧", PartKind.Damage, "命中施加 4 秒燃烧，每秒伤害 6。", .8f, E(EffectOperation.Burn, 6, 4));
            var poison = Effect("Poison", "中毒", PartKind.Damage, "命中施加 7 秒中毒，每秒伤害 4。", .8f, E(EffectOperation.Poison, 4, 7));
            var slow = Effect("Slow", "冰冻减速", PartKind.Damage, "目标移动速度变为 30%，持续 4 秒。", .7f, E(EffectOperation.Slow, .3f, 4));
            Effect("Stun", "眩晕", PartKind.Damage, "暂停靶标巡航 1.5 秒。", 1, E(EffectOperation.Stun, 1, 1.5f));
            var mark = Effect("Mark", "易伤标记", PartKind.Damage, "后续伤害放大至 1.5 倍，持续 5 秒。", .8f, E(EffectOperation.Mark, 1.5f, 5));
            Effect("BonusDamage", "额外打击", PartKind.Damage, "命中后追加一次 50% 基础伤害。", .7f, E(EffectOperation.BonusDamage, .5f));
            Effect("FirePoison", "火毒双属性", PartKind.Damage, "一个槽位同时施加燃烧和中毒。", 1.5f, E(EffectOperation.Burn, 5, 4), E(EffectOperation.Poison, 3, 6));
            Effect("FrostMark", "寒霜易伤", PartKind.Damage, "减速并标记，后续攻击增伤。", 1.5f, E(EffectOperation.Slow, .4f, 4), E(EffectOperation.Mark, 1.4f, 4));
            Effect("ShockStun", "震击", PartKind.Damage, "追加伤害并短暂眩晕。", 1.4f, E(EffectOperation.BonusDamage, .35f), E(EffectOperation.Stun, 1, .8f));
            var explosion = Effect("Explosion", "爆炸", PartKind.Impact, "命中时产生 3 m 范围额外伤害；直接命中和爆炸可以叠加。", 1.2f, E(EffectOperation.Explosion, 3, .75f));
            Effect("WideExplosion", "广域爆炸", PartKind.Impact, "5 m 范围，额外伤害为基础伤害的 45%。", 1.8f, E(EffectOperation.Explosion, 5, .45f));
            Effect("DoubleExplosion", "双重爆炸", PartKind.Impact, "一次命中执行两次爆炸：近距强爆与远距弱爆。", 2, E(EffectOperation.Explosion, 2, 1), E(EffectOperation.Explosion, 4, .4f));
            var bounce = Effect("Bounce", "弹跳三次", PartKind.Impact, "碰撞后反射，最多延续 3 次。", .6f, E(EffectOperation.Bounce, 1, 1, 3));
            Effect("DoubleBounce", "双弹跳组件", PartKind.Impact, "内部两个弹跳单元各提供 2 次延续，总计 4 次。", 1, E(EffectOperation.Bounce, 1, 1, 2), E(EffectOperation.Bounce, 1, 1, 2));
            var pierce = Effect("Pierce", "穿透三次", PartKind.Impact, "穿过 3 个碰撞体，同一碰撞体不重复结算。", .9f, E(EffectOperation.Pierce, 1, 1, 3));
            var chain = Effect("Chain", "连锁闪电", PartKind.Impact, "从命中点链向 6 m 内最多 3 个其他目标，各受 60% 伤害。", 1.3f, E(EffectOperation.Chain, 6, .6f, 3));
            Effect("Push", "击退", PartKind.Impact, "沿飞行方向施加冲量，靶场可观察位移。", .5f, E(EffectOperation.Push, 8));
            Effect("Pull", "牵引", PartKind.Impact, "向反方向施加冲量，将目标拉向射击者。", .5f, E(EffectOperation.Pull, 8));
            Effect("ExplosivePierce", "穿透爆破", PartKind.Impact, "穿透两次，每次接触产生范围爆炸。", 2, E(EffectOperation.Pierce, 1, 1, 2), E(EffectOperation.Explosion, 2.5f, .5f));
            Effect("ChainPush", "连锁冲击", PartKind.Impact, "命中目标受击退，同时伤害附近目标。", 1.6f, E(EffectOperation.Push, 6), E(EffectOperation.Chain, 6, .4f, 2));
            var split = Effect("TimedSplit", "定时分裂", PartKind.Trigger, "0.25 s 后生成 3 颗碎片；子弹继承效果，但不再次分裂。", .8f, E(EffectOperation.TimedSplit, 30, .8f, 3, .25f));
            var doubleSplit = Effect("DoubleSplit", "双分裂", PartKind.Trigger, "一个配件在 0.20 s 和 0.40 s 各触发一次三发分裂。", 1.3f,
                E(EffectOperation.TimedSplit, 24, .9f, 3, .2f), E(EffectOperation.TimedSplit, 45, .7f, 3, .4f));
            Effect("TripleSplit", "三段分裂", PartKind.Trigger, "0.15 / 0.30 / 0.45 s 各生成两颗碎片。", 1.6f,
                E(EffectOperation.TimedSplit, 18, 1, 2, .15f), E(EffectOperation.TimedSplit, 35, .8f, 2, .3f), E(EffectOperation.TimedSplit, 55, .7f, 2, .45f));
            var hitSplit = Effect("HitSplit", "命中分裂", PartKind.Trigger, "第一次接触时生成 5 颗碎片。", .9f, E(EffectOperation.HitSplit, 60, .8f, 5));
            Effect("DoubleHitSplit", "命中双分裂", PartKind.Trigger, "一次接触执行两组分裂，每组三颗，角度不同。", 1.5f, E(EffectOperation.HitSplit, 25, 1, 3), E(EffectOperation.HitSplit, 70, .6f, 3));
            Effect("ExpirySplit", "终寿分裂", PartKind.Trigger, "寿命耗尽时生成五颗碎片；提前碰撞终止不触发。", .6f, E(EffectOperation.ExpirySplit, 100, .6f, 5));
            Effect("TimedHitSplit", "时间命中双触发", PartKind.Trigger, "先定时生成两颗；后续命中再生成三颗。", 1.4f, E(EffectOperation.TimedSplit, 25, .8f, 2, .2f), E(EffectOperation.HitSplit, 60, .7f, 3));
            Effect("LateSplit", "延迟散花", PartKind.Trigger, "0.7 s 后生成八颗宽角碎片。", 1.2f, E(EffectOperation.TimedSplit, 150, .5f, 8, .7f));
            Effect("DamageBoost", "增伤", PartKind.Modifier, "基础伤害 ×1.8。", .8f, E(EffectOperation.DamageScale, 1.8f));
            Effect("SpeedBoost", "高速", PartKind.Modifier, "弹速 ×1.7。", .6f, E(EffectOperation.SpeedScale, 1.7f));
            Effect("BigRound", "大型化", PartKind.Modifier, "半径 ×2。", .5f, E(EffectOperation.RadiusScale, 2));
            var longLife = Effect("LongLife", "长寿命", PartKind.Modifier, "寿命 ×2。", .5f, E(EffectOperation.LifetimeScale, 2));
            Effect("Economy", "节能", PartKind.Modifier, "整次费用 ×0.6。", 0, E(EffectOperation.CostScale, .6f));
            Effect("HeavyPower", "重装强化", PartKind.Modifier, "伤害 ×2、半径 ×1.5，速度 ×0.7。", 1.2f, E(EffectOperation.DamageScale, 2), E(EffectOperation.RadiusScale, 1.5f), E(EffectOperation.SpeedScale, .7f));
            Effect("NeedleTune", "针刺调校", PartKind.Modifier, "速度 ×2、半径 ×0.5、伤害 ×0.8。", .5f, E(EffectOperation.SpeedScale, 2), E(EffectOperation.RadiusScale, .5f), E(EffectOperation.DamageScale, .8f));
            Effect("DoublePower", "双增伤", PartKind.Modifier, "内部两次 ×1.4 增伤，合计 ×1.96。", 1, E(EffectOperation.DamageScale, 1.4f), E(EffectOperation.DamageScale, 1.4f));
            var burst = Effect("Burst", "三连发", PartKind.FireControl, "同一次开火以 0.12 s 间隔提交三组弹丸。", .5f, E(EffectOperation.Burst, 1, 1, 3, .12f));
            Effect("DoubleTap", "双击", PartKind.FireControl, "两组弹丸，间隔 0.08 s。", .3f, E(EffectOperation.Burst, 1, 1, 2, .08f));
            Effect("FiveBurst", "五连发", PartKind.FireControl, "五组弹丸，间隔 0.1 s。", .7f, E(EffectOperation.Burst, 1, 1, 5, .1f));
            Effect("Rapid", "快射", PartKind.FireControl, "冷却时间 ×0.5。", .5f, E(EffectOperation.CooldownScale, .5f));
            Effect("SlowFire", "稳射", PartKind.FireControl, "冷却时间 ×1.6，便于逐发观察。", 0, E(EffectOperation.CooldownScale, 1.6f));
            Effect("DoubleBurst", "双连发组件", PartKind.FireControl, "两个双击单元合成四组弹丸，间隔 0.1 s。", .6f, E(EffectOperation.Burst, 1, 1, 2, .1f), E(EffectOperation.Burst, 1, 1, 2, .1f));
            var catalog = Asset<PartCatalog>("Catalog", _ => { });
            catalog.Parts = parts.ToArray();
            catalog.Recipes = new[] {
                Recipe("Custom", "自定义 / 空配方", "八类全开，所有槽位为空，从零组合。"),
                Recipe("Precision", "精确单发", "标准弹；其余槽位为空。", standard),
                Recipe("Scatter", "散弹", "五发扇形 + 标准弹。", standard, fan),
                Recipe("Grenadier", "抛物线榴弹", "重型弹 + 重力 + 爆炸。用方向键稍抬高枪口。", heavy, gravity, explosion),
                Recipe("Firestorm", "追踪火雨", "标准弹 + 立体散射 + 追踪 + 燃烧。", standard, cone, homing, burn),
                Recipe("DoubleSplit", "双分裂试验", "标准弹 + 双分裂；观察分裂事件计数。", standard, doubleSplit),
                Recipe("Electric", "连锁冰电", "追踪 + 连锁 + 减速。", standard, homing, chain, slow),
                Recipe("Spiral", "螺旋毒弹", "重型弹 + 螺旋 + 中毒 + 长寿命。", heavy, helix, poison, longLife),
                Recipe("Ricochet", "环形弹跳", "环形八发，碰撞后反射。", standard, ring, bounce),
                Recipe("Piercing", "穿透标记", "穿透弹 + 易伤标记 + 三连发。", standard, pierce, mark, burst),
                Recipe("Fragment", "分裂爆弹", "重型弹 + 命中分裂 + 爆炸。", heavy, hitSplit, explosion),
                Recipe("Timed", "定时分裂", "标准弹 + 一次定时分裂。", standard, split)
            };
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); return catalog;
        }
        private static ProjectileDefinition Round(string id, string name, string description, float damage, float speed, float radius, float life, float cost, Color color)
        {
            var result = Asset<ProjectileDefinition>(id, a => { a.PartId = "projectile." + id; a.DisplayName = name; a.Description = description;
                a.Damage = damage; a.Speed = speed; a.Radius = radius; a.Lifetime = life; a.EnergyPerProjectile = cost; a.Color = color; });
            parts.Add(result); return result;
        }
        private static DistributionDefinition Spread(string id, string name, string description, DistributionMode mode, int count, float spread)
        {
            var result = Asset<DistributionDefinition>(id, a => { a.PartId = "distribution." + id; a.DisplayName = name; a.Description = description; a.Mode = mode; a.Count = count; a.SpreadDegrees = spread; });
            parts.Add(result); return result;
        }
        private static EffectPartDefinition Effect(string id, string name, PartKind kind, string description, float cost, params EffectStep[] steps)
        {
            var result = Asset<EffectPartDefinition>(id, a => { a.PartId = kind.ToString().ToLowerInvariant() + "." + id;
                a.DisplayName = name; a.Description = description; a.Category = kind; a.EnergyPerProjectile = cost; a.Operations = steps; });
            parts.Add(result); return result;
        }
        private static LauncherRecipeDefinition Recipe(string id, string name, string description, params PartDefinition[] equipped)
        {
            return Asset<LauncherRecipeDefinition>("Recipe_" + id, a => { a.RecipeId = "recipe." + id; a.DisplayName = name;
                a.Description = description; a.Parts = equipped; a.Cooldown = .3f; a.BaseEnergyCost = 1; a.MaxGeneratedPerShot = 1024; });
        }
        private static T Asset<T>(string name, Action<T> initialize) where T : ScriptableObject
        {
            string path = Path + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                if (existing is PartDefinition part && string.IsNullOrEmpty(part.Description))
                {
                    var template = ScriptableObject.CreateInstance<T>(); initialize(template);
                    var definition = (PartDefinition)(ScriptableObject)template; part.DisplayName = definition.DisplayName; part.Description = definition.Description;
                    EditorUtility.SetDirty(part); UnityEngine.Object.DestroyImmediate(template);
                }
                return existing;
            }
            var asset = ScriptableObject.CreateInstance<T>(); initialize(asset); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}
