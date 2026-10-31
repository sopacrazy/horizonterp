using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Scene-local exploration and bounded QA for the original city example.
// This component is never attached to the official city or its entry scene.
[DefaultExecutionOrder(-80)]
public sealed class CityExamplePreview : MonoBehaviour
{
    [Serializable] public sealed class Destination
    {
        public string name, kind;
        public Vector3 position;
        public float yaw;
    }
    [Serializable] public sealed class Route
    {
        public string name;
        public Vector3[] points;
    }

    public Destination[] destinations = Array.Empty<Destination>();
    public Route[] routes = Array.Empty<Route>();
    public Bounds[] buildingFootprints = Array.Empty<Bounds>();
    public Camera[] reviewCameras = Array.Empty<Camera>();
    public CitySectorStreaming streaming;
    public ThirdPersonPlayer player;
    public Collider collisionObstacle;
    public int expectedSectorCount;
    public string reviewDirectory = "";
    public Texture2D overview;

    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> captures = new List<string>();
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> localTransport;
    CharacterController controller;
    CityDayNight lighting;
    bool verify, finished, capturing, teleportBusy, heldByStreaming, menuOpen;
    int rejectedRequests, destinationPassed, floorSamples, routeSegments;
    float verificationStarted;
    string status = "Preparando os setores próximos...";
    Vector2 scroll;

    [Serializable] sealed class Report
    {
        public bool passed;
        public string capturedUtc;
        public List<string> checks, errors, captures;
        public int destinationPassed, destinationTotal, floorSamples, routeSegments;
        public int sectors, completedLoads, completedUnloads, rejectedRequests, remoteRequests;
        public float elapsedSeconds;
    }

    string ReviewFolder => string.IsNullOrWhiteSpace(reviewDirectory)
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/city-example-review"))
        : Path.GetFullPath(reviewDirectory);

    void Awake()
    {
        verify = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-city-example") >= 0;
        localTransport = RejectRequest;
        AccountSession.VerificationTransport = localTransport;
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
        if (!player) player = FindAnyObjectByType<ThirdPersonPlayer>();
        if (!streaming) streaming = FindAnyObjectByType<CitySectorStreaming>();
        if (player)
        {
            controller = player.GetComponent<CharacterController>();
            player.enabled = false;
            heldByStreaming = true;
        }
    }

    IEnumerator RejectRequest(string method, string path, string body, bool authenticated, Action<ApiReply,string> done)
    {
        rejectedRequests++;
        errors.Add("Solicitação bloqueada na prévia local: " + method + " " + path);
        done(null, "A cidade de exemplo funciona sem conta e sem conexão à API.");
        yield break;
    }

    IEnumerator Start()
    {
        lighting = FindAnyObjectByType<CityDayNight>();
        yield return null;
        ApplyQuality();
        if (!verify) yield break;
        Directory.CreateDirectory(ReviewFolder);
        verificationStarted = Time.realtimeSinceStartup;
        var pending = new Stack<IEnumerator>();
        pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            bool moved = false;
            object instruction = null;
            Exception failure = null;
            try
            {
                if (Time.realtimeSinceStartup - verificationStarted > 240)
                    throw new TimeoutException("A verificação da cidade excedeu quatro minutos.");
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null)
            {
                errors.Add(failure.ToString());
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                Finish();
                yield break;
            }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        Finish();
    }

