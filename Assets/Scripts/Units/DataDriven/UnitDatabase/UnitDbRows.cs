// UnitDbRows.cs
// sqlite-net POCOs for units.db + symmetric mappers to the runtime DTOs.
// Column-per-key: every JSON key becomes its own column; nested objects/arrays
// (UnitDisplay, UnitStats, Vector2, hit/effect frames, levels) stay as compact
// JSON text columns. Enums store as TEXT via [StoreAsText] — same string form
// the JSON files use — so row <-> DTO round-trips are lossless.
//
// Abilities are split into per-kind tables (abilities_basic / abilities_bb /
// abilities_leader / abilities_extra). Raw leader/extra skill ids come from
// different id spaces than the skill MST ids used by bb/sbb/ubb and collide
// numerically, so a single shared table would silently lose rows; unit data
// references an ability id together with the field that holds it, and that
// field's kind selects the table. SBB and UBB share the bb table (skill MST
// ids are unique across all three).
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SQLite;
using UnityEngine;

public static class UnitDbJson
{
    // Compact counterpart of DataDrivenJson.Settings: same converters so
    // enums/Vector2 serialize identically, minus the indentation.
    public static readonly JsonSerializerSettings Compact = new JsonSerializerSettings
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = { new StringEnumConverter(), new Vector2Converter() }
    };

    public static string Serialize<T>(T value)
        => value == null ? null : JsonConvert.SerializeObject(value, Compact);

    public static T Deserialize<T>(string json)
        => json == null ? default : JsonConvert.DeserializeObject<T>(json, Compact);

    public static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse(value, out T result) ? result : default;
}

[Table("units")]
public class UnitDbRow
{
    // ── Details ──
    [PrimaryKey] [Column("unit_id")]   public string UnitId { get; set; }
    [Indexed]    [Column("unit_name")] public string UnitName { get; set; }
    [Column("unit_number")]  public string UnitNumber { get; set; }
    [Column("description")]  public string Description { get; set; }
    [Column("summon_desc")]  public string SummonDesc { get; set; }
    [Column("fusion_desc")]  public string FusionDesc { get; set; }
    [Column("evo_desc")]     public string EvoDesc { get; set; }
    [Indexed] [Column("element")] public string Element { get; set; }
    [Indexed] [Column("rarity")]  public string Rarity { get; set; }

    // ── Visual addresses (paths, not assets) ──
    [Column("slot_icon_path")] public string SlotIconPath { get; set; }
    [Column("icon_path")]      public string IconPath { get; set; }
    [Column("full_art_path")]  public string FullArtPath { get; set; }
    [Column("animation_type")] public string AnimationType { get; set; }
    [Column("sprite_sheet_paths")] public string SpriteSheetPathsJson { get; set; }
    [Column("cgg_path")]       public string CggPath { get; set; }
    [Column("idle_cgs_path")]  public string IdleCgsPath { get; set; }
    [Column("move_cgs_path")]  public string MoveCgsPath { get; set; }
    [Column("attack_cgs_path")] public string AttackCgsPath { get; set; }
    [Column("sam_path")]       public string SamPath { get; set; }

    // ── Display values (UnitDisplay / Vector2, one JSON column per key) ──
    [Column("unit_display_home_position")]          public string UnitDisplayHomePositionJson { get; set; }
    [Column("unit_display_detail_position")]        public string UnitDisplayDetailPositionJson { get; set; }
    [Column("unit_display_confirm_image_position")] public string UnitDisplayConfirmImagePositionJson { get; set; }
    [Column("unit_display_cut_in_image_position")]  public string UnitDisplayCutInImagePositionJson { get; set; }
    [Column("unit_display_summon_position")]        public string UnitDisplaySummonPositionJson { get; set; }
    [Column("unit_display_hp_position")]            public string UnitDisplayHpPositionJson { get; set; }
    [Column("unit_display_cursor_position")]        public string UnitDisplayCursorPositionJson { get; set; }

    // ── Statistics ──
    [Column("ai_level")]    public int AiLevel { get; set; }
    [Column("max_level")]   public int MaxLevel { get; set; }
    [Column("max_health")]  public int MaxHealth { get; set; }
    [Column("atk")]         public int Atk { get; set; }
    [Column("def")]         public int Def { get; set; }
    [Column("rec")]         public int Rec { get; set; }
    [Column("summon_cost")] public int SummonCost { get; set; }
    [Column("base_exp")]    public int BaseExp { get; set; }
    [Column("stats_base")]  public string StatsBaseJson { get; set; }
    [Column("stats_lord")]  public string StatsLordJson { get; set; }

