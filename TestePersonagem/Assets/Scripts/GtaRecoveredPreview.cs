using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class GtaRecoveredPreview : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public Transform[] walkCheckpoints = Array.Empty<Transform>();
    public Transform[] floorCheckpoints = Array.Empty<Transform>();
    public Camera[] reviewCameras = Array.Empty<Camera>();
    const string Review = @"C:\SistemaAdr\gta\tools\gta-map-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> captures = new List<string>();
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> localTransport;
    CharacterController controller;
    bool verify, finished;
    int rejectedRequests, renderers, materialSlots, missingMaterials, invalidShaders;
    int floorCheckpointsTested, floorGroundingSamples;
    float walkDistance, runDistance;

    [Serializable]
    sealed class Report
    {
        public bool passed;
        public int rejectedRequests, renderers, materialSlots, missingMaterials, invalidShaders;
        public int floorCheckpointsTested, floorGroundingSamples;
        public float walkDistance, runDistance;
        public List<string> checks, errors, captures;
    }
    void Awake()
    {
        verify = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-map") >= 0;
        Application.runInBackground = true;
        QualitySettings.shadowDistance = 90;
        QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 4;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        var city = GameObject.Find("Mapa GTA recuperado");
        if (city) GtaCityEnvironment.ConfigureSurfaces(city.transform);
        localTransport = RejectRequest;
        AccountSession.VerificationTransport = localTransport;
        if (verify) Application.logMessageReceived += OnLog;
    }
    IEnumerator RejectRequest(string method, string path, string body, bool authenticated, Action<ApiReply,string> done)
    {
        rejectedRequests++;
        errors.Add("Requisicao bloqueada no teste local: " + method + " " + path);
        done(null, "Teste local do mapa, sem acesso a API."); yield break;
    }
    void OnDestroy()
    {
        if (verify) Application.logMessageReceived -= OnLog;
        if (AccountSession.VerificationTransport == localTransport) AccountSession.VerificationTransport = null;
    }

    IEnumerator Start()
    {
        if (!verify) yield break;
        Directory.CreateDirectory(Review);
        // Record exceptions from nested checks instead of leaving a verification player open.
        var pending = new Stack<IEnumerator>(); pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            bool moved = false; object instruction = null; Exception failure = null;
            try { moved = pending.Peek().MoveNext(); if (moved) instruction = pending.Peek().Current; }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); break; }
            if (!moved) pending.Pop();
            else if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        Finish();
    }

    IEnumerator Verify()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        if (!player) player = FindAnyObjectByType<ThirdPersonPlayer>();
        controller = player ? player.GetComponent<CharacterController>() : null;
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance, "Cena sem conta ou multiplayer");
        Check(AccountSession.VerificationTransport == localTransport, "Transporte online bloqueado");
        if (!Check(player && controller && player.cameraTransform && Camera.main, "Personagem, capsula e camera presentes")) yield break;
        player.DemoMode = true; player.DemoInput = Vector2.zero; player.DemoRun = false;
        VerifyMaterials();
        Check(TryFloor(player.transform.position, out _), "Colisao de piso sob o spawn");
        yield return VerifyFloorCheckpoints();
        Vector3 from, to;
        if (TryRoute(out from, out to))
        {
            yield return Travel(from, to, false);
            yield return Travel(to, from, true);
        }
        else Check(false, "Nenhum trecho curto livre para verificar caminhada e corrida");
        player.DemoInput = Vector2.zero; player.DemoRun = false;
        player.ResetPosition();
        yield return new WaitForSecondsRealtime(.4f);
        Check(player.IsGrounded && TryFloor(player.transform.position, out _), "Personagem apoiado apos retorno ao spawn");
        yield return CaptureViews();
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance && rejectedRequests == 0,
            "Verificacao encerrada sem conta, multiplayer ou requisicoes");
    }

    IEnumerator VerifyFloorCheckpoints()
    {
        if (floorCheckpoints == null || floorCheckpoints.Length == 0) yield break;
        // These authored samples cover their own locations, not every street in the city.
        float deadline = Time.realtimeSinceStartup + 4.5f;
        for (int i = 0; i < floorCheckpoints.Length; i++)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                Check(false, "Amostragem de piso excedeu 4,5 s: " + floorCheckpointsTested + "/" + floorCheckpoints.Length);
                break;
            }
            floorCheckpointsTested++;
            if (!Check(floorCheckpoints[i], "Amostra de piso " + i + ": ponto definido")) continue;
            if (!Check(TryFloor(floorCheckpoints[i].position, out var floor), "Amostra de piso " + i + ": colisao caminhavel")) continue;
            if (!Check(ClearCapsule(floor.point), "Amostra de piso " + i + ": espaco livre para a capsula")) continue;
            if (i % 6 != 0) continue;
            controller.enabled = false;
            player.transform.position = floor.point + Vector3.up * (.05f - controller.center.y + controller.height * .5f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.25f);
            floorGroundingSamples++;
            Check(player.IsGrounded && TryFloor(player.transform.position, out var support) &&
                Vector3.ProjectOnPlane(player.transform.position - floor.point, Vector3.up).magnitude < .15f &&
                Mathf.Abs(player.transform.position.y + controller.center.y - controller.height * .5f - support.point.y) < .12f,
                "Amostra de piso " + i + ": controlador apoiado apos teleporte");
        }
        player.ResetPosition();
        yield return new WaitForSecondsRealtime(.25f);
        Check(player.IsGrounded && TryFloor(player.transform.position, out _), "Retorno ao spawn apos amostragem de piso");
    }

    void VerifyMaterials()
    {
        var examples = new List<string>();
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!renderer.enabled) continue;
            renderers++;
            foreach (var material in renderer.sharedMaterials)
            {
                materialSlots++;
                if (!material)
                {
                    missingMaterials++;
                    if (examples.Count < 12) examples.Add(renderer.name + ": material ausente");
                }
                else if (!material.shader || !material.shader.isSupported || material.shader.name.Contains("InternalErrorShader"))
                {
                    invalidShaders++;
                    if (examples.Count < 12) examples.Add(renderer.name + ": shader invalido em " + material.name);
                }
            }
        }
        Check(renderers > 0 && materialSlots > 0, "Cenario renderizavel: " + renderers + " renderers, " + materialSlots + " slots");
        Check(missingMaterials == 0 && invalidShaders == 0,
            "Materiais ausentes=" + missingMaterials + ", shaders invalidos=" + invalidShaders);
        foreach (var example in examples) errors.Add(example);
    }

    bool TryRoute(out Vector3 from, out Vector3 to)
    {
        from = to = player.transform.position;
        if (walkCheckpoints != null && walkCheckpoints.Length >= 2 && walkCheckpoints[0] && walkCheckpoints[1])
        {
            from = walkCheckpoints[0].position; to = walkCheckpoints[1].position;
            return ClearRoute(from, to);
        }
        // Probe several directions before moving; a wall ahead is not a control failure.
        foreach (float radius in new[] { 0f, 2f, 4f })
            for (int anchor = 0; anchor < (radius == 0 ? 1 : 8); anchor++)
            {
                var candidate = player.transform.position + Quaternion.Euler(0, anchor * 45, 0) * Vector3.forward * radius;
                if (!TryFloor(candidate, out var floor)) continue;
                for (int direction = 0; direction < 16; direction++)
                {
                    var end = floor.point + Quaternion.Euler(0, direction * 22.5f, 0) * Vector3.forward * 2.5f;
                    if (!ClearRoute(floor.point, end)) continue;
                    from = floor.point; to = end; return true;
                }
            }
        return false;
    }

    bool ClearRoute(Vector3 from, Vector3 to)
    {
        float distance = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
        if (distance < 1 || distance > 6) return false;
        float lastHeight = 0; int samples = Mathf.CeilToInt(distance / .3f);
        for (int i = 0; i <= samples; i++)
        {
            if (!TryFloor(Vector3.Lerp(from, to, i / (float)samples), out var floor)) return false;
            if (i > 0 && Mathf.Abs(floor.point.y - lastHeight) > .18f) return false;
            if (!ClearCapsule(floor.point)) return false;
            lastHeight = floor.point.y;
        }
        return true;
    }

    bool ClearCapsule(Vector3 foot)
    {
        float radius = controller.radius * .93f;
        var bottom = foot + Vector3.up * (radius + .05f);
        var top = foot + Vector3.up * (controller.height - radius + .05f);
        foreach (var hit in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player.transform)) return false;
        return true;
    }

    bool TryFloor(Vector3 position, out RaycastHit floor)
    {
        var hits = Physics.RaycastAll(position + Vector3.up * 2.5f, Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (!hit.transform.IsChildOf(player.transform) && hit.normal.y > .75f && Mathf.Abs(hit.point.y - position.y) < 1.5f)
            { floor = hit; return true; }
        floor = default; return false;
    }

    IEnumerator Travel(Vector3 from, Vector3 to, bool running)
    {
        string label = running ? "Corrida" : "Caminhada";
        if (!Check(TryFloor(from, out var first) && TryFloor(to, out _), label + ": piso no inicio e no fim")) yield break;
        controller.enabled = false;
        player.transform.position = first.point + Vector3.up * (.05f - controller.center.y + controller.height * .5f);
        controller.enabled = true; Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.25f);
        Vector3 before = player.transform.position;
        player.DemoRun = running;
        float distance = Vector3.ProjectOnPlane(to - before, Vector3.up).magnitude;
        float deadline = Time.realtimeSinceStartup + distance / Mathf.Max(.2f, running ? player.runSpeed : player.walkSpeed) * 2 + 1;
        float maxVerticalError = 0;
        while (Time.realtimeSinceStartup < deadline)
        {
            Vector3 direction = Vector3.ProjectOnPlane(to - player.transform.position, Vector3.up);
            if (direction.magnitude < .22f) break;
            direction.Normalize();
            var forward = Vector3.ProjectOnPlane(player.cameraTransform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(player.cameraTransform.right, Vector3.up).normalized;
            player.DemoInput = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
            if (TryFloor(player.transform.position, out var floor)) maxVerticalError = Mathf.Max(maxVerticalError, Mathf.Abs(player.transform.position.y - floor.point.y));
            else maxVerticalError = 100;
            yield return null;
        }
        player.DemoInput = Vector2.zero; player.DemoRun = false;
        yield return new WaitForSecondsRealtime(.2f);
        float moved = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
        if (running) runDistance = moved; else walkDistance = moved;
        Check(moved > .7f && Vector3.ProjectOnPlane(to - player.transform.position, Vector3.up).magnitude < .4f,
            label + ": percurso concluido, deslocamento=" + moved.ToString("F2", CultureInfo.InvariantCulture) + " m");
        Check(player.IsGrounded && maxVerticalError < .5f, label + ": piso sustenta a capsula sem queda");
    }

    IEnumerator CaptureViews()
    {
        var camera = Camera.main;
        Capture(camera, "gameplay.png");
        var orbit = camera.GetComponent<ThirdPersonCamera>();
        if (orbit) orbit.enabled = false;
        var position = player.transform.position;
        camera.transform.position = position + new Vector3(60, 90, -100);
        camera.transform.LookAt(position + Vector3.up * 4);
        yield return null;
        Capture(camera, "panorama.png");
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 focus = position + Vector3.up * 1.35f;
            Vector3 offset = new Vector3(side * 5, 2.2f, 6);
            if (Physics.SphereCast(focus, .15f, offset.normalized, out var hit, offset.magnitude, 1, QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(.4f, hit.distance - .2f);
            camera.transform.position = focus + offset;
            camera.transform.LookAt(focus);
            yield return null;
            Capture(camera, side < 0 ? "street-left.png" : "street-right.png");
        }
        if (reviewCameras != null)
            for (int i = 0; i < reviewCameras.Length; i++) if (reviewCameras[i]) Capture(reviewCameras[i], "review-" + i + ".png");
    }

    void Capture(Camera camera, string name)
    {
        var texture = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        var oldTarget = camera.targetTexture;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Review, name), pixels.EncodeToPNG());
            captures.Add(name);
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = previous;
            texture.Release(); Destroy(texture); Destroy(pixels);
        }
    }

    bool Check(bool condition, string description)
    {
        checks.Add((condition ? "OK: " : "FAIL: ") + description);
        if (!condition) errors.Add(description);
        return condition;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        if (player) { player.DemoInput = Vector2.zero; player.DemoRun = false; }
        var report = new Report { passed = errors.Count == 0, rejectedRequests = rejectedRequests, renderers = renderers,
            materialSlots = materialSlots, missingMaterials = missingMaterials, invalidShaders = invalidShaders,
            floorCheckpointsTested = floorCheckpointsTested, floorGroundingSamples = floorGroundingSamples,
            walkDistance = walkDistance, runDistance = runDistance, checks = checks, errors = errors, captures = captures };
        File.WriteAllText(Path.Combine(Review, "runtime.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(Review, "runtime.txt"), "Passed=" + report.passed + "\nWalk distance=" + walkDistance.ToString(CultureInfo.InvariantCulture) +
            "\nRun distance=" + runDistance.ToString(CultureInfo.InvariantCulture) + "\nGrounded=" + (player && player.IsGrounded) +
            "\nFloor checkpoints tested=" + floorCheckpointsTested + "\nFloor grounding samples=" + floorGroundingSamples +
            "\nAccount=" + (bool)AccountSession.Instance + "\nMultiplayer=" + (bool)MultiplayerWorld.Instance + "\n" + string.Join("\n", checks));
        Application.Quit(report.passed ? 0 : 1);
    }

    void OnGUI()
    {
        GUI.Box(new Rect(12, 12, 550, 48), "Cidade - teste local\nWASD: andar | Shift: correr | Espaco: pular | R: voltar");
    }
}