    void ApplyQuality()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Application.isMobilePlatform ? 30 : 60;
        QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 4;
        QualitySettings.shadowDistance = Application.isMobilePlatform ? 30 : 100;
        QualitySettings.shadowCascades = Application.isMobilePlatform ? 0 : 2;
        QualitySettings.pixelLightCount = Application.isMobilePlatform ? 2 : 4;
        if (Application.isMobilePlatform) QualitySettings.globalTextureMipmapLimit = 1;
    }

    void Update()
    {
        if (!player) return;
        bool preparing = streaming && !streaming.Ready;
        if (preparing)
        {
            if (player.enabled) { player.enabled = false; heldByStreaming = true; }
            if (player.animator) player.animator.SetFloat("Speed", 0);
        }
        else if (heldByStreaming && !verify && !teleportBusy)
        {
            heldByStreaming = false;
            player.enabled = true;
            status = "Explore a cidade ou escolha um destino em F2.";
            // ThirdPersonPlayer.Start applies its shared defaults on first enable.
            StartCoroutine(ApplyQualityAfterPlayerStart());
        }
        if (!verify && Input.GetKeyDown(KeyCode.F2)) menuOpen = !menuOpen;
        if (!verify && !GameHud.Instance && lighting && Input.GetKeyDown(KeyCode.N)) lighting.Toggle();
        if (!verify && !preparing && !teleportBusy && player.transform.position.y < -12)
            StartCoroutine(Visit(new Destination { name = "Praça central", position = player.spawnPosition, yaw = player.spawnYaw }));
    }

    IEnumerator ApplyQualityAfterPlayerStart() { yield return null; ApplyQuality(); }

    IEnumerator WaitReady(float timeout = 20)
    {
        if (!streaming) yield break;
        streaming.RefreshNow();
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!streaming.Ready && Time.realtimeSinceStartup < deadline)
        {
            if (!string.IsNullOrEmpty(streaming.LastError))
                throw new InvalidOperationException(streaming.LastError);
            yield return null;
        }
        if (!streaming.Ready) throw new TimeoutException("Os setores não ficaram prontos em " + timeout + " s.");
        Physics.SyncTransforms();
    }

    void Place(Vector3 position, float yaw)
    {
        player.enabled = false;
        controller.enabled = false;
        player.DemoInput = Vector2.zero;
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        if (streaming) { streaming.target = player.transform; streaming.RefreshNow(); }
        Physics.SyncTransforms();
    }

    IEnumerator Visit(Destination destination)
    {
        if (teleportBusy || verify) yield break;
        teleportBusy = true;
        menuOpen = false;
        status = "Carregando " + destination.name + "...";
        Place(destination.position, destination.yaw);
        float deadline = Time.realtimeSinceStartup + 25;
        while (streaming && !streaming.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        if (streaming && !streaming.Ready)
        {
            status = "Falha ao preparar o destino. Voltando ao início.";
            Place(player.spawnPosition, player.spawnYaw);
            deadline = Time.realtimeSinceStartup + 20;
            while (streaming && !streaming.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        }
        if (TryFloor(player.transform.position, out var floor))
            player.transform.position = floor.point + Vector3.up * (.05f - controller.center.y + controller.height * .5f);
        controller.enabled = true;
        player.enabled = !(streaming && !streaming.Ready);
        heldByStreaming = !player.enabled;
        var orbit = player.cameraTransform ? player.cameraTransform.GetComponent<ThirdPersonCamera>() : null;
        if (orbit) orbit.ResetView();
        teleportBusy = false;
        status = destination.name;
        ApplyQuality();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
        if (!Check(player && controller && player.cameraTransform, "Personagem, cápsula e câmera configurados")) yield break;
        player.DemoMode = true;
        player.DemoInput = Vector2.zero;
        yield return WaitReady();
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance, "Prévia independente sem conta ou multiplayer");
        Check(AccountSession.VerificationTransport == localTransport, "Transporte externo bloqueado na cena de exemplo");
        Check(NeighborhoodMap.Active && !NeighborhoodMap.Active.isOfficialCity, "Mapa de teste identificado como separado do oficial");
        Check(streaming && streaming.sectors.Length == expectedSectorCount && expectedSectorCount > 1,
            "Metadados de streaming: " + expectedSectorCount + " setores");
        Check(streaming && streaming.LoadedCount > 0 && streaming.LoadedCount < expectedSectorCount,
            "Somente setores próximos carregados na entrada: " + (streaming ? streaming.LoadedCount : 0));
        Check(destinations != null && destinations.Length >= 8, "Destinos configurados para os vários distritos");
        Check(routes != null && routes.Length > 0, "Percursos conectados configurados");
        Check(buildingFootprints != null && buildingFootprints.Length > 0, "Implantação das construções registrada");

        foreach (var destination in destinations)
        {
            if (destination == null) { Check(false, "Destino ausente"); continue; }
            Place(destination.position, destination.yaw);
            yield return WaitReady();
            bool valid = Sample(destination.position, destination.name, true);
            if (valid) destinationPassed++;
            Check(valid, "Acesso ao destino: " + destination.name);
        }

        // Finish the normal-distance queue while still at the last distant destination.
        // Ready covers loading; PendingCount also covers the trailing unload queue.
        if (streaming)
        {
            float settleDeadline = Time.realtimeSinceStartup + 20;
            do
            {
                streaming.RefreshNow();
                if (streaming.PendingCount == 0) break;
                yield return null;
            }
            while (Time.realtimeSinceStartup < settleDeadline);
            Check(streaming.CompletedUnloads > 0,
                "Destinos distantes descarregam setores anteriores: " + streaming.CompletedUnloads);
        }

        if (destinations.Length > 0)
        {
            Place(destinations[0].position, destinations[0].yaw);
            yield return WaitReady();
            yield return VerifyWalking();
        }
        if (collisionObstacle) yield return VerifyObstacle();

        // Full loading is restricted to route/material validation and captures in QA.
        // Interactive play always restores the distance-based profile afterwards.
        float originalDesktop = streaming ? streaming.desktopLoadDistance : 0;
        float originalMobile = streaming ? streaming.mobileLoadDistance : 0;
        try
        {
            if (streaming)
            {
                Place(new Vector3(0, .08f, 0), 0);
                streaming.desktopLoadDistance = streaming.mobileLoadDistance = 5000;
                yield return WaitReady(60);
            }
            yield return VerifyRoutes();
            VerifyLoadedGeometry();
            capturing = true;
            if (lighting) lighting.SetNight(false);
            yield return new WaitForSecondsRealtime(.2f);
            foreach (var camera in reviewCameras)
                if (camera) Capture(camera, SafeName(camera.name));
                else Check(false, "Câmera de revisão ausente");
            if (lighting && reviewCameras.Length > 2 && reviewCameras[2])
            {
                lighting.SetNight(true);
                yield return new WaitForSecondsRealtime(.2f);
                Check(lighting.IsNight,"Alternância para iluminação noturna");
                Capture(reviewCameras[2],"Centro-noite");
            }
        }
        finally
        {
            capturing = false;
            if (lighting) lighting.SetNight(false);
            if (streaming)
            {
                streaming.desktopLoadDistance = originalDesktop;
                streaming.mobileLoadDistance = originalMobile;
                streaming.RefreshNow();
            }
        }
        Check(rejectedRequests == 0 && !AccountSession.Instance && !MultiplayerWorld.Instance,
            "Nenhuma solicitação à API ou alteração de conta durante a verificação");
        Check(streaming && streaming.CompletedLoads > 0 && streaming.CompletedUnloads > 0,
            "Deslocamento carregou e descarregou setores: " + (streaming ? streaming.CompletedLoads + "/" + streaming.CompletedUnloads : "ausente"));
    }

    IEnumerator VerifyRoutes()
    {
        int routeNumber = 0;
        foreach (var route in routes)
        {
            routeNumber++;
            if (route == null || route.points == null || route.points.Length < 2)
            { Check(false, "Percurso " + routeNumber + " sem pontos suficientes"); continue; }
            int initialErrors = errors.Count;
            for (int segment = 1; segment < route.points.Length; segment++)
            {
                var a = route.points[segment - 1];
                var b = route.points[segment];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4));
                for (int step = 0; step <= steps; step++)
                    Sample(Vector3.Lerp(a, b, step / (float)steps),
                        route.name + ": trecho " + segment + ", amostra " + step, false);
                routeSegments++;
            }
            Check(errors.Count == initialErrors, "Piso e cápsula livres em todo o percurso: " + route.name);
            // Keep long route manifests cooperative without revisiting the streaming queue.
            yield return null;
        }
    }

    bool Sample(Vector3 point, string description, bool detailed)
    {
        floorSamples++;
        bool finite = Finite(point);
        bool floorFound = finite && TryFloor(point, out _);
        bool clear = false;
        if (floorFound && TryFloor(point, out var floor))
        {
            Vector3 center = floor.point + Vector3.up * (.08f + controller.height * .5f);
            float radius = controller.radius * .94f;
            float halfSegment = Mathf.Max(0, controller.height * .5f - radius);
            clear = true;
            foreach (var overlap in Physics.OverlapCapsule(center - Vector3.up * halfSegment,
                center + Vector3.up * halfSegment, radius, 1, QueryTriggerInteraction.Ignore))
                if (!overlap.transform.IsChildOf(player.transform)) { clear = false; break; }
        }
        bool passed = finite && floorFound && clear;
        if (!passed && errors.Count < 160)
            errors.Add(description + " em " + point + ": finito=" + finite + ", piso=" + floorFound + ", passagem=" + clear);
        if (detailed && passed) checks.Add("OK: Piso contínuo e altura livre em " + description);
        return passed;
    }

    bool TryFloor(Vector3 point, out RaycastHit floor)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 3, 1, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (!hit.collider.transform.IsChildOf(player.transform) && hit.normal.y >= .7f && Mathf.Abs(hit.point.y - point.y) <= .45f)
            { floor = hit; return true; }
        floor = default;
        return false;
    }

    IEnumerator VerifyWalking()
    {
        if (!TryFloor(player.transform.position, out var floor)) { Check(false, "Piso inicial para caminhar"); yield break; }
        player.transform.position = floor.point + Vector3.up * (.05f - controller.center.y + controller.height * .5f);
        controller.enabled = true;
        player.enabled = true;
        yield return null;
        ApplyQuality();
        Vector3 before = player.transform.position;
        // A generated destination reserves its forward centreline as its entrance approach.
        Vector3 desired = player.transform.forward;
        Vector3 target = before + desired * 2;
        float deadline = Time.realtimeSinceStartup + 3;
        while (Time.realtimeSinceStartup < deadline)
        {
            Vector3 direction = Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up);
            if (direction.magnitude < .18f) break;
            direction.Normalize();
            Vector3 forward = Vector3.ProjectOnPlane(player.cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(player.cameraTransform.right, Vector3.up).normalized;
            player.DemoInput = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
            yield return null;
        }
        player.DemoInput = Vector2.zero;
        yield return new WaitForSecondsRealtime(.15f);
        float movement = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
        Check(movement > 1.6f && controller.isGrounded && Mathf.Abs(player.transform.position.y - before.y) < .35f,
            "Controlador real percorre dois metros sem queda: " + movement.ToString("F2") + " m");
        player.enabled = false;
        controller.enabled = false;
    }

    IEnumerator VerifyObstacle()
    {
        if (!collisionObstacle || !collisionObstacle.enabled || collisionObstacle.isTrigger)
        { Check(false, "Obstáculo de colisão sólido presente"); yield break; }
        var bounds = collisionObstacle.bounds;
        Vector3 direction = bounds.size.x <= bounds.size.z ? Vector3.right : Vector3.forward;
        float extent = bounds.size.x <= bounds.size.z ? bounds.extents.x : bounds.extents.z;
        Vector3 start = bounds.center - direction * (extent + controller.radius + .7f);
        start.y = .08f;
        Place(start, 0);
        yield return WaitReady();
        if (!TryFloor(start, out var floor)) { Check(false, "Piso na aproximação ao obstáculo"); yield break; }
        player.transform.position = floor.point + Vector3.up * (.05f - controller.center.y + controller.height * .5f);
        controller.enabled = true;
        Vector3 before = player.transform.position;
        bool side = false;
        float attempted = 0;
        float deadline = Time.realtimeSinceStartup + .8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            float step = Mathf.Min(Time.unscaledDeltaTime, .05f) * 3;
            attempted += step;
            side |= (controller.Move(direction * step + Vector3.down * .01f) & CollisionFlags.Sides) != 0;
            yield return null;
        }
        float advanced = Vector3.Dot(player.transform.position - before, direction);
        Check(side && advanced < attempted * .8f, "Colisor sólido impede atravessar o obstáculo");
        controller.enabled = false;
    }

    void VerifyLoadedGeometry()
    {
        int renderers = 0, missing = 0, invalid = 0;
        foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            renderers++;
            if (!Finite(renderer.bounds.center) || !Finite(renderer.bounds.size)) invalid++;
            foreach (var material in renderer.sharedMaterials)
                if (!material || !material.shader || material.shader.name.Contains("InternalError")) missing++;
        }
        Check(renderers > 100, "Geometria da cidade presente: " + renderers + " renderizadores");
        Check(missing == 0 && invalid == 0, "Materiais e limites de geometria válidos: faltas=" + missing + ", inválidos=" + invalid);
    }

    void Capture(Camera camera, string name)
    {
        bool previousFog = RenderSettings.fog;
        float previousLodBias = QualitySettings.lodBias;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        int width = camera.orthographic ? 1600 : 1440;
        int height = camera.orthographic ? 1600 : 900;
        var rt = new RenderTexture(width, height, 24);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            // Overview cameras stand outside the normal gameplay fog range.
            RenderSettings.fog = false;
            QualitySettings.lodBias = 6;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
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
            camera.targetTexture = previousTarget;
            RenderSettings.fog = previousFog;
            QualitySettings.lodBias = previousLodBias;
            RenderTexture.active = previousActive;
            rt.Release();
            Destroy(rt); Destroy(texture);
        }
    }

    static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    static string SafeName(string name)
    {
        foreach (char character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
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
        if (verify && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + trace);
    }
    void Finish()
    {
        if (finished) return;
        finished = true;
        WriteReport();
        Debug.Log("CITY_EXAMPLE_VERIFICATION " + (errors.Count == 0 ? "PASS" : "FAIL") + " checks=" + checks.Count + " samples=" + floorSamples);
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }
    void WriteReport()
    {
        Directory.CreateDirectory(ReviewFolder);
        var report = new Report
        {
            passed = errors.Count == 0, capturedUtc = DateTime.UtcNow.ToString("o"), checks = checks, errors = errors, captures = captures,
            destinationPassed = destinationPassed, destinationTotal = destinations?.Length ?? 0, floorSamples = floorSamples,
            routeSegments = routeSegments, sectors = streaming ? streaming.sectors.Length : 0,
            completedLoads = streaming ? streaming.CompletedLoads : 0, completedUnloads = streaming ? streaming.CompletedUnloads : 0,
            rejectedRequests = rejectedRequests, remoteRequests = 0, elapsedSeconds = Time.realtimeSinceStartup - verificationStarted
        };
        File.WriteAllText(Path.Combine(ReviewFolder, "validation.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(ReviewFolder, "validation.txt"), string.Join("\n", checks) + "\n\n" + string.Join("\n", errors));
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
        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        var textStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        GUI.Box(new Rect(12, 10, width, 95), GUIContent.none);
        GUI.Label(new Rect(24, 18, width - 24, 24), "CIDADE HORIZONTE · EXEMPLO SEPARADO", titleStyle);
        GUI.Label(new Rect(24, 45, width - 24, 25), "WASD mover  ·  Shift correr  ·  Espaço pular  ·  N dia/noite  ·  F2 destinos", textStyle);
        string loading = streaming && !streaming.Ready ? "Carregando os setores próximos..." : status;
        GUI.Label(new Rect(24, 70, width - 24, 28), loading + (streaming ? "  [" + streaming.LoadedCount + "/" + expectedSectorCount + " setores]" : ""), textStyle);
        if (GUI.Button(new Rect(12, 110, 180, 30), menuOpen ? "Fechar destinos [F2]" : "Escolher destino [F2]")) menuOpen = !menuOpen;
        if (!menuOpen || verify) return;
        float height = Mathf.Min(Screen.height - 160, 530);
        GUI.Box(new Rect(12, 145, width, height), GUIContent.none);
        scroll = GUI.BeginScrollView(new Rect(20, 154, width - 16, height - 18), scroll,
            new Rect(0, 0, width - 40, destinations.Length * 40 + (overview ? 255 : 0)));
        for (int i = 0; i < destinations.Length; i++)
        {
            var destination = destinations[i];
            if (destination != null && GUI.Button(new Rect(4, i * 40, width - 56, 34), destination.name) && !teleportBusy)
                StartCoroutine(Visit(destination));
        }
        if (overview) GUI.DrawTexture(new Rect(4, destinations.Length * 40, 250, 250), overview, ScaleMode.ScaleToFit);
        GUI.EndScrollView();
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        if (AccountSession.VerificationTransport == localTransport) AccountSession.VerificationTransport = null;
    }
}
