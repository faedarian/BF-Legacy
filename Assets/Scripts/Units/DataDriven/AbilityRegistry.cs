// AbilityRegistry.cs
// Consumer-facing ability cache for the data-driven pipeline. Mirrors the old
// baked Ability SOs: each ability_{kind}_{id}.json / per-kind db table row
// deserializes once into a shared DTO, then builds one runtime Ability view
// with resolved ParticleEffect/AudioClip references. Abilities are shared
// across units, so the view is cached by (kind, id) for the session (no
// release, like UnitRegistry v1).
using System.Collections.Generic;
using UnityEngine;

public static class AbilityRegistry
{
    static readonly Dictionary<(AbilityKind Kind, string AbilityId), Ability> s_abilities = new Dictionary<(AbilityKind, string), Ability>();

    /// <summary>
    /// Resolves an ability by kind + id — the kind mirrors the unit field the id
    /// came from (basic/bb/sbb/ubb/leader/extra) and selects the per-kind table.
    /// </summary>
    public static Ability GetAbilityById(AbilityKind kind, string abilityId)
    {
        if (string.IsNullOrEmpty(abilityId)) return null;
        var key = (kind, abilityId);
        if (s_abilities.TryGetValue(key, out Ability cached)) return cached;

        AbilityData data = UnitDataRegistry.GetAbilityData(kind, abilityId);
        if (data == null) return null;

        Ability ability = Build(data, kind);
        s_abilities[key] = ability;
        return ability;
    }

    public static bool TryGetLoadedAbility(AbilityKind kind, string abilityId, out Ability ability)
        => s_abilities.TryGetValue((kind, abilityId), out ability);

    /// <summary>Addresses referenced by this ability's effect frames, for warmup.</summary>
    public static void Warmup(AbilityKind kind, IEnumerable<string> abilityIds)
    {
        foreach (string abilityId in abilityIds)
        {
            if (string.IsNullOrEmpty(abilityId) || s_abilities.ContainsKey((kind, abilityId))) continue;
            AbilityData data = UnitDataRegistry.TryGetCachedAbilityData(kind, abilityId, out var d) ? d : null;
            if (data?.effectFrames == null) continue;

            var addresses = new List<string>();
            foreach (EffectFrameData ef in data.effectFrames)
            {
                if (!string.IsNullOrEmpty(ef.particleEffectId)) addresses.Add(ef.particleEffectId);
                if (!string.IsNullOrEmpty(ef.audioClipPath)) addresses.Add(ef.audioClipPath);
            }
            AssetResolver.Preload(addresses);
        }
    }

    static Ability Build(AbilityData data, AbilityKind kind)
    {
        Ability a = ScriptableObject.CreateInstance<Ability>();
        a.name = $"ability_{kind.Tag()}_{data.abilityId}";

        a.abilityId        = data.abilityId;
        a.abilityName      = data.abilityName;
        a.abilityDesc      = data.abilityDesc;
        a.targetType       = data.targetType;
        a.targetArea       = data.targetArea;
        a.dropChecksPerHit = data.dropChecksPerHit;

        // Lists are shared with the cached DTO — the old baked SOs were shared
        // references too, and consumers treat ability data as read-only config.
        a.hitFrames    = data.hitFrames;
        a.levels       = data.levels;
        a.effectFrames = data.effectFrames == null ? null : BuildEffectFrames(data.effectFrames);

        return a;
    }

    static List<EffectFrame> BuildEffectFrames(List<EffectFrameData> data)
    {
        var list = new List<EffectFrame>(data.Count);
        foreach (EffectFrameData d in data)
        {
            list.Add(new EffectFrame
            {
                frame               = d.frame,
                battleEffectGroupId = d.battleEffectGroupId,
                particleEffectId    = d.particleEffectId,
                particleEffect      = string.IsNullOrEmpty(d.particleEffectId)
                    ? null : AssetResolver.LoadParticleEffect(d.particleEffectId),
                placementType       = d.placementType,
                xAnchor             = d.xAnchor,
                yAnchor             = d.yAnchor,
                offsetX             = d.offsetX,
                offsetY             = d.offsetY,
                audioClip           = string.IsNullOrEmpty(d.audioClipPath)
                    ? null : AssetResolver.LoadAudio(d.audioClipPath)
            });
        }
        return list;
    }
}
