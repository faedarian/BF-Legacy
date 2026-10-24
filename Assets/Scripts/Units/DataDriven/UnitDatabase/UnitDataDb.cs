// UnitDataDb.cs
// SQLite-backed store for the data-driven UnitData/AbilityData DTOs, replacing
// per-file Addressables JSON loads. Two lazily-opened connections:
//   s_conn     main baseline db, opened READ-ONLY and never written
//              (editor: StreamingAssets/Data/units.db directly, no staleness;
//               desktop: copy-on-version-change into persistentDataPath;
//               Android: UnityWebRequest copy — files inside the APK aren't
//               openable — kicked off at RuntimeInitializeOnLoadMethod, until
//               it lands lookups degrade to null like any missing unit)
//   s_modConn  ':memory:' overlay schema holding the external folder JSONs
//              (UnitDataPaths.ExternalRoot); overlay rows win over base rows
//              (units_*.json, ability_{basic|bb|leader|extra}_*.json)
// NOTE: the overlay is a separate connection because a READ-ONLY SQLite
// connection cannot write to an ATTACH'd ':memory:' database.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SQLite;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;

public static class UnitDataDb
{
    const string DbFileName = "units.db";
    const string DataDirName = "Data";
    const string MetaDataVersionKey = "dataVersion";
    const string PlayerPrefsVersionKey = "UnitDataDb.DataVersion";

    static SQLiteConnection s_conn;
    static SQLiteConnection s_modConn;
    static bool s_overlayActive;
    static bool s_quitHooked;
    static bool s_openFailed;

#if UNITY_ANDROID && !UNITY_EDITOR
    static bool s_copyStarted, s_copyDone, s_copyOk;
#endif

    /// <summary>True when the main baseline db connection is open.</summary>
    public static bool Available => s_conn != null;

    /// <summary>True when the mod overlay holds at least one row.</summary>
    public static bool ExternalOverridesActive
    {
        get { EnsureOpen(); return s_overlayActive; }
    }

    static void EnsureOpen()
    {
        if (s_conn == null && !s_openFailed) OpenMain();
        if (s_modConn == null) OpenOverlay();
    }