    // ── Abilities (ids into the per-kind ability tables) ──
    [Column("basic_ability_id")]  public string BasicAbilityId { get; set; }
    [Column("bb_ability_id")]     public string BbAbilityId { get; set; }
    [Column("sbb_ability_id")]    public string SbbAbilityId { get; set; }
    [Column("ubb_ability_id")]    public string UbbAbilityId { get; set; }
    [Column("leader_ability_id")] public string LeaderAbilityId { get; set; }
    [Column("extra_ability_id")]  public string ExtraAbilityId { get; set; }
    [Column("bb_type")]           public string BbType { get; set; }

    // ── Movement ──
    [Column("move_speed_attack")] public float MoveSpeedAttack { get; set; }
    [Column("move_speed_skill")]  public float MoveSpeedSkill { get; set; }
    [Column("speed_type_attack")] public int SpeedTypeAttack { get; set; }
    [Column("speed_type_skill")]  public int SpeedTypeSkill { get; set; }
    [Column("move_type_attack")]  public int MoveTypeAttack { get; set; }
    [Column("move_type_skill")]   public int MoveTypeSkill { get; set; }

    // ── Evolution ──
    [Column("evo_from")]     public string EvoFrom { get; set; }
    [Column("evo_into")]     public string EvoInto { get; set; }
    [Column("evo_mats")]     public string EvoMatsJson { get; set; }
    [Column("evo_item")]     public string EvoItem { get; set; }
    [Column("evo_zel_cost")] public int EvoZelCost { get; set; }

    // ── Sell ──
    [Column("sell_price")]   public int SellPrice { get; set; }
    [Column("sell_caution")] public bool SellCaution { get; set; }

    public static UnitDbRow FromDto(UnitData d) => new UnitDbRow
    {
        UnitId = d.unitId,
        UnitName = d.unitName,
        UnitNumber = d.unitNumber,
        Description = d.description,
        SummonDesc = d.summonDesc,
        FusionDesc = d.fusionDesc,
        EvoDesc = d.evoDesc,
        Element = d.element.ToString(),
        Rarity = d.rarity.ToString(),

        SlotIconPath = d.slotIconPath,
        IconPath = d.iconPath,
        FullArtPath = d.fullArtPath,
        AnimationType = d.animationType.ToString(),
        SpriteSheetPathsJson = UnitDbJson.Serialize(d.spriteSheetPaths),
        CggPath = d.cggPath,
        IdleCgsPath = d.idleCgsPath,
        MoveCgsPath = d.moveCgsPath,
        AttackCgsPath = d.attackCgsPath,
        SamPath = d.samPath,

        UnitDisplayHomePositionJson = UnitDbJson.Serialize(d.unitDisplayHomePosition),
        UnitDisplayDetailPositionJson = UnitDbJson.Serialize(d.unitDisplayDetailPosition),
        UnitDisplayConfirmImagePositionJson = UnitDbJson.Serialize(d.unitDisplayConfirmImagePosition),
        UnitDisplayCutInImagePositionJson = UnitDbJson.Serialize(d.unitDisplayCutInImagePosition),
        UnitDisplaySummonPositionJson = UnitDbJson.Serialize(d.unitDisplaySummonPosition),
        UnitDisplayHpPositionJson = UnitDbJson.Serialize(d.unitDisplayHpPosition),
        UnitDisplayCursorPositionJson = UnitDbJson.Serialize(d.unitDisplayCursorPosition),

        AiLevel = d.aiLevel,
        MaxLevel = d.maxLevel,
        MaxHealth = d.maxHealth,
        Atk = d.atk,
        Def = d.def,
        Rec = d.rec,
        SummonCost = d.summonCost,
        BaseExp = d.baseExp,
        StatsBaseJson = UnitDbJson.Serialize(d.statsBase),
        StatsLordJson = UnitDbJson.Serialize(d.statsLord),

        BasicAbilityId = d.basicAbilityId,
        BbAbilityId = d.bbAbilityId,
        SbbAbilityId = d.sbbAbilityId,
        UbbAbilityId = d.ubbAbilityId,
        LeaderAbilityId = d.leaderAbilityId,
        ExtraAbilityId = d.extraAbilityId,
        BbType = d.bbType,

        MoveSpeedAttack = d.moveSpeedAttack,
        MoveSpeedSkill = d.moveSpeedSkill,
        SpeedTypeAttack = d.speedTypeAttack,
        SpeedTypeSkill = d.speedTypeSkill,
        MoveTypeAttack = d.moveTypeAttack,
        MoveTypeSkill = d.moveTypeSkill,

        EvoFrom = d.evoFrom,
        EvoInto = d.evoInto,
        EvoMatsJson = UnitDbJson.Serialize(d.evoMats),
        EvoItem = d.evoItem,
        EvoZelCost = d.evoZelCost,

        SellPrice = d.sellPrice,
        SellCaution = d.sellCaution
    };

