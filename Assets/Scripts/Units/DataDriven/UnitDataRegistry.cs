// UnitDataRegistry.cs
// Data source for the data-driven UnitData/AbilityData DTOs: the SQLite
// database (UnitDataDb) with its in-memory mod overlay — overlay rows
// (external folder JSONs) win over the base db rows. DTOs are cached for the
// session. FallbackToJson re-enables the legacy external-JSON → Addressables
// load path for debugging missing-db situations; default off.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class UnitDataRegistry
{
    /// <summary>
    /// Legacy load path (external folder JSON → bundled Addressables JSON) for
    /// debugging a missing/broken units.db. Default off; delete once verified.
    /// </summary>
    public static bool FallbackToJson = false;

    static readonly Dictionary<string, UnitData> s_unitCache = new Dictionary<string, UnitData>();
    static readonly Dictionary<(AbilityKind Kind, string AbilityId), AbilityData> s_abilityCache = new Dictionary<(AbilityKind, string), AbilityData>();

    public static bool ExternalOverridesActive => UnitDataDb.ExternalOverridesActive;

    public static UnitData GetUnitData(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) return null;
        if (s_unitCache.TryGetValue(unitId, out UnitData cached)) return cached;

        UnitData data = UnitDataDb.GetUnitData(unitId);
        if (data == null && FallbackToJson)
            data = TryLoadExternal<UnitData>($"unit_{unitId}.json")
                ?? LoadBundled<UnitData>(DataDrivenAddresses.UnitJson(unitId));

        if (data == null)
        {
            Debug.LogError($"[UnitDataRegistry] Unit data {unitId} not found.");
            return null;
        }

        s_unitCache[unitId] = data;
        return data;
    }

    /// <summary>
    /// Resolves an ability by kind + id — the kind mirrors the unit field the id
    /// came from and selects the per-kind ability table (db or external/bundled
    /// ability_{kind}_{id}.json).
    /// </summary>
    public static AbilityData GetAbilityData(AbilityKind kind, string abilityId)
    {
        if (string.IsNullOrEmpty(abilityId)) return null;
        var key = (kind, abilityId);
        if (s_abilityCache.TryGetValue(key, out AbilityData cached)) return cached;

        AbilityData data = UnitDataDb.GetAbilityData(kind, abilityId);
        if (data == null && FallbackToJson)
            data = TryLoadExternal<AbilityData>($"ability_{kind.Tag()}_{abilityId}.json")
                ?? LoadBundled<AbilityData>(DataDrivenAddresses.AbilityJson(kind, abilityId));

        if (data == null)
        {
            Debug.LogError($"[UnitDataRegistry] Ability data {kind.Tag()}_{abilityId} not found.");
            return null;
        }

        s_abilityCache[key] = data;
        return data;
    }

    public static bool TryGetCachedUnitData(string unitId, out UnitData data) => s_unitCache.TryGetValue(unitId, out data);
    public static bool TryGetCachedAbilityData(AbilityKind kind, string abilityId, out AbilityData data)
        => s_abilityCache.TryGetValue((kind, abilityId), out data);

    static T TryLoadExternal<T>(string fileName) where T : class
    {
        if (!Directory.Exists(UnitDataPaths.ExternalRoot)) return null;

        string path = Path.Combine(UnitDataPaths.ExternalRoot, fileName);
        if (!File.Exists(path)) return null;

        try
        {
            T data = JsonConvert.DeserializeObject<T>(File.ReadAllText(path), DataDrivenJson.Settings);
            if (data != null) return data;
            Debug.LogError($"[UnitDataRegistry] External file '{path}' deserialized to null — falling back to bundled.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataRegistry] Malformed external file '{path}': {e.Message} — falling back to bundled.");
            return null;
        }
    }

    static T LoadBundled<T>(string address) where T : class
    {
        TextAsset text = AssetResolver.LoadText(address);
        if (text == null) return null;
        return JsonConvert.DeserializeObject<T>(text.text, DataDrivenJson.Settings);
    }
}