    static void OpenMain()
    {
        string path = ResolveDbPath();
        if (path == null) return; // transient (Android copy in flight) or already latched

        try
        {
            s_conn = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            s_conn.BusyTimeout = TimeSpan.FromSeconds(1);
            HookQuit();
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataDb] Failed to open '{path}': {e.Message}");
            s_conn = null;
            s_openFailed = true;
        }
    }

    static void OpenOverlay()
    {
        try
        {
            s_modConn = new SQLiteConnection(":memory:",
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.Memory);
            s_modConn.BusyTimeout = TimeSpan.FromSeconds(1);
            s_modConn.CreateTable<UnitDbRow>();
            s_modConn.CreateTable<BasicAbilityDbRow>();
            s_modConn.CreateTable<BbAbilityDbRow>();
            s_modConn.CreateTable<LeaderAbilityDbRow>();
            s_modConn.CreateTable<ExtraAbilityDbRow>();
            LoadExternalJsons();
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataDb] Failed to prepare mod overlay: {e.Message}");
            s_modConn = null;
            s_overlayActive = false;
        }
    }

    static string ResolveDbPath()
    {
#if UNITY_EDITOR
        // Editor reads StreamingAssets directly — rebuilds are picked up instantly.
        return Path.Combine(Application.streamingAssetsPath, DataDirName, DbFileName);
#elif UNITY_ANDROID
        // Copy may still be in flight — transient null, retried on next lookup.
        string dest = Path.Combine(Application.persistentDataPath, DbFileName);
        if (!s_copyDone) return null;
        s_openFailed = !s_copyOk; // once done, a failed copy is definitive
        return s_copyOk ? dest : null;
#else
        string dest = Path.Combine(Application.persistentDataPath, DbFileName);
        string source = Path.Combine(Application.streamingAssetsPath, DataDirName, DbFileName);
        EnsureCopy(source, dest);
        if (File.Exists(dest)) return dest;
        s_openFailed = true;
        return null;
#endif
    }

    // ── Copy-on-version-change (players) ──

    static void EnsureCopy(string source, string dest)
    {
        string sourceVersion = ReadMetaVersion(source);
        string localVersion = PlayerPrefs.GetString(PlayerPrefsVersionKey, null);
        if (File.Exists(dest) && sourceVersion != null && sourceVersion == localVersion) return;

        try
        {
            File.Copy(source, dest, overwrite: true);
            PlayerPrefs.SetString(PlayerPrefsVersionKey, sourceVersion);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataDb] Failed to copy '{source}' → '{dest}': {e.Message}");
        }
    }

    public static UnitDbRow GetRandomUnitRow()
    {
        EnsureOpen();
        if (s_conn == null) return null;
        try
        {
            const string filter = "icon_path IS NOT NULL AND icon_path <> ''";

            int count = s_conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM units WHERE {filter}");
            if (count == 0) return null;

            var row = s_conn.Query<UnitDbRow>(
                $"SELECT * FROM units WHERE {filter} ORDER BY rowid LIMIT 1 OFFSET ?",
                UnityEngine.Random.Range(0, count)).FirstOrDefault();
            if (row == null) return null;

            // overlay wins, unless its version has no icon
            var mod = s_modConn?.Find<UnitDbRow>(row.UnitId);
            return (mod != null && !string.IsNullOrEmpty(mod.IconPath)) ? mod : row;
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataDb] GetRandomUnitRow failed: {e.Message}");
            return null;
        }
    }

    static string ReadMetaVersion(string path)
    {
        try
        {
            using (var conn = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly))
                return conn.Find<MetaRow>(MetaDataVersionKey)?.Value;
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnitDataDb] Cannot read dataVersion from '{path}': {e.Message}");
            return null;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartAndroidCopy()
    {
        if (s_copyStarted) return;
        s_copyStarted = true;

        var go = new GameObject("UnitDataDb.AndroidCopy");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<AndroidCopyDriver>().Run();
    }

    class AndroidCopyDriver : MonoBehaviour
    {
        public void Run() => StartCoroutine(CopyRoutine());

        System.Collections.IEnumerator CopyRoutine()
        {
            string dest = Path.Combine(Application.persistentDataPath, DbFileName);
            string url = $"{Application.streamingAssetsPath}/{DataDirName}/{DbFileName}";

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[UnitDataDb] Failed to read '{url}': {request.error}");
                    s_copyDone = s_copyOk = File.Exists(dest);
                    yield break;
                }

                string tmp = dest + ".tmp";
                File.WriteAllBytes(tmp, request.downloadHandler.data);

                string sourceVersion = ReadMetaVersion(tmp);
                string localVersion = PlayerPrefs.GetString(PlayerPrefsVersionKey, null);
                if (!File.Exists(dest) || sourceVersion == null || sourceVersion != localVersion)
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                    PlayerPrefs.SetString(PlayerPrefsVersionKey, sourceVersion);
                    PlayerPrefs.Save();
                }
                else
                {
                    File.Delete(tmp);
                }

                s_copyDone = s_copyOk = true;
            }
        }
    }