    public UnitData ToDto() => new UnitData
    {
        unitId = UnitId,
        unitName = UnitName,
        unitNumber = UnitNumber,
        description = Description,
        summonDesc = SummonDesc,
        fusionDesc = FusionDesc,
        evoDesc = EvoDesc,
        element = UnitDbJson.ParseEnum<ElementalType>(Element),
        rarity = UnitDbJson.ParseEnum<UnitRarity>(Rarity),

        slotIconPath = SlotIconPath,
        iconPath = IconPath,
        fullArtPath = FullArtPath,
        animationType = UnitDbJson.ParseEnum<AnimationType>(AnimationType),
        spriteSheetPaths = UnitDbJson.Deserialize<List<string>>(SpriteSheetPathsJson),
        cggPath = CggPath,
        idleCgsPath = IdleCgsPath,
        moveCgsPath = MoveCgsPath,
        attackCgsPath = AttackCgsPath,
        samPath = SamPath,

        unitDisplayHomePosition = UnitDbJson.Deserialize<UnitDisplay>(UnitDisplayHomePositionJson),
        unitDisplayDetailPosition = UnitDbJson.Deserialize<UnitDisplay>(UnitDisplayDetailPositionJson),
        unitDisplayConfirmImagePosition = UnitDbJson.Deserialize<UnitDisplay>(UnitDisplayConfirmImagePositionJson),
        unitDisplayCutInImagePosition = UnitDbJson.Deserialize<UnitDisplay>(UnitDisplayCutInImagePositionJson),
        unitDisplaySummonPosition = UnitDbJson.Deserialize<UnitDisplay>(UnitDisplaySummonPositionJson),
        unitDisplayHpPosition = UnitDbJson.Deserialize<Vector2>(UnitDisplayHpPositionJson),
        unitDisplayCursorPosition = UnitDbJson.Deserialize<Vector2>(UnitDisplayCursorPositionJson),

        aiLevel = AiLevel,
        maxLevel = MaxLevel,
        maxHealth = MaxHealth,
        atk = Atk,
        def = Def,
        rec = Rec,
        summonCost = SummonCost,
        baseExp = BaseExp,
        statsBase = UnitDbJson.Deserialize<UnitStats>(StatsBaseJson),
        statsLord = UnitDbJson.Deserialize<UnitStats>(StatsLordJson),

        basicAbilityId = BasicAbilityId,
        bbAbilityId = BbAbilityId,
        sbbAbilityId = SbbAbilityId,
        ubbAbilityId = UbbAbilityId,
        leaderAbilityId = LeaderAbilityId,
        extraAbilityId = ExtraAbilityId,
        bbType = BbType,

        moveSpeedAttack = MoveSpeedAttack,
        moveSpeedSkill = MoveSpeedSkill,
        speedTypeAttack = SpeedTypeAttack,
        speedTypeSkill = SpeedTypeSkill,
        moveTypeAttack = MoveTypeAttack,
        moveTypeSkill = MoveTypeSkill,

        evoFrom = EvoFrom,
        evoInto = EvoInto,
        evoMats = UnitDbJson.Deserialize<List<string>>(EvoMatsJson),
        evoItem = EvoItem,
        evoZelCost = EvoZelCost,

        sellPrice = SellPrice,
        sellCaution = SellCaution
    };
}

