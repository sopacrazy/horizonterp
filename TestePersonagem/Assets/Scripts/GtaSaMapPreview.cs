using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Navigation, local-only protection and actual game-camera captures for the separate SA test.
[DefaultExecutionOrder(-80)]
public sealed class GtaSaMapPreview : MonoBehaviour
{
    [Serializable] public sealed class Destination
    {
        public string name;
        public Vector3 position;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public int loadedSectors, desiredSectors, renderers, colliders, visibleMaterials;
        public int barracosProbed, barracosWithoutNearbySupport, barracosWithSourceOverlap;
        public float walkDistance, runDistance, averageFps, processMemoryMb, largestVerticalDifference;
        public float unityReservedMemoryMb, unityAllocatedMemoryMb;
        public string[] supportExamples;
        public string[] unsupportedExamples;
        public string[] sourceOverlapExamples;
        public string terrainAudit;
        public PatchProbe[] terrainPatchProbes;
        public Vector3 playerPosition;
        public string error;
        public string[] captures;
        public RegionSample[] regions;
    }
    [Serializable] sealed class RegionSample
    {
        public string name, error;
        public int loadedSectors, desiredSectors, renderers, colliders;
        public bool grounded;
        public float fps, unityReservedMemoryMb, unityAllocatedMemoryMb;
        public Vector3 playerPosition;
        public string[] captures;
    }
    [Serializable] sealed class PatchProbe
    {
        public string name;
        public int passedSamples;
        public PatchProbePoint[] samples;
    }
    [Serializable] sealed class PatchProbePoint
    {
        public Vector3 point;
        public bool colliderHit;
        public float hitY, heightError, normalY, topSurfaceY;
        public string topSurface;
    }

    public ThirdPersonPlayer player;
    public bool integratedReview;
    public bool regionalReview;
    public GtaSaSectorStreaming streaming;
    public Camera[] reviewCameras = Array.Empty<Camera>();
    public Destination[] destinations = Array.Empty<Destination>();
    public Vector3 focus;
    public string reviewDirectory = "";
    readonly List<string> captures = new List<string>();
    readonly List<RegionSample> regions = new List<RegionSample>();
    readonly Dictionary<string,PatchProbe> terrainPatchProbes = new Dictionary<string,PatchProbe>(StringComparer.Ordinal);
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> localTransport;
    bool verify, teleporting;
    bool lastTeleportSucceeded;
    string lastTeleportError = "";
    string error = "";
    int destinationIndex;
    int barracosProbed, barracosWithoutNearbySupport, barracosWithSourceOverlap;
    float walkDistance, runDistance, averageFps, processMemoryMb, largestVerticalDifference;
    float unityReservedMemoryMb, unityAllocatedMemoryMb;
    readonly List<string> supportExamples = new List<string>();
    readonly List<string> unsupportedExamples = new List<string>();
    readonly List<string> sourceOverlapExamples = new List<string>();
    string terrainAudit = "não executado";

    string Folder => string.IsNullOrEmpty(reviewDirectory)
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/gta-sa-import/review"))
        : Path.GetFullPath(reviewDirectory);

    void Awake()
    {
        verify = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-sa-map") >= 0;
        if (integratedReview && !verify) { enabled = false; return; }
        localTransport = RejectRequest;
        AccountSession.VerificationTransport = localTransport;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadowDistance = Application.isMobilePlatform ? 0 : 95;
        Application.targetFrameRate = Application.isMobilePlatform ? 30 : 60;
        if (Application.isMobilePlatform)
        {
            QualitySettings.globalTextureMipmapLimit = 2;
            QualitySettings.shadows = ShadowQuality.Disable;
            RenderSettings.fogStartDistance = 85;
            RenderSettings.fogEndDistance = 225;
            if (Camera.main) Camera.main.farClipPlane = 270;
        }
    }

    IEnumerator RejectRequest(string method, string path, string body, bool authenticated, Action<ApiReply,string> done)
    {
        error = "Requisição externa recusada na cena de teste: " + method + " " + path;
        done(null, "Mapa de teste local, sem API.");
        yield break;
    }