#endif

    // ── Mod overlay (external folder JSONs → memory) ──

    static void LoadExternalJsons()
    {
        string root = UnitDataPaths.ExternalRoot;
        if (!Directory.Exists(root)) return;

        var unitRows = new List<UnitDbRow>();
        var abilityRows = new List<AbilityDbRow>(); // concrete kind rows — sqlite-net maps them by runtime type

        foreach (string file in Directory.GetFiles(root, "*.json"))
        {
            string name = Path.GetFileName(file);
            try
            {
                string json = File.ReadAllText(file);
                if (name.StartsWith("unit_", StringComparison.Ordinal))
                {
                    var dto = JsonConvert.DeserializeObject<UnitData>(json, DataDrivenJson.Settings);
                    if (dto == null)
                        Debug.LogError($"[UnitDataDb] External file '{file}' deserialized to null — skipped.");
                    else
                        unitRows.Add(UnitDbRow.FromDto(dto));
                }
                else if (TryParseAbilityFileName(name, out AbilityKind kind))
                {
                    var dto = JsonConvert.DeserializeObject<AbilityData>(json, DataDrivenJson.Settings);
                    if (dto == null)
                        Debug.LogError($"[UnitDataDb] External file '{file}' deserialized to null — skipped.");
                    else
                        abilityRows.Add(AbilityDbRow.FromDto(kind, dto));
                }
                else if (name.StartsWith("ability_", StringComparison.Ordinal))
                {
                    // Recognized but untagged (pre-split export) — abilities are
                    // kind-scoped now, so these can't be routed to a table.
                    Debug.LogWarning($"[UnitDataDb] External file '{name}' ignored — legacy naming; expected " +
                                     "ability_(basic|bb|leader|extra)_*.json (re-export external units).");
                }
                // Unrelated *.json files are silently ignored, as before.
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnitDataDb] Malformed external file '{file}': {e.Message} — skipped.");
            }
        }

        if (unitRows.Count == 0 && abilityRows.Count == 0) return;

        s_modConn.RunInTransaction(() =>
        {
            foreach (var row in unitRows) s_modConn.InsertOrReplace(row);
            foreach (var row in abilityRows) s_modConn.InsertOrReplace(row);
        });

        s_overlayActive = true;
        Debug.Log($"[UnitDataDb] Mod overlay loaded from '{root}': {unitRows.Count} units, {abilityRows.Count} abilities.");
    }

    /// <summary>"ability_(basic|bb|leader|extra)_{id}.json" → its ability kind; false otherwise.</summary>
    static bool TryParseAbilityFileName(string fileName, out AbilityKind kind)
    {
        kind = default;
        const string prefix = "ability_";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) return false;

        string rest = fileName.Substring(prefix.Length);
        int sep = rest.IndexOf('_');
        if (sep < 0) return false;

        switch (rest.Substring(0, sep))
        {
            case "basic":  kind = AbilityKind.Basic;  return true;
            case "bb":     kind = AbilityKind.Bb;     return true;
            case "leader": kind = AbilityKind.Leader; return true;
            case "extra":  kind = AbilityKind.Extra;  return true;
            default:       return false;
        }
    }

    // ── Lookups (overlay first, then base) ──

    public static UnitData GetUnitData(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) return null;
        EnsureOpen();

        UnitDbRow row = QueryOverlayRow<UnitDbRow>(unitId)
                        ?? (s_conn != null ? s_conn.Find<UnitDbRow>(unitId) : null);
        return row?.ToDto();
    }

    /// <summary>
    /// Resolves an ability by kind + id. The kind selects the per-kind table
    /// (unit data references ids together with the field that holds them), so
    /// ids that collide numerically across id spaces stay unambiguous.
    /// </summary>
    public static AbilityData GetAbilityData(AbilityKind kind, string abilityId)
    {
        if (string.IsNullOrEmpty(abilityId)) return null;
        EnsureOpen();

        AbilityDbRow row = kind switch
        {
            AbilityKind.Basic  => QueryOverlayRow<BasicAbilityDbRow>(abilityId)  ?? BaseAbilityRow<BasicAbilityDbRow>(abilityId),
            AbilityKind.Bb     => QueryOverlayRow<BbAbilityDbRow>(abilityId)     ?? BaseAbilityRow<BbAbilityDbRow>(abilityId),
            AbilityKind.Leader => QueryOverlayRow<LeaderAbilityDbRow>(abilityId) ?? BaseAbilityRow<LeaderAbilityDbRow>(abilityId),
            _                  => QueryOverlayRow<ExtraAbilityDbRow>(abilityId)  ?? BaseAbilityRow<ExtraAbilityDbRow>(abilityId)
        };
        return row?.ToDto();
    }

    static T BaseAbilityRow<T>(string id) where T : class, new()
        => s_conn != null ? s_conn.Find<T>(id) : null;

    static T QueryOverlayRow<T>(string id) where T : new()
    {
        if (!s_overlayActive) return default;
        return s_modConn.Find<T>(id);
    }

    static void HookQuit()
    {
        if (s_quitHooked) return;
        s_quitHooked = true;
        Application.quitting += Close;
    }

    public static void Close()
    {
        try { s_conn?.Close(); } catch { /* read-only close is best-effort */ }
        try { s_modConn?.Close(); } catch { }
        s_conn = null;
        s_modConn = null;
        s_overlayActive = false;
        s_openFailed = false;
    }
}