// ── Ability kind ─────────────────────────────────────────────────────────────
// Which ability table an id belongs to. Mirrored by the unit fields:
// basicAbilityId → Basic, bb/sbb/ubbAbilityId → Bb, leaderAbilityId → Leader,
// extraAbilityId → Extra.
public enum AbilityKind
{
    Basic,   // normal attacks (id = the owning unit's id)
    Bb,      // bb, sbb and ubb (skill MST ids, unique across all three)
    Leader,  // leader skills
    Extra    // extra skills
}

public static class AbilityKinds
{
    /// <summary>File-name / display tag for a kind: ability_{tag}_{id}.</summary>
    public static string Tag(this AbilityKind kind) => kind switch
    {
        AbilityKind.Basic  => "basic",
        AbilityKind.Bb     => "bb",
        AbilityKind.Leader => "leader",
        _                  => "extra"
    };

    /// <summary>SQLite table holding a kind's rows.</summary>
    public static string TableName(this AbilityKind kind) => $"abilities_{Tag(kind)}";
}

// Shared shape for every ability table. Never mapped itself — the sealed
// subclasses below carry the [Table] mapping and are the only types handed to
// sqlite-net (CreateTable/Find/Insert all resolve the concrete runtime type).
public abstract class AbilityDbRow
{
    [PrimaryKey] [Column("ability_id")]   public string AbilityId { get; set; }
    [Indexed]    [Column("ability_name")] public string AbilityName { get; set; }
    [Column("ability_desc")] public string AbilityDesc { get; set; }
    [Column("target_type")] public string TargetType { get; set; }
    [Column("target_area")] public string TargetArea { get; set; }
    [Column("drop_checks_per_hit")] public int DropChecksPerHit { get; set; }
    [Column("hit_frames")]    public string HitFramesJson { get; set; }
    [Column("effect_frames")] public string EffectFramesJson { get; set; }
    [Column("levels")]        public string LevelsJson { get; set; }

    /// <summary>Builds a row of the concrete table type for a kind.</summary>
    public static AbilityDbRow FromDto(AbilityKind kind, AbilityData d) => kind switch
    {
        AbilityKind.Basic  => Copy(d, new BasicAbilityDbRow()),
        AbilityKind.Bb     => Copy(d, new BbAbilityDbRow()),
        AbilityKind.Leader => Copy(d, new LeaderAbilityDbRow()),
        _                  => Copy(d, new ExtraAbilityDbRow())
    };

    static T Copy<T>(AbilityData d, T row) where T : AbilityDbRow
    {
        row.AbilityId = d.abilityId;
        row.AbilityName = d.abilityName;
        row.AbilityDesc = d.abilityDesc;
        row.TargetType = d.targetType.ToString();
        row.TargetArea = d.targetArea.ToString();
        row.DropChecksPerHit = d.dropChecksPerHit;
        row.HitFramesJson = UnitDbJson.Serialize(d.hitFrames);
        row.EffectFramesJson = UnitDbJson.Serialize(d.effectFrames);
        row.LevelsJson = UnitDbJson.Serialize(d.levels);
        return row;
    }

    public AbilityData ToDto() => new AbilityData
    {
        abilityId = AbilityId,
        abilityName = AbilityName,
        abilityDesc = AbilityDesc,
        targetType = UnitDbJson.ParseEnum<TargetType>(TargetType),
        targetArea = UnitDbJson.ParseEnum<TargetArea>(TargetArea),
        dropChecksPerHit = DropChecksPerHit,
        hitFrames = UnitDbJson.Deserialize<List<HitFrame>>(HitFramesJson),
        effectFrames = UnitDbJson.Deserialize<List<EffectFrameData>>(EffectFramesJson),
        levels = UnitDbJson.Deserialize<List<AbilityLevel>>(LevelsJson)
    };
}

[Table("abilities_basic")]  public sealed class BasicAbilityDbRow  : AbilityDbRow { }
[Table("abilities_bb")]     public sealed class BbAbilityDbRow     : AbilityDbRow { }
[Table("abilities_leader")] public sealed class LeaderAbilityDbRow : AbilityDbRow { }
[Table("abilities_extra")]  public sealed class ExtraAbilityDbRow  : AbilityDbRow { }

[Table("meta")]
public class MetaRow
{
    [PrimaryKey] [Column("key")]   public string Key { get; set; }
    [Column("value")] public string Value { get; set; }
}
