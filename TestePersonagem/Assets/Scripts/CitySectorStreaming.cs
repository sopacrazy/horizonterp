using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Distance-based streaming includes the surroundings in every direction, including collisions.
[DefaultExecutionOrder(-100)]
public sealed class CitySectorStreaming : MonoBehaviour
{
    [Serializable]
    public class Sector
    {
        public string scene;
        public Bounds bounds;
    }

    public Sector[] sectors = Array.Empty<Sector>();
    public Transform target;
    public float mobileLoadDistance = 125;
    public float desktopLoadDistance = 190;
    public static bool IsPreparing { get; private set; }
    public bool Ready { get; private set; }
    public int LoadedCount { get; private set; }
    public int PendingCount { get; private set; }
    public int CompletedLoads { get; private set; }
    public int CompletedUnloads { get; private set; }
    public int UnusedAssetSweeps { get; private set; }
    public string LastError { get; private set; } = "";
    public event Action<Scene> SectorLoaded;

    const float PollSeconds = .4f;
    const float UnloadHysteresis = 60;
    const float SafetyMargin = 45;
    readonly List<State> states = new List<State>();
    readonly HashSet<string> desired = new HashSet<string>(StringComparer.Ordinal);
    static CitySectorStreaming active;
    Sector[] indexedSectors;
    Scene officialScene;
    AsyncOperation operation;
    string operationScene;
    bool operationIsLoad, metadataValid, shuttingDown, quitting, lowMemoryTrim;
    int generation, unloadsSinceSweep, missingDesired;
    Vector3 stationaryAnchor;
    float lastMovementTime;

    sealed class State
    {
        public Sector definition;
        public string name;
        public bool loaded;
        public float distanceSquared;
        public float retryAt;
    }

    void Awake()
    {
        if (active && active != this)
        {
            Debug.LogWarning("Only one CitySectorStreaming may control the official city.");
            enabled = false;
            return;
        }
        active = this;
        officialScene = gameObject.scene;
        lastMovementTime = Time.realtimeSinceStartup;
        Application.lowMemory += OnLowMemory;
        RefreshNow();
    }