    IEnumerator Start()
    {
        // The 3x3 regional review renders every adjacent sector; normal play stays distance-based.
        if (verify && streaming && streaming.sectors.Length <= 16)
            streaming.desktopRadius = 850;
        else if (verify && streaming)
            streaming.desktopRadius = 350;
        float started = Time.realtimeSinceStartup;
        float loadTimeout = streaming && streaming.sectors.Length > 16 ? 90 : 45;
        while (streaming && !streaming.Ready && Time.realtimeSinceStartup - started < loadTimeout)
            yield return new WaitForSecondsRealtime(.2f);
        if (!streaming || !streaming.Ready) error = "Setor inicial não carregou: " + (streaming ? streaming.LastError : "streamer ausente");
        else if (player && TryFloor(focus, out var ground))
        {
            player.spawnPosition = ground + Vector3.up * .07f;
            player.transform.position = player.spawnPosition;
            player.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(.35f);
            if (!player.IsGrounded) error = "Jogador não estabilizou no piso inicial.";
        }
        else error = "Nenhum piso acessível foi encontrado perto do ponto inicial.";
        if (verify)
        {
            Directory.CreateDirectory(Folder);
            if (string.IsNullOrEmpty(error))
            {
                yield return TestMovement();
                if (streaming && streaming.sectors.Length <= 16)
                {
                    ProbeBarracoSupport();
                    AuditTerrainPair();
                }
                yield return SamplePerformance();
            }
            yield return new WaitForEndOfFrame();
            int firstCapture = captures.Count;
            if (Camera.main) Capture(Camera.main, "Jogabilidade-GTA-SA.png", 1440, 900);
            foreach (var camera in reviewCameras)
                if (camera) Capture(camera, SafeFile(camera.name) + ".png", 1600, 1000);
            if (streaming && streaming.sectors.Length > 16)
            {
                regions.Add(Region("Los Santos — início", firstCapture, "", averageFps));
                if (string.IsNullOrEmpty(error)) yield return CaptureFullRegions();
                if (!regionalReview && string.IsNullOrEmpty(error) && (terrainPatchProbes.Count != 5 ||
                    terrainPatchProbes.Values.Any(item => item.passedSamples != 5)))
                    error = "Sondagem de piso incompleta: " + terrainPatchProbes.Count +
                        "/5 reparos; falhas=" + string.Join(", ", terrainPatchProbes.Values
                            .Where(item => item.passedSamples != 5)
                            .Select(item => item.name + " " + item.passedSamples + "/5"));
            }
            WriteReport();
            Application.Quit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }

    void Update()
    {
        if (verify || teleporting || destinations == null || destinations.Length == 0) return;
        if (Input.GetKeyDown(KeyCode.F2)) StartCoroutine(TeleportNext());
    }

    IEnumerator TeleportNext()
    {
        destinationIndex = (destinationIndex + 1) % destinations.Length;
        yield return TeleportTo(destinations[destinationIndex]);
    }

    IEnumerator TeleportTo(Destination destination)
    {
        teleporting = true;
        lastTeleportSucceeded = false;
        lastTeleportError = "";
        Vector3 previous = player.transform.position;
        var controller = player.GetComponent<CharacterController>();
        player.DemoMode = true; player.DemoInput = Vector2.zero;
        player.enabled = false;
        controller.enabled = false;
        if (streaming && Application.isMobilePlatform)
            yield return streaming.ClearLoadedForTeleport();
        player.transform.position = destination.position + Vector3.up * 80;
        if (streaming) streaming.RefreshNow();
        float started = Time.realtimeSinceStartup;
        while (streaming && !streaming.Ready && Time.realtimeSinceStartup - started < 90)
            yield return new WaitForSecondsRealtime(.2f);
        if (streaming && streaming.Ready && TryFloor(destination.position, out var floor))
        {
            player.transform.position = floor + Vector3.up * .07f;
            player.spawnPosition = player.transform.position;
            lastTeleportSucceeded = true;
        }
        else
        {
            lastTeleportError = "Destino sem piso/setores prontos: " + destination.name +
                (streaming && !string.IsNullOrEmpty(streaming.LastError) ? " — " + streaming.LastError : "");
            Debug.LogWarning(lastTeleportError);
            player.transform.position = previous;
            if (streaming) streaming.RefreshNow();
        }
        controller.enabled = true;
        player.enabled = true;
        player.DemoMode = false;
        if (lastTeleportSucceeded)
        {
            float unloadingStarted = Time.realtimeSinceStartup;
            while (streaming && streaming.LoadedCount > streaming.DesiredCount + 2 &&
                   Time.realtimeSinceStartup - unloadingStarted < 30)
                yield return new WaitForSecondsRealtime(.2f);
        }
        teleporting = false;
    }

    IEnumerator CaptureFullRegions()
    {
        if (destinations == null || destinations.Length == 0) yield break;
        foreach (var destination in destinations.Take(5))
        {
            if (destination == null) continue;
            yield return TeleportTo(destination);
            if (!lastTeleportSucceeded)
            {
                regions.Add(Region(destination.name, captures.Count, lastTeleportError, 0));
                if (string.IsNullOrEmpty(error)) error = lastTeleportError;
                break;
            }
            yield return new WaitForSecondsRealtime(.35f);
            ProbeLoadedTerrainPatches();
            float fps = 0;
            int frames = 0;
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < .75f) { frames++; yield return null; }
            fps = frames / Mathf.Max(.001f, Time.realtimeSinceStartup - started);
            int firstCapture = captures.Count;
            string safe = SafeFile(destination.name);
            if (Camera.main) Capture(Camera.main, "Jogabilidade-" + safe + ".png", 1440, 900);
            if (reviewCameras != null && reviewCameras.Length > 0 && reviewCameras[0])
            {
                var aerial = reviewCameras[0];
                Vector3 oldPosition = aerial.transform.position;
                Quaternion oldRotation = aerial.transform.rotation;
                float oldFar = aerial.farClipPlane;
                Vector3 target = player.transform.position;
                aerial.transform.position = target + new Vector3(-45, 155, -80);
                aerial.transform.rotation = Quaternion.LookRotation(target - aerial.transform.position);
                aerial.farClipPlane = 420;
                Capture(aerial, "Vista-aérea-" + safe + ".png", 1600, 1000);
                aerial.transform.SetPositionAndRotation(oldPosition, oldRotation);
                aerial.farClipPlane = oldFar;
            }
            var sample = Region(destination.name, firstCapture, "", fps);
            regions.Add(sample);
            if (!sample.grounded && string.IsNullOrEmpty(error))
                error = "Jogador não estabilizou no piso de " + destination.name;
        }
    }

