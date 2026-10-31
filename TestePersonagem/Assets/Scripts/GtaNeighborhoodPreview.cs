using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Exploration and QA for a separate neighbourhood assembled from recovered scenery.
[DefaultExecutionOrder(-80)]
public sealed class GtaNeighborhoodPreview : MonoBehaviour
{
    [Serializable] public sealed class Destination
    {
        public string name;
        public Vector3 position;
        public float yaw;
    }

    public ThirdPersonPlayer player;
    public Transform[] floorCheckpoints = Array.Empty<Transform>();
    public Transform[] walkCheckpoints = Array.Empty<Transform>();
    public Camera[] reviewCameras = Array.Empty<Camera>();
    public GameObject[] originalAssemblies = Array.Empty<GameObject>();
    public Collider collisionObstacle;
    public string reviewDirectory = "";
    public Bounds districtBounds;
    public string[] sourceNames = Array.Empty<string>();
    public Destination[] destinations = Array.Empty<Destination>();

    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> captures = new List<string>();
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> localTransport;
    CharacterController controller;
    CityDayNight lighting;
    bool verify, finished, capturing, menuOpen;
    int rejectedRequests, floorSamples, groundedSamples, destinationSamples;
    int rendererCount, materialSlots, missingMaterials, invalidShaders;
    float walkDistance, runDistance, started;
    Vector2 scroll;

    [Serializable] sealed class Report
    {
        public bool passed;
        public string capturedUtc;
        public int sourceAssemblies, rejectedRequests, remoteRequests;
        public int floorSamples, groundedSamples, destinationSamples;
        public int renderers, materialSlots, missingMaterials, invalidShaders;
        public float walkDistance, runDistance, elapsedSeconds;
        public string[] sourceNames;
        public Bounds districtBounds;
        public List<string> checks, errors, captures;
    }

    string ReviewFolder => string.IsNullOrWhiteSpace(reviewDirectory)
        ? Path.GetFullPath(Path.Combine(Application.dataPath, Application.isEditor
            ? "../../tools/gta-neighborhood-review" : "../../../../tools/gta-neighborhood-review"))
        : Path.GetFullPath(reviewDirectory);

    void Awake()
    {
        verify = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-neighborhood") >= 0;
        localTransport = RejectRequest;
        AccountSession.VerificationTransport = localTransport;
        Application.runInBackground = true;
        if (verify) Application.logMessageReceived += OnLog;
        if (!player) player = FindAnyObjectByType<ThirdPersonPlayer>();
        if (player) controller = player.GetComponent<CharacterController>();
        lighting = FindAnyObjectByType<CityDayNight>();
        GtaCityEnvironment.ConfigureSurfaces(gameObject.transform);
        foreach (var assembly in originalAssemblies)
            if (assembly && !assembly.transform.IsChildOf(transform)) GtaCityEnvironment.ConfigureSurfaces(assembly.transform);
    }

    IEnumerator RejectRequest(string method, string path, string body, bool authenticated, Action<ApiReply,string> done)
    {
        rejectedRequests++;
        errors.Add("Solicitação bloqueada na prévia: " + method + " " + path);
        done(null, "Bairro de teste local, sem conexão à conta ou à API.");
        yield break;
    }

    IEnumerator Start()
    {
        yield return null;
        ApplyQuality();
        if (!verify) yield break;
        Directory.CreateDirectory(ReviewFolder);
        started = Time.realtimeSinceStartup;
        var pending = new Stack<IEnumerator>();
        pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            bool moved = false;
            object instruction = null;
            Exception failure = null;
            try
            {
                if (Time.realtimeSinceStartup - started > 90)
                    throw new TimeoutException("A verificação do bairro excedeu 90 segundos.");
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null)
            {
                errors.Add(failure.ToString());
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                break;
            }
            if (!moved) pending.Pop();
            else if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        Finish();
    }

