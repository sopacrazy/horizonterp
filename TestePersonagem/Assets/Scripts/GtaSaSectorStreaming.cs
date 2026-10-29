using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sector streamer for the separate San Andreas import test. It does not touch the official city streamer.
[DefaultExecutionOrder(-100)]
public sealed class GtaSaSectorStreaming : MonoBehaviour
{
    [Serializable] public sealed class Sector
    {
        public string scene;
        public Bounds bounds;
        public int instances;
    }

    public Sector[] sectors = Array.Empty<Sector>();
    public Transform target;
    public float desktopRadius = 230;
    public float mobileRadius = 150;
    public int maxMobileSectors = 9;
    public float unloadExtra = 90;
    public int LoadedCount { get; private set; }
    public int DesiredCount { get; private set; }
    public bool Ready { get; private set; }
    public string LastError { get; private set; } = "";
    readonly HashSet<string> desired = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
    readonly List<Sector> mobileCandidates = new List<Sector>();
    bool stopping;
    bool suspendedForTeleport;

    IEnumerator Start()
    {
        if (!target)
        {
            var player = FindAnyObjectByType<ThirdPersonPlayer>(FindObjectsInactive.Include);
            if (player) target = player.transform;
        }
        var pause = new WaitForSecondsRealtime(.3f);
        while (!stopping)
        {
            RefreshNow();
            if (suspendedForTeleport) { yield return pause; continue; }
            if (!target) { yield return pause; continue; }
            bool limitMobile = Application.isMobilePlatform && maxMobileSectors > 0;
            // Free a slot before loading a replacement, so movement never grows past the cap.
            if (limitMobile && LoadedCount >= maxMobileSectors)
            {
                Sector obsolete = null;
                foreach (var sector in sectors)
                {
                    if (sector == null || desired.Contains(sector.scene) || !IsLoaded(sector.scene) || pending.Contains(sector.scene)) continue;
                    if (obsolete == null || sector.bounds.SqrDistance(target.position) > obsolete.bounds.SqrDistance(target.position))
                        obsolete = sector;
                }
                if (obsolete != null)
                {
                    pending.Add(obsolete.scene);
                    var unload = SceneManager.UnloadSceneAsync(obsolete.scene);
                    if (unload != null) yield return unload;
                    pending.Remove(obsolete.scene);
                    continue;
                }
            }
            Sector next = null;
            float best = float.PositiveInfinity;
            foreach (var sector in sectors)
            {
                if (sector == null || !desired.Contains(sector.scene) || IsLoaded(sector.scene) || pending.Contains(sector.scene)) continue;
                float distance = sector.bounds.SqrDistance(target.position);
                if (distance < best) { best = distance; next = sector; }
            }
            if (next != null)
            {
                pending.Add(next.scene);
                if (!Application.CanStreamedLevelBeLoaded(next.scene))
                {
                    LastError = "Setor ausente da compilação: " + next.scene;
                    Debug.LogError(LastError);
                    pending.Remove(next.scene);
                    yield break;
                }
                var operation = SceneManager.LoadSceneAsync(next.scene, LoadSceneMode.Additive);
                if (operation != null) yield return operation;
                pending.Remove(next.scene);
                if (!IsLoaded(next.scene))
                {
                    LastError = "Falha ao carregar " + next.scene;
                    Debug.LogError(LastError);
                    yield break;
                }
                SceneManager.SetActiveScene(gameObject.scene);
                continue;
            }
            // Keep the currently occupied sector until the replacement has loaded.
            if (!Ready) { yield return pause; continue; }
            float radius = (Application.isMobilePlatform ? mobileRadius : desktopRadius) + unloadExtra;
            float limit = radius * radius;
            foreach (var sector in sectors)
            {
                if (sector == null || desired.Contains(sector.scene) || !IsLoaded(sector.scene) || pending.Contains(sector.scene)) continue;
                if (!limitMobile && (sector.bounds.SqrDistance(target.position) <= limit || sector.bounds.Contains(target.position))) continue;
                pending.Add(sector.scene);
                var operation = SceneManager.UnloadSceneAsync(sector.scene);
                if (operation != null) yield return operation;
                pending.Remove(sector.scene);
                break;
            }
            yield return pause;
        }
    }

    // Called only while the player controller is disabled for a long-distance teleport.
    // It avoids holding both cities' sectors in memory on a 4 GB mobile device.
    public IEnumerator ClearLoadedForTeleport()
    {
        suspendedForTeleport = true;
        while (pending.Count > 0) yield return null;
        foreach (var sector in sectors)
        {
            if (sector == null || !IsLoaded(sector.scene)) continue;
            var unload = SceneManager.UnloadSceneAsync(sector.scene);
            if (unload != null) yield return unload;
        }
        yield return Resources.UnloadUnusedAssets();
        suspendedForTeleport = false;
        RefreshNow();
    }

    public void RefreshNow()
    {
        desired.Clear();
        LoadedCount = 0;
        Ready = target && sectors != null && sectors.Length > 0 && string.IsNullOrEmpty(LastError);
        if (!Ready) return;
        float radius = Application.isMobilePlatform ? mobileRadius : desktopRadius;
        float limit = radius * radius;
        Sector nearest = null;
        float best = float.PositiveInfinity;
        bool limitMobile = Application.isMobilePlatform && maxMobileSectors > 0;
        mobileCandidates.Clear();
        foreach (var sector in sectors)
        {
            if (sector == null || string.IsNullOrEmpty(sector.scene)) { Ready = false; continue; }
            float d = sector.bounds.SqrDistance(target.position);
            if (d < best) { best = d; nearest = sector; }
            if (d <= limit || sector.bounds.Contains(target.position))
            {
                if (limitMobile) mobileCandidates.Add(sector);
                else desired.Add(sector.scene);
            }
            if (IsLoaded(sector.scene)) LoadedCount++;
        }
        if (limitMobile)
        {
            int sx = Mathf.FloorToInt(target.position.x / 256f);
            int sy = Mathf.FloorToInt(-target.position.z / 256f);
            string owner = "GTASASector_" + (sx < 0 ? "m" + (-sx) : "p" + sx) + "_" +
                (sy < 0 ? "m" + (-sy) : "p" + sy);
            mobileCandidates.Sort((a,b) => {
                bool aOwn = a.scene == owner, bOwn = b.scene == owner;
                if (aOwn != bOwn) return aOwn ? -1 : 1;
                float da = a.bounds.SqrDistance(target.position), db = b.bounds.SqrDistance(target.position);
                int byBounds = da.CompareTo(db);
                if (byBounds != 0) return byBounds;
                return (a.bounds.center - target.position).sqrMagnitude.CompareTo(
                    (b.bounds.center - target.position).sqrMagnitude);
            });
            for (int i = 0; i < Mathf.Min(maxMobileSectors, mobileCandidates.Count); i++)
                desired.Add(mobileCandidates[i].scene);
        }
        if (desired.Count == 0 && nearest != null) desired.Add(nearest.scene);
        DesiredCount = desired.Count;
        foreach (string scene in desired) if (!IsLoaded(scene)) Ready = false;
    }

    static bool IsLoaded(string name)
    {
        var scene = SceneManager.GetSceneByName(name);
        return scene.IsValid() && scene.isLoaded;
    }

    void OnDestroy() { stopping = true; }
}