    RegionSample Region(string name, int firstCapture, string regionError, float fps)
    {
        if (streaming) streaming.RefreshNow();
        return new RegionSample {
            name = name, error = regionError,
            loadedSectors = streaming ? streaming.LoadedCount : 0,
            desiredSectors = streaming ? streaming.DesiredCount : 0,
            renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length,
            colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None).Length,
            grounded = player && player.IsGrounded,
            fps = fps,
            unityReservedMemoryMb = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f,
            unityAllocatedMemoryMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
            playerPosition = player ? player.transform.position : Vector3.zero,
            captures = captures.Skip(firstCapture).ToArray()
        };
    }

    void ProbeLoadedTerrainPatches()
    {
        Physics.SyncTransforms();
        foreach (var marker in FindObjectsByType<GtaSaTerrainPatchMarker>(FindObjectsSortMode.None))
        {
            if (!marker || terrainPatchProbes.ContainsKey(marker.patchName)) continue;
            var collider = marker.GetComponentInChildren<MeshCollider>();
            var samples = new List<PatchProbePoint>();
            foreach (var local in marker.localSamples ?? Array.Empty<Vector3>())
            {
                Vector3 point = marker.transform.TransformPoint(local);
                var ray = new Ray(point + Vector3.up * 3, Vector3.down);
                RaycastHit patchHit = default;
                bool hit = collider && collider.Raycast(ray, out patchHit, 6);
                float heightError = hit ? Mathf.Abs(patchHit.point.y - point.y) : -1;
                float topY = float.NegativeInfinity;
                string topName = "nenhum";
                foreach (var worldHit in Physics.RaycastAll(ray, 6, 1, QueryTriggerInteraction.Ignore))
                    if (worldHit.normal.y >= .72f && worldHit.point.y > topY)
                    { topY = worldHit.point.y; topName = FindInstanceName(worldHit.collider.transform); }
                samples.Add(new PatchProbePoint {
                    point=point, colliderHit=hit, hitY=hit ? patchHit.point.y : -9999,
                    heightError=heightError, normalY=hit ? patchHit.normal.y : 0,
                    topSurfaceY=topY > float.NegativeInfinity ? topY : -9999, topSurface=topName
                });
            }
            int passed = samples.Count(item => item.colliderHit && item.heightError < .12f && item.normalY >= .72f);
            terrainPatchProbes.Add(marker.patchName, new PatchProbe {
                name=marker.patchName, passedSamples=passed, samples=samples.ToArray()
            });
            Debug.Log("GTA_SA_PATCH_FLOOR " + marker.patchName + " samples=" + passed + "/" + samples.Count);
        }
    }

    static bool TryFloor(Vector3 near, out Vector3 floor)
    {
        floor = default;
        float best = float.PositiveInfinity;
        for (int x = -12; x <= 12; x += 2)
            for (int z = -12; z <= 12; z += 2)
            {
                Vector3 origin = new Vector3(near.x + x * 4, near.y + 120, near.z + z * 4);
                foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 260, 1, QueryTriggerInteraction.Ignore))
                {
                    if (hit.normal.y < .72f || hit.point.y > near.y + 2 || hit.point.y < near.y - 25) continue;
                    var at = hit.point + Vector3.up * .55f;
                    // Reserve headroom for the player and the third-person camera.
                    if (Physics.CheckCapsule(at, hit.point + Vector3.up * 2.75f, .43f, 1, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Raycast(hit.point + Vector3.up * 2.8f, Vector3.up, 2.5f, 1, QueryTriggerInteraction.Ignore)) continue;
                    float score = (hit.point - near).sqrMagnitude;
                    if (hit.collider.name.StartsWith("barraco", StringComparison.OrdinalIgnoreCase)) score += 10000;
                    if (score < best) { best = score; floor = hit.point; }
                }
            }
        return best < float.PositiveInfinity;
    }

    IEnumerator TestMovement()
    {
        if (!player) { error = "Jogador ausente para teste de locomoção."; yield break; }
        var controller = player.GetComponent<CharacterController>();
        if (!controller) { error = "CharacterController ausente."; yield break; }
        Vector3 start = player.transform.position;
        if (integratedReview && NeighborhoodMap.Active)
        {
            controller.enabled = false;
            var recovered = NeighborhoodMap.Active.ResolveSavedPosition(new Vector3(0, -59.975f, -10.7f), controller);
            controller.enabled = true;
            if (recovered.y < -45f || Vector3.Distance(recovered, NeighborhoodMap.Active.SpawnPosition) > 8f)
            { error = "Save do interior antigo não foi recuperado para a rua."; yield break; }
            Debug.Log("SA_LEGACY_INTERIOR_RECOVERY_OK position=" + recovered);
        }
        Vector2[] directions = {
            Vector2.up, Vector2.right, Vector2.left, Vector2.down,
            new Vector2(1,1).normalized, new Vector2(-1,1).normalized,
            new Vector2(1,-1).normalized, new Vector2(-1,-1).normalized
        };
        foreach (bool run in new[] { false, true })
        {
            float best = 0;
            foreach (var direction in directions)
            {
                controller.enabled = false;
                player.transform.position = start;
                controller.enabled = true;
                player.DemoMode = true; player.DemoInput = direction; player.DemoRun = run;
                float began = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - began < (run ? .55f : 1.25f)) yield return null;
                player.DemoInput = Vector2.zero; player.DemoRun = false;
                yield return new WaitForSecondsRealtime(.08f);
                Vector3 delta = player.transform.position - start;
                float horizontal = new Vector2(delta.x,delta.z).magnitude;
                if (Mathf.Abs(delta.y) < 1.6f && player.IsGrounded) best = Mathf.Max(best,horizontal);
                if (best >= (run ? 1.6f : 1.15f)) break;
            }
            if (run) runDistance = best; else walkDistance = best;
        }
        player.DemoInput = Vector2.zero; player.DemoRun = false; player.DemoMode = false;
        controller.enabled = false; player.transform.position = start; controller.enabled = true;
        yield return new WaitForSecondsRealtime(.12f);
        if (walkDistance < 1.15f || runDistance < 1.6f)
            error = "Percurso livre de caminhada/corrida não comprovado no início: " +
                walkDistance.ToString("F2") + " m / " + runDistance.ToString("F2") + " m.";
    }

    void ProbeBarracoSupport()
    {
        var barracos = FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(item => item.name.StartsWith("barraco #", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => (item.position - focus).sqrMagnitude).Take(48);
        foreach (var barraco in barracos)
        {
            barracosProbed++;
            float closest = float.PositiveInfinity;
            float supportedY = -10000;
            string supportName = "nenhum";
            var origin = barraco.position + Vector3.up * 10;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 120, 1, QueryTriggerInteraction.Ignore))
            {
                if (Inside(hit.collider.transform, barraco)) continue;
                float difference = Mathf.Abs(hit.point.y - barraco.position.y);
                if (difference >= closest) continue;
                closest = difference; supportedY = hit.point.y;
                supportName = FindInstanceName(hit.collider.transform);
            }
            float signedGap = closest < float.PositiveInfinity ? barraco.position.y - supportedY : 110;
            float gap = Mathf.Abs(signedGap);
            largestVerticalDifference = Mathf.Max(largestVerticalDifference, gap);
            string detail = barraco.name + " x=" + barraco.position.x.ToString("F3") +
                " y=" + barraco.position.y.ToString("F3") + " z=" + barraco.position.z.ToString("F3") +
                " apoio=" + supportedY.ToString("F3") + " desnível=" + signedGap.ToString("F3") +
                " por=" + supportName;
            if (signedGap > 2.5f)
            {
                barracosWithoutNearbySupport++;
                unsupportedExamples.Add(detail);
            }
            else if (signedGap < -2.5f) { barracosWithSourceOverlap++; sourceOverlapExamples.Add(detail); }
            if (supportExamples.Count < 12)
                supportExamples.Add(barraco.name + " base=" + barraco.position.y.ToString("F2") +
                    " apoio=" + supportedY.ToString("F2") + " gap=" + gap.ToString("F2") + " por=" + supportName);
        }
    }

    static bool Inside(Transform candidate, Transform root)
    {
        for (var current = candidate; current; current = current.parent) if (current == root) return true;
        return false;
    }

    static string FindInstanceName(Transform candidate)
    {
        for (var node = candidate; node; node = node.parent)
            if (node.name.Contains("#")) return node.name;
        return candidate ? candidate.name : "nenhum";
    }

    void AuditTerrainPair()
    {
        var root = FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(item => item.name.StartsWith("hillpalos06", StringComparison.OrdinalIgnoreCase) && item.name.Contains("#"));
        if (!root) { terrainAudit = "hillpalos06 ausente"; return; }
        var ray = new Ray(new Vector3(1162.235974f, 150, 2108.937664f), Vector3.down);
        float collisionY = float.NaN, visualY = float.NaN;
        foreach (var collider in root.GetComponentsInChildren<MeshCollider>())
            if (collider.Raycast(ray, out var hit, 250) && (float.IsNaN(collisionY) || hit.point.y > collisionY))
                collisionY = hit.point.y;
        string firstVertex = "nenhum";
        bool oldBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh || filter.transform.name.StartsWith("COL ", StringComparison.Ordinal)) continue;
            if (firstVertex == "nenhum" && filter.sharedMesh.vertexCount > 0)
                firstVertex = (filter.sharedMesh.isReadable ? filter.sharedMesh.vertices[0].ToString("F3") : "not-readable") +
                    " local=" + filter.transform.localPosition.ToString("F3") +
                    " scale=" + filter.transform.localScale.ToString("F3");
            var temporary = filter.gameObject.AddComponent<MeshCollider>();
            temporary.sharedMesh = filter.sharedMesh;
            Physics.SyncTransforms();
            if (temporary.Raycast(ray, out var hit, 250) && (float.IsNaN(visualY) || hit.point.y > visualY)) visualY = hit.point.y;
            Destroy(temporary);
        }
        Physics.queriesHitBackfaces = oldBackfaces;
        terrainAudit = root.name + " rootY=" + root.position.y.ToString("F2") +
            " visualY=" + visualY.ToString("F2") + " colY=" + collisionY.ToString("F2") +
            " Δ=" + Mathf.Abs(visualY-collisionY).ToString("F3") + " firstVertex=" + firstVertex;
    }

    IEnumerator SamplePerformance()
    {
        int frames = 0;
        float started = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - started < 1.5f) { frames++; yield return null; }
        averageFps = frames / Mathf.Max(.001f,Time.realtimeSinceStartup - started);
        processMemoryMb = (float)(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0);
        if (processMemoryMb <= 0) processMemoryMb = -1; // Unsupported by this Unity player/runtime.
        unityReservedMemoryMb = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f;
        unityAllocatedMemoryMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
    }

    void Capture(Camera camera, string name, int width, int height)
    {
        var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);
            image.Apply();
            string path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, image.EncodeToPNG());
            captures.Add(path);
            Destroy(image);
        }
        catch (Exception exception) { error = "Falha ao capturar " + name + ": " + exception.Message; }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            texture.Release(); Destroy(texture);
        }
    }

    void WriteReport()
    {
        int renderers = 0, colliders = 0, materials = 0;
        foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        { renderers++; materials += renderer.sharedMaterials.Length; }
        colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
        var report = new Report {
            passed = string.IsNullOrEmpty(error) && renderers > 0 && colliders > 0,
            loadedSectors = streaming ? streaming.LoadedCount : 0,
            desiredSectors = streaming ? streaming.DesiredCount : 0,
            renderers = renderers, colliders = colliders, visibleMaterials = materials,
            playerPosition = player ? player.transform.position : Vector3.zero,
            barracosProbed = barracosProbed, barracosWithoutNearbySupport = barracosWithoutNearbySupport,
            barracosWithSourceOverlap = barracosWithSourceOverlap,
            largestVerticalDifference = largestVerticalDifference, walkDistance = walkDistance, runDistance = runDistance,
            averageFps = averageFps, processMemoryMb = processMemoryMb,
            unityReservedMemoryMb = unityReservedMemoryMb, supportExamples = supportExamples.ToArray(),
            unityAllocatedMemoryMb = unityAllocatedMemoryMb,
            unsupportedExamples = unsupportedExamples.ToArray(),
            sourceOverlapExamples = sourceOverlapExamples.ToArray(),
            terrainAudit = terrainAudit,
            terrainPatchProbes = terrainPatchProbes.Values.OrderBy(item => item.name, StringComparer.Ordinal).ToArray(),
            error = error, captures = captures.ToArray(), regions = regions.ToArray()
        };
        File.WriteAllText(Path.Combine(Folder,"runtime.json"),JsonUtility.ToJson(report,true));
        if (!report.passed) Debug.LogError("GTA_SA_TEST_FAILED " + error);
        else Debug.Log("GTA_SA_TEST_PASSED renderers=" + renderers + " sectors=" + report.loadedSectors);
    }

    static string SafeFile(string text)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid,'-');
        return text.Replace(' ','-');
    }

    void OnGUI()
    {
        if (verify) return;
        string next = streaming && streaming.sectors.Length > 16 ? "próxima região" : "próximo setor convertido";
        GUI.Label(new Rect(12,12,680,55), "GTA San Andreas modificado · TESTE SEPARADO\nWASD mover · Shift correr · F2 " + next + " · R voltar");
        if (streaming) GUI.Label(new Rect(12,70,500,24), "Setores ativos: " + streaming.LoadedCount + " / " + streaming.DesiredCount);
    }

    void OnDestroy()
    {
        if (AccountSession.VerificationTransport == localTransport) AccountSession.VerificationTransport = null;
    }
}