    IEnumerator Start()
    {
        if (active != this) yield break;
        int token = generation;
        var poll = new WaitForSecondsRealtime(PollSeconds);
        while (!shuttingDown && token == generation)
        {
            RefreshNow();
            if (!target || !metadataValid || states.Count == 0) { yield return poll; continue; }
            State next = NextLoad();
            if (next != null)
            {
                BeginLoad(next, token);
                yield return WaitForOperation(token);
                if (shuttingDown || token != generation) yield break;
                next.loaded = SceneLoaded(next.name);
                if (next.loaded)
                {
                    CompletedLoads++;
                    var scene = SceneManager.GetSceneByName(next.name);
                    foreach (var root in scene.GetRootGameObjects()) GtaCityEnvironment.ConfigureSurfaces(root.transform);
                    if (officialScene.IsValid() && officialScene.isLoaded) SceneManager.SetActiveScene(officialScene);
                    try { SectorLoaded?.Invoke(scene); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
                else if (operation != null) Fail(next, "Sector load did not finish: " + next.name);
                ClearOperation();
                continue;
            }
            // Load first, even if a failed load is waiting for its retry cooldown.
            if (missingDesired > 0) { yield return poll; continue; }
            next = NextUnload();
            if (next != null)
            {
                BeginUnload(next);
                yield return WaitForOperation(token);
                if (shuttingDown || token != generation) yield break;
                if (!SceneLoaded(next.name)) { CompletedUnloads++; unloadsSinceSweep++; }
                ClearOperation();
                continue;
            }
            lowMemoryTrim = false;
            // Shared resources remain alive while referenced by neighboring loaded scenes.
            // Only reclaim unreferenced resources after a batch of unloads while stationary.
            if (unloadsSinceSweep >= 8 && Time.realtimeSinceStartup - lastMovementTime > 2)
            {
                operation = Resources.UnloadUnusedAssets();
                operationScene = null; operationIsLoad = false;
                yield return WaitForOperation(token);
                if (shuttingDown || token != generation) yield break;
                unloadsSinceSweep = 0; UnusedAssetSweeps++;
                ClearOperation();
                continue;
            }
            yield return poll;
        }
    }

    // Call after a teleporter changes target.position, or after replacing target with a QA probe.
    // Native Unity scene operations cannot be cancelled; the next operation uses the new target.
    public void RefreshNow()
    {
        if (shuttingDown || active != this) return;
        if (indexedSectors != sectors) BuildIndex();
        if (!target)
        {
            var player = FindAnyObjectByType<ThirdPersonPlayer>();
            if (player) target = player.transform;
        }
        desired.Clear();
        LoadedCount = 0; missingDesired = 0; PendingCount = 0;
        if (target)
        {
            var position = target.position;
            if ((position - stationaryAnchor).sqrMagnitude > .0625f)
            { stationaryAnchor = position; lastMovementTime = Time.realtimeSinceStartup; }
            float distance = Mathf.Max(SafetyMargin, Application.isMobilePlatform ? mobileLoadDistance : desktopLoadDistance);
            State nearest = null;
            foreach (var state in states)
            {
                state.loaded = SceneLoaded(state.name);
                if (state.loaded) LoadedCount++;
                state.distanceSquared = state.definition.bounds.SqrDistance(position);
                if (nearest == null || state.distanceSquared < nearest.distanceSquared) nearest = state;
                if (state.distanceSquared <= distance * distance || Protected(state, position)) desired.Add(state.name);
            }
            // Metadata gaps must still prefetch the nearest sector rather than silently loading none.
            if (desired.Count == 0 && nearest != null) desired.Add(nearest.name);
            foreach (var state in states)
            {
                if (desired.Contains(state.name) && !state.loaded) missingDesired++;
                if (CanUnload(state, position, distance)) PendingCount++;
            }
            PendingCount += missingDesired;
            if (operation != null && !operation.isDone && operationScene != null && operationIsLoad && !desired.Contains(operationScene)) PendingCount++;
        }
        else
        {
            foreach (var state in states) if (SceneLoaded(state.name)) LoadedCount++;
        }
        Ready = target && metadataValid && desired.Count > 0 && missingDesired == 0;
        IsPreparing = !Ready;
    }

    void BuildIndex()
    {
        indexedSectors = sectors;
        states.Clear(); metadataValid = true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (sectors == null || sectors.Length == 0) { metadataValid = false; return; }
        foreach (var sector in sectors)
        {
            if (sector == null || string.IsNullOrEmpty(sector.scene) || !sector.scene.StartsWith("CitySector_", StringComparison.Ordinal) ||
                sector.scene.Contains("/") || sector.scene.Contains("\\") || sector.scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(sector.scene) || !Finite(sector.bounds.center) || !Finite(sector.bounds.size) ||
                sector.bounds.size.x <= 0 || sector.bounds.size.y < 0 || sector.bounds.size.z <= 0)
            {
                metadataValid = false;
                LastError = "Invalid or duplicated CitySector metadata.";
                Debug.LogError(LastError);
                continue;
            }
            states.Add(new State { definition = sector, name = sector.scene, loaded = SceneLoaded(sector.scene) });
        }
    }

    State NextLoad()
    {
        State nearest = null;
        foreach (var state in states)
            if (desired.Contains(state.name) && !state.loaded && state.retryAt <= Time.realtimeSinceStartup &&
                (nearest == null || state.distanceSquared < nearest.distanceSquared)) nearest = state;
        return nearest;
    }

    State NextUnload()
    {
        State farthest = null;
        float distance = Mathf.Max(SafetyMargin, Application.isMobilePlatform ? mobileLoadDistance : desktopLoadDistance);
        foreach (var state in states)
            if (CanUnload(state, target.position, distance) && state.retryAt <= Time.realtimeSinceStartup &&
                (farthest == null || state.distanceSquared > farthest.distanceSquared)) farthest = state;
        return farthest;
    }

    bool CanUnload(State state, Vector3 position, float loadDistance)
    {
        return state.loaded && !desired.Contains(state.name) && !Protected(state, position) &&
            (lowMemoryTrim || state.distanceSquared > (loadDistance + UnloadHysteresis) * (loadDistance + UnloadHysteresis));
    }

    static bool Protected(State state, Vector3 position)
    {
        var safe = state.definition.bounds;
        safe.Expand(SafetyMargin * 2);
        return safe.Contains(position);
    }

    void BeginLoad(State state, int token)
    {
        operation = null; operationScene = state.name; operationIsLoad = true;
        try
        {
            if (!Application.CanStreamedLevelBeLoaded(state.name)) { Fail(state, "Sector is not in Build Settings: " + state.name); return; }
            operation = SceneManager.LoadSceneAsync(state.name, LoadSceneMode.Additive);
            if (operation == null) { Fail(state, "Unity did not begin loading sector: " + state.name); return; }
            string name = state.name;
            operation.completed += completed =>
            {
                if (this && !shuttingDown && token == generation) return;
                // A component destruction cancels managed scheduling, not Unity's native operation.
                // A replacement controller discovers loaded registered scenes and owns their queue.
                if (quitting || !Application.isPlaying || active) return;
                var abandoned = SceneManager.GetSceneByName(name);
                if (abandoned.IsValid() && abandoned.isLoaded) SceneManager.UnloadSceneAsync(abandoned);
            };
        }
        catch (Exception exception) { Fail(state, "Cannot load " + state.name + ": " + exception.Message); }
    }

    void BeginUnload(State state)
    {
        operation = null; operationScene = state.name; operationIsLoad = false;
        try { operation = SceneManager.UnloadSceneAsync(state.name); }
        catch (Exception exception) { Fail(state, "Cannot unload " + state.name + ": " + exception.Message); }
        if (operation == null) state.retryAt = Time.realtimeSinceStartup + 4;
    }

    IEnumerator WaitForOperation(int token)
    {
        float nextPoll = Time.realtimeSinceStartup;
        while (operation != null && !operation.isDone && !shuttingDown && token == generation)
        {
            if (Time.realtimeSinceStartup >= nextPoll) { RefreshNow(); nextPoll = Time.realtimeSinceStartup + PollSeconds; }
            yield return null;
        }
        RefreshNow();
    }

    void ClearOperation() { operation = null; operationScene = null; }
    void Fail(State state, string message)
    {
        state.retryAt = Time.realtimeSinceStartup + 4;
        LastError = message;
        Debug.LogError(message);
    }
    static bool SceneLoaded(string name) { var scene = SceneManager.GetSceneByName(name); return scene.IsValid() && scene.isLoaded; }
    static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    public bool IsSectorLoaded(string sceneName) { return !string.IsNullOrEmpty(sceneName) && SceneLoaded(sceneName); }
    public string[] GetLoadedSectorNames()
    {
        var names = new List<string>();
        foreach (var state in states) if (SceneLoaded(state.name)) names.Add(state.name);
        names.Sort(StringComparer.Ordinal); return names.ToArray();
    }
    public string[] GetDesiredSectorNames() { var names = new List<string>(desired); names.Sort(StringComparer.Ordinal); return names.ToArray(); }
    void OnLowMemory() { lowMemoryTrim = true; }
    void OnApplicationQuit() { quitting = true; }
    void OnDestroy()
    {
        shuttingDown = true; generation++;
        StopAllCoroutines();
        Application.lowMemory -= OnLowMemory;
        Ready = false; PendingCount = 0;
        if (active == this) { active = null; IsPreparing = false; }
    }
}