    void ApplyQuality()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Application.isMobilePlatform ? 30 : 60;
        QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 4;
        QualitySettings.shadowDistance = Application.isMobilePlatform ? 30 : 80;
        QualitySettings.shadowCascades = Application.isMobilePlatform ? 0 : 2;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        if (Application.isMobilePlatform) QualitySettings.globalTextureMipmapLimit = 1;
    }

    void Update()
    {
        if (lighting) Shader.SetGlobalFloat("_RecoveredCityNight", lighting.IsNight ? 1 : 0);
        if (verify) return;
        if (Input.GetKeyDown(KeyCode.F2)) menuOpen = !menuOpen;
        if (!GameHud.Instance && lighting && Input.GetKeyDown(KeyCode.N)) lighting.Toggle();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(.7f);
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance, "Cena local sem conta ou multiplayer");
        Check(AccountSession.VerificationTransport == localTransport, "Transporte externo bloqueado na prévia");
        Check(NeighborhoodMap.Active && !NeighborhoodMap.Active.isOfficialCity, "Bairro identificado como exemplo separado");
        if (!Check(player && controller && player.cameraTransform && Camera.main, "Personagem, cápsula e câmera presentes")) yield break;
        player.DemoMode = true;
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        Physics.SyncTransforms();
        VerifyAssemblies();
        VerifyMaterials();
        Check(TryFloor(player.spawnPosition, out _), "Piso original sob o início do jogador");
        yield return VerifyCheckpoints();
        VerifyDestinations();

        if (TryWalkRoute(out var from, out var to))
        {
            yield return Travel(from, to, false);
            yield return Travel(to, from, true);
        }
        else Check(false, "Percurso de dois a cinco metros livre para caminhada e corrida");
        if (collisionObstacle) yield return VerifyObstacle();
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        player.ResetPosition();
        yield return new WaitForSecondsRealtime(.3f);
        Check(player.IsGrounded && TryFloor(player.transform.position, out _), "Personagem apoiado após voltar ao início");
        yield return CaptureViews();
        Check(rejectedRequests == 0 && !AccountSession.Instance && !MultiplayerWorld.Instance,
            "Teste encerrado sem requisições externas, conta ou multiplayer");
    }

    void VerifyAssemblies()
    {
        Check(originalAssemblies != null && originalAssemblies.Length > 0, "Conjuntos recuperados presentes");
        Check(sourceNames != null && sourceNames.Length == originalAssemblies.Length,
            "Origem dos conjuntos registrada: " + sourceNames.Length);
        Check(Finite(districtBounds.center) && Finite(districtBounds.size) && districtBounds.size.x > 1 && districtBounds.size.z > 1,
            "Limites do bairro válidos: " + districtBounds.size);
        foreach (var assembly in originalAssemblies)
        {
            if (!Check(assembly, "Conjunto original definido")) continue;
            Check(Finite(assembly.transform.position) && Finite(assembly.transform.lossyScale) &&
                assembly.GetComponentsInChildren<MeshRenderer>(true).Length > 0,
                "Geometria e transformação válidas: " + assembly.name);
        }
    }

    void VerifyMaterials()
    {
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (!renderer.enabled) continue;
            rendererCount++;
            foreach (var material in renderer.sharedMaterials)
            {
                materialSlots++;
                if (!material) { missingMaterials++; if (missingMaterials < 12) errors.Add(renderer.name + ": material ausente"); }
                else if (!material.shader || !material.shader.isSupported || material.shader.name.Contains("InternalError"))
                { invalidShaders++; if (invalidShaders < 12) errors.Add(renderer.name + ": shader inválido em " + material.name); }
            }
        }
        Check(rendererCount > 0 && materialSlots > 0, "Cenário renderizável: " + rendererCount + " renderizadores, " + materialSlots + " materiais");
        Check(missingMaterials == 0 && invalidShaders == 0,
            "Materiais ausentes=" + missingMaterials + ", shaders inválidos=" + invalidShaders);
    }

    IEnumerator VerifyCheckpoints()
    {
        if (!Check(floorCheckpoints != null && floorCheckpoints.Length > 0, "Pontos de piso configurados")) yield break;
        int groundingInterval = Mathf.Max(1, Mathf.CeilToInt(floorCheckpoints.Length / 6f));
        for (int i = 0; i < floorCheckpoints.Length; i++)
        {
            floorSamples++;
            if (!Check(floorCheckpoints[i], "Piso " + i + ": ponto definido")) continue;
            Vector3 point = floorCheckpoints[i].position;
            if (!Check(TryFloor(point, out var floor), "Piso " + i + ": apoio próximo da altura esperada em " + point)) continue;
            if (!Check(ClearCapsule(floor.point), "Piso " + i + ": passagem livre para o personagem")) continue;
            if (i % groundingInterval != 0) continue;
            PlaceOnFloor(floor.point, player.spawnYaw);
            yield return new WaitForSecondsRealtime(.3f);
            groundedSamples++;
            bool supported = player.IsGrounded && TryFloor(player.transform.position, out var support) &&
                Mathf.Abs(player.transform.position.y + controller.center.y - controller.height * .5f - support.point.y) < .13f &&
                Vector3.ProjectOnPlane(player.transform.position - floor.point, Vector3.up).magnitude < .15f;
            Check(supported, "Piso " + i + ": cápsula apoiada após teleporte");
        }
    }

    void VerifyDestinations()
    {
        foreach (var destination in destinations)
        {
            if (!Check(destination != null && Finite(destination.position), "Destino definido")) continue;
            destinationSamples++;
            Check(TryFloor(destination.position, out var floor) && ClearCapsule(floor.point),
                "Destino acessível: " + destination.name);
        }
    }

    bool TryFloor(Vector3 point, out RaycastHit floor)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 3, 1, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => Mathf.Abs(a.point.y - point.y).CompareTo(Mathf.Abs(b.point.y - point.y)));
        foreach (var hit in hits)
            if (!hit.transform.IsChildOf(player.transform) && hit.normal.y > .7f && Mathf.Abs(hit.point.y - point.y) <= .3f)
            { floor = hit; return true; }
        floor = default;
        return false;
    }

    bool ClearCapsule(Vector3 foot)
    {
        float radius = controller.radius * .94f;
        var bottom = foot + Vector3.up * (radius + .055f);
        var top = foot + Vector3.up * (controller.height - radius + .055f);
        foreach (var collider in Physics.OverlapCapsule(bottom, top, radius, 1, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(player.transform)) return false;
        return true;
    }

    bool TryWalkRoute(out Vector3 from, out Vector3 to)
    {
        from = to = player.spawnPosition;
        if (walkCheckpoints != null && walkCheckpoints.Length >= 2 && walkCheckpoints[0] && walkCheckpoints[1])
        {
            from = walkCheckpoints[0].position;
            to = walkCheckpoints[1].position;
            return ClearRoute(from, to);
        }
        foreach (float radius in new[] { 0f, 3f, 6f })
            for (int anchor = 0; anchor < (radius == 0 ? 1 : 8); anchor++)
            {
                Vector3 first = player.spawnPosition + Quaternion.Euler(0, anchor * 45, 0) * Vector3.forward * radius;
                if (!TryFloor(first, out var floor)) continue;
                for (int direction = 0; direction < 16; direction++)
                {
                    Vector3 end = floor.point + Quaternion.Euler(0, direction * 22.5f, 0) * Vector3.forward * 2.5f;
                    if (!ClearRoute(floor.point, end)) continue;
                    from = floor.point; to = end;
                    return true;
                }
            }
        return false;
    }

    bool ClearRoute(Vector3 from, Vector3 to)
    {
        float distance = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
        if (distance < 1.8f || distance > 5.1f) return false;
        int steps = Mathf.CeilToInt(distance / .2f);
        float previous = from.y;
        for (int i = 0; i <= steps; i++)
        {
            if (!TryFloor(Vector3.Lerp(from, to, i / (float)steps), out var floor) || !ClearCapsule(floor.point)) return false;
            if (i > 0 && Mathf.Abs(floor.point.y - previous) > .22f) return false;
            previous = floor.point.y;
        }
        return true;
    }

    void PlaceOnFloor(Vector3 foot, float yaw)
    {
        controller.enabled = false;
        player.DemoInput = Vector2.zero;
        player.transform.SetPositionAndRotation(foot + Vector3.up * (.05f - controller.center.y + controller.height * .5f),
            Quaternion.Euler(0, yaw, 0));
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    IEnumerator Travel(Vector3 from, Vector3 to, bool run)
    {
        string label = run ? "Corrida" : "Caminhada";
        if (!Check(TryFloor(from, out var floor), label + ": piso inicial presente")) yield break;
        PlaceOnFloor(floor.point, 0);
        yield return new WaitForSecondsRealtime(.25f);
        Vector3 before = player.transform.position;
        float distance = Vector3.ProjectOnPlane(to - before, Vector3.up).magnitude;
        player.DemoRun = run;
        float deadline = Time.realtimeSinceStartup + distance / Mathf.Max(.2f, run ? player.runSpeed : player.walkSpeed) * 2 + 1;
        bool remainedSupported = true;
        while (Time.realtimeSinceStartup < deadline)
        {
            Vector3 direction = Vector3.ProjectOnPlane(to - player.transform.position, Vector3.up);
            if (direction.magnitude < .2f) break;
            direction.Normalize();
            Vector3 forward = Vector3.ProjectOnPlane(player.cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(player.cameraTransform.right, Vector3.up).normalized;
            player.DemoInput = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
            remainedSupported &= TryFloor(player.transform.position, out _);
            yield return null;
        }
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        yield return new WaitForSecondsRealtime(.2f);
        float moved = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
        if (run) runDistance = moved; else walkDistance = moved;
        Check(moved >= 1.5f && Vector3.ProjectOnPlane(to - player.transform.position, Vector3.up).magnitude < .4f,
            label + ": percurso real concluído, deslocamento=" + moved.ToString("F2") + " m");
        Check(remainedSupported && player.IsGrounded, label + ": personagem sustentado pelo piso sem queda");
    }

    IEnumerator VerifyObstacle()
    {
        if (!Check(collisionObstacle.enabled && !collisionObstacle.isTrigger, "Obstáculo sólido presente")) yield break;
        Bounds bounds = collisionObstacle.bounds;
        Vector3 direction = bounds.size.x <= bounds.size.z ? Vector3.right : Vector3.forward;
        float extent = bounds.size.x <= bounds.size.z ? bounds.extents.x : bounds.extents.z;
        Vector3 start = bounds.center - direction * (extent + controller.radius + .65f);
        start.y = player.spawnPosition.y;
        if (!Check(TryFloor(start, out var floor), "Piso na aproximação ao obstáculo")) yield break;
        PlaceOnFloor(floor.point, 0);
        yield return new WaitForSecondsRealtime(.2f);
        player.enabled = false;
        bool side = false;
        float attempted = 0;
        Vector3 before = player.transform.position;
        float deadline = Time.realtimeSinceStartup + .8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            float step = Mathf.Min(Time.unscaledDeltaTime, .05f) * 3;
            attempted += step;
            side |= (controller.Move(direction * step + Vector3.down * .01f) & CollisionFlags.Sides) != 0;
            yield return null;
        }
        float advanced = Vector3.Dot(player.transform.position - before, direction);
        player.enabled = true;
        Check(side && advanced < attempted * .8f, "Colisão impede atravessar o obstáculo");
    }

    void Visit(Destination destination)
    {
        if (verify || !player || !controller || destination == null || !TryFloor(destination.position, out var floor)) return;
        if (!ClearCapsule(floor.point)) return;
        PlaceOnFloor(floor.point, destination.yaw);
        var orbit = player.cameraTransform.GetComponent<ThirdPersonCamera>();
        if (orbit) orbit.ResetView();
        menuOpen = false;
    }

    IEnumerator CaptureViews()
    {
        float oldBias = QualitySettings.lodBias;
        int oldLevel = QualitySettings.maximumLODLevel;
        bool oldFog = RenderSettings.fog;
        try
        {
            capturing = true;
            QualitySettings.lodBias = 6;
            QualitySettings.maximumLODLevel = 0;
            RenderSettings.fog = false;
            yield return null;
            if (Camera.main) Capture(Camera.main, "gameplay");
            foreach (var camera in reviewCameras)
                if (camera) Capture(camera, SafeName(camera.name));
                else Check(false, "Câmera de revisão definida");
        }
        finally
        {
            capturing = false;
            QualitySettings.lodBias = oldBias;
            QualitySettings.maximumLODLevel = oldLevel;
            RenderSettings.fog = oldFog;
        }
    }

    void Capture(Camera camera, string name)
    {
        int width = camera.orthographic ? 1600 : 1440;
        int height = camera.orthographic ? 1600 : 900;
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var oldFlags = camera.clearFlags;
        var oldColor = camera.backgroundColor;
        var render = new RenderTexture(width, height, 24);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            if (camera.orthographic)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.13f, .18f, .22f);
            }
            camera.targetTexture = render;
            camera.Render();
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            string path = Path.Combine(ReviewFolder, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            captures.Add(path);
            var pixels = texture.GetPixels32();
            double sum = 0, square = 0;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 31)
            {
                double value = (pixels[i].r + pixels[i].g + pixels[i].b) / (3.0 * 255);
                sum += value; square += value * value; count++;
            }
            double mean = sum / count, variance = square / count - mean * mean;
            Check(mean > .005 && variance > .00001, "Captura renderizada: " + name);
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.clearFlags = oldFlags;
            camera.backgroundColor = oldColor;
            RenderTexture.active = oldActive;
            render.Release();
            Destroy(render); Destroy(texture);
        }
    }

    static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) &&
        !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    static string SafeName(string name)
    {
        foreach (char value in Path.GetInvalidFileNameChars()) name = name.Replace(value, '_');
        return name.Replace(' ', '-');
    }
    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }
    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + trace);
    }
    void Finish()
    {
        if (finished) return;
        finished = true;
        if (player) { player.DemoInput = Vector2.zero; player.DemoRun = false; }
        WriteReport();
        Debug.Log("GTA_NEIGHBORHOOD_VERIFICATION " + (errors.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks.Count + " floors=" + floorSamples);
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }
    void WriteReport()
    {
        Directory.CreateDirectory(ReviewFolder);
        var report = new Report
        {
            passed = errors.Count == 0, capturedUtc = DateTime.UtcNow.ToString("o"), sourceAssemblies = originalAssemblies.Length,
            rejectedRequests = rejectedRequests, remoteRequests = 0, floorSamples = floorSamples, groundedSamples = groundedSamples,
            destinationSamples = destinationSamples, renderers = rendererCount, materialSlots = materialSlots,
            missingMaterials = missingMaterials, invalidShaders = invalidShaders, walkDistance = walkDistance, runDistance = runDistance,
            elapsedSeconds = Time.realtimeSinceStartup - started, sourceNames = sourceNames, districtBounds = districtBounds,
            checks = checks, errors = errors, captures = captures
        };
        File.WriteAllText(Path.Combine(ReviewFolder, "runtime.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(ReviewFolder, "runtime.txt"), "Passed=" + report.passed + "\n" +
            "Walk=" + walkDistance.ToString("F3") + " m\nRun=" + runDistance.ToString("F3") + " m\n" +
            "Floor samples=" + floorSamples + "\nGrounded samples=" + groundedSamples + "\nRemote requests=0\n\n" +
            string.Join("\n", checks) + "\n\n" + string.Join("\n", errors));
    }
    void OnApplicationQuit()
    {
        if (verify && !finished) { errors.Add("Verificação interrompida antes de concluir."); WriteReport(); }
    }
    void OnGUI()
    {
        if (capturing) return;
        GUI.depth = -4900;
        float width = Mathf.Min(560, Screen.width - 24);
        GUI.Box(new Rect(12, 12, width, 68), GUIContent.none);
        GUI.Label(new Rect(24, 20, width - 24, 25), "BAIRRO RECUPERADO · TESTE SEPARADO · SEM CONTA ONLINE");
        GUI.Label(new Rect(24, 47, width - 24, 25), "WASD mover  ·  Shift correr  ·  Espaço pular  ·  R voltar  ·  F2 destinos");
        if (destinations.Length == 0) return;
        if (GUI.Button(new Rect(12, 85, 175, 30), menuOpen ? "Fechar destinos [F2]" : "Explorar destinos [F2]")) menuOpen = !menuOpen;
        if (!menuOpen || verify) return;
        float height = Mathf.Min(Screen.height - 140, destinations.Length * 42 + 18);
        GUI.Box(new Rect(12, 120, width, height), GUIContent.none);
        scroll = GUI.BeginScrollView(new Rect(20, 129, width - 16, height - 16), scroll,
            new Rect(0, 0, width - 40, destinations.Length * 42));
        for (int i = 0; i < destinations.Length; i++)
        {
            var destination = destinations[i];
            if (destination != null && GUI.Button(new Rect(4, i * 42, width - 56, 35), destination.name)) Visit(destination);
        }
        GUI.EndScrollView();
    }
    void OnDestroy()
    {
        if (verify) Application.logMessageReceived -= OnLog;
        if (AccountSession.VerificationTransport == localTransport) AccountSession.VerificationTransport = null;
        Shader.SetGlobalFloat("_RecoveredCityNight", 0);
    }
}
