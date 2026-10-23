using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Runs only in an explicitly requested standalone verification. Normal play is unchanged.
public sealed class CityRuntimeVerification : MonoBehaviour
{
    const string OutputFolder = @"C:\SistemaAdr\gta\tools\city-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<Measurement> measurements = new List<Measurement>();
    ThirdPersonPlayer player;
    CharacterController controller;
    ThirdPersonCamera orbit;
    GameHud hud;
    GameObject city;
    Vector3 spawn;
    bool finished;

    [Serializable]
    sealed class Measurement
    {
        public string name;
        public float value;
        public string unit;
    }

    [Serializable]
    sealed class Report
    {
        public string capturedUtc;
        public string unityVersion;
        public string graphicsDevice;
        public int screenWidth;
        public int screenHeight;
        public bool passed;
        public List<string> checks;
        public List<string> errors;
        public List<Measurement> measurements;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-city") >= 0)
            new GameObject("City Runtime Verification").AddComponent<CityRuntimeVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(OutputFolder);
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + trace);
    }

    // Flatten our nested coroutines so an exception also writes a report and exits with failure.
    IEnumerator Start()
    {
        while(StartupLoader.IsLoading)yield return null;
        var pending = new Stack<IEnumerator>();
        pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            object instruction = null;
            bool moved = false;
            Exception failure = null;
            try
            {
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null)
            {
                errors.Add(failure.ToString());
                Finish();
                yield break;
            }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        if (!finished) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(1.5f);
        player = FindAnyObjectByType<ThirdPersonPlayer>();
        orbit = FindAnyObjectByType<ThirdPersonCamera>();
        hud = GameHud.Instance;
        city = GameObject.Find("Centro - Rua de teste");
        bool ready = Check(player, "Personagem presente") &
                     Check(orbit, "Câmera de terceira pessoa presente") &
                     Check(hud != null && hud.Root != null, "HUD inicializado") &
                     Check(city, "Rua de teste presente na cena");
        if (!ready) yield break;

        controller = player.GetComponent<CharacterController>();
        if (!Check(controller && controller.enabled, "CharacterController ativo")) yield break;
        spawn = player.transform.position;
        player.DemoMode = true;
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        hud.ClosePanel();
        yield return new WaitForSecondsRealtime(.4f);

        Check(controller.isGrounded, "Personagem repousa sobre a rua após iniciar");
        Check(GroundAt(player.transform.position, out RaycastHit ground), "Piso físico existe sob o ponto inicial");
        if (ground.collider)
        {
            Measure("initialFloorY", ground.point.y, "m");
            Measure("initialCapsuleBottomY", controller.bounds.min.y, "m");
            Check(Mathf.Abs(controller.bounds.min.y - ground.point.y) < .11f,
                "Base da cápsula coincide com o piso (tolerância de 11 cm)");
        }

        // Measure the actual world-space skin, applying the skeleton scale exactly once.
        Bounds skinBounds = new Bounds();
        bool hasSkin = false;
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || !skin.sharedMesh) continue;
            Bounds bounds = CharacterGeometry.GetWorldBounds(skin);
            if (!hasSkin) { skinBounds = bounds; hasSkin = true; }
            else skinBounds.Encapsulate(bounds);
        }
        Check(hasSkin, "Malha animada do personagem disponível");
        if (hasSkin)
        {
            Measure("characterVisibleHeight", skinBounds.size.y, "m");
            Measure("characterCapsuleHeight", controller.bounds.size.y, "m");
            Check(skinBounds.size.y >= 1.77f && skinBounds.size.y <= 1.83f,
                "Altura visual do personagem entre 1,77 e 1,83 m durante a animação parada");
            Check(Mathf.Abs(controller.bounds.size.y - skinBounds.size.y) <= .16f,
                "Cápsula de colisão acompanha a altura visual");
            if (ground.collider)
                Check(Mathf.Abs(skinBounds.min.y - ground.point.y) <= .13f,
                    "Pés visuais ficam junto ao chão (tolerância de 13 cm)");
        }
        Check(player.animator && player.animator.enabled && player.animator.runtimeAnimatorController,
            "Animator e controlador de locomoção preservados");
        ValidateMaterials();

        Teleport(new Vector3(-6, spawn.y + .1f, 0), 90);
        yield return new WaitForSecondsRealtime(.5f);
        Vector3 beforeWalk = player.transform.position;
        float start = Time.time;
        player.DemoInput = Vector2.up;
        yield return new WaitForSecondsRealtime(1.3f);
        float walkingDuration = Time.time - start;
        float walkingDistance = Vector3.ProjectOnPlane(player.transform.position - beforeWalk, Vector3.up).magnitude;
        float walkingSpeed = player.CurrentSpeed;
        player.DemoInput = Vector2.zero;
        Measure("walkDistance", walkingDistance, "m");
        Measure("walkMeasuredSpeed", walkingDistance / Mathf.Max(walkingDuration, .01f), "m/s");
        Check(walkingDistance > 1.5f && Mathf.Abs(walkingDistance / walkingDuration - player.walkSpeed) < .25f,
            "Andar percorre a rua na velocidade configurada");
        Check(Mathf.Abs(walkingSpeed - player.walkSpeed) < .3f && controller.isGrounded,
            "Caminhada mantém velocidade e contato com o asfalto");
        Check(player.animator && player.animator.GetFloat("Speed") > 1,
            "Caminhada alimenta a animação de locomoção");
        yield return Capture("01-andando-na-rua");

        Vector3 beforeRun = player.transform.position;
        start = Time.time;
        player.DemoRun = true;
        player.DemoInput = Vector2.up;
        yield return new WaitForSecondsRealtime(1.3f);
        float runningDuration = Time.time - start;
        float runningDistance = Vector3.ProjectOnPlane(player.transform.position - beforeRun, Vector3.up).magnitude;
        float runningSpeed = player.CurrentSpeed;
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        Measure("runDistance", runningDistance, "m");
        Measure("runMeasuredSpeed", runningDistance / Mathf.Max(runningDuration, .01f), "m/s");
        Check(runningDistance > walkingDistance * 1.8f && Mathf.Abs(runningDistance / runningDuration - player.runSpeed) < .4f,
            "Corrida percorre distância maior e respeita velocidade configurada");
        Check(Mathf.Abs(runningSpeed - player.runSpeed) < .45f && controller.isGrounded,
            "Corrida permanece apoiada na rua");
        yield return Capture("02-correndo-na-rua");
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.CurrentSpeed < .12f && player.animator.GetFloat("Speed") < .2f,
            "Soltar os controles retorna à animação parada");

        bool roadFound = GroundAt(new Vector3(0, .3f, 2.2f), out RaycastHit road);
        bool sidewalkFound = GroundAt(new Vector3(0, .3f, 4.2f), out RaycastHit sidewalk);
        Check(roadFound && sidewalkFound, "Rua e calçada têm superfícies físicas na rota do teste");
        if (roadFound && sidewalkFound)
        {
            float curbHeight = sidewalk.point.y - road.point.y;
            Measure("curbHeight", curbHeight, "m");
            Check(Mathf.Abs(curbHeight - .15f) <= .035f && controller.stepOffset > curbHeight,
                "Meio-fio tem aproximadamente 15 cm e cabe no limite de degrau");
            Teleport(new Vector3(0, road.point.y + .08f, 2.2f), 0);
            yield return new WaitForSecondsRealtime(.4f);
            player.DemoInput = Vector2.up;
            float deadline = Time.unscaledTime + 3f;
            while (player.transform.position.z < 4.2f && Time.unscaledTime < deadline) yield return null;
            player.DemoInput = Vector2.zero;
            yield return new WaitForSecondsRealtime(.4f);
            Measure("curbTestFinalZ", player.transform.position.z, "m");
            Check(player.transform.position.z >= 4.15f && controller.isGrounded &&
                  Mathf.Abs(controller.bounds.min.y - sidewalk.point.y) < .11f,
                "Personagem sobe a calçada caminhando sem salto ou teleporte");
            yield return Capture("03-calcada");
        }

        yield return VerifyBuildingCollision();
        // Exercise the actual below-world recovery path, rather than calling ResetPosition directly.
        Teleport(new Vector3(8, -12, 0), 90);
        yield return new WaitForSecondsRealtime(.8f);
        Measure("respawnDistanceFromInitial", Vector3.ProjectOnPlane(player.transform.position - spawn, Vector3.up).magnitude, "m");
        Check(Vector3.ProjectOnPlane(player.transform.position - spawn, Vector3.up).magnitude < .15f && controller.isGrounded,
            "Cair abaixo do cenário retorna ao ponto inicial seguro e ao piso");

        Teleport(spawn + Vector3.up * .1f, 90);
        yield return new WaitForSecondsRealtime(.7f);
        Check(Camera.main && orbit.target == player.transform && player.cameraTransform == orbit.transform,
            "Câmera principal acompanha e orienta o personagem");
        Vector3 focus = player.transform.position + Vector3.up * 1.25f;
        float cameraDistance = Vector3.Distance(orbit.transform.position, focus);
        Measure("cameraDistanceAtSpawn", cameraDistance, "m");
        Check(cameraDistance > 3.8f && cameraDistance < 4.45f,
            "Câmera tem distância confortável e desobstruída no início");
        Check(hud.Root.worldBound.width >= 900 && hud.Root.Q("minimap-card") != null,
            "HUD e minimapa têm layout calculado na resolução de teste");
        hud.RefreshMaps();
        yield return new WaitForEndOfFrame();
        var minimap = hud.Root.Q("minimap-card")?.Q<Image>(className: "mini-image");
        Check(minimap != null && HasRenderedVariation(minimap.image as RenderTexture),
            "Minimapa contém imagem renderizada da cidade");
        yield return Capture("05-rua-hud");

        orbit.yaw = 35;
        orbit.pitch = 18;
        yield return new WaitForSecondsRealtime(.5f);
        yield return Capture("06-vista-diagonal");
        hud.OpenPanel("map");
        yield return new WaitForSecondsRealtime(.4f);
        hud.RefreshMaps();
        yield return new WaitForEndOfFrame();
        var map = hud.Root.Q(className: "map-area")?.Q<Image>(className: "mini-image");
        Check(hud.CurrentPanel == "map" && map != null && HasRenderedVariation(map.image as RenderTexture),
            "Mapa ampliado abre e mostra a geometria real da área");
        yield return Capture("07-mapa-da-area");
        hud.ClosePanel();
        Teleport(spawn + Vector3.up * .1f, 90);
        yield return new WaitForSecondsRealtime(.4f);
        Check(errors.Count == 0, "Execução sem erros, exceções ou asserts do Unity");
    }

    IEnumerator VerifyBuildingCollision()
    {
        bool found = false;
        RaycastHit wall = default;
        float testX = 0;
        foreach (float x in new[] { 0f, -6f, 6f, -12f, 12f })
        {
            if (!EnvironmentRay(new Vector3(x, 1, 4.2f), Vector3.forward, 16, out RaycastHit candidate)) continue;
            if (!candidate.collider.transform.IsChildOf(city.transform) || candidate.collider.bounds.size.y < 3 || candidate.point.z < 5.5f) continue;
            found = true; testX = x; wall = candidate; break;
        }
        if (!Check(found, "Fachada com colisão encontrada na borda norte da calçada")) yield break;
        Measure("buildingCollisionFaceZ", wall.point.z, "m");
        Measure("buildingCollisionTestX", testX, "m");
        Vector3 startPosition = new Vector3(testX, .15f, 4.2f);
        if (GroundAt(startPosition, out RaycastHit floor)) startPosition.y = floor.point.y + .08f;
        Teleport(startPosition, 0);
        yield return new WaitForSecondsRealtime(.5f);
        player.DemoInput = Vector2.up;
        float duration = Mathf.Clamp((wall.point.z - 4.2f) / player.walkSpeed + 1.1f, 1.5f, 10f);
        yield return new WaitForSecondsRealtime(duration);
        float finalZ = player.transform.position.z;
        float blockedSpeed = player.CurrentSpeed;
        player.DemoInput = Vector2.zero;
        Measure("buildingCollisionFinalZ", finalZ, "m");
        Measure("buildingCollisionBlockedSpeed", blockedSpeed, "m/s");
        Check(finalZ <= wall.point.z + .02f && finalZ >= wall.point.z - controller.radius - .4f && blockedSpeed < .25f,
            "Caminhar contra o prédio para junto da fachada sem atravessá-la");
        Check(controller.isGrounded, "Bloqueio da fachada mantém o personagem sobre a calçada");
        yield return Capture("04-colisao-predio");

        // View from just in front of the wall: the camera must shorten its orbit instead of clipping through it.
        orbit.yaw = 180;
        yield return new WaitForSecondsRealtime(.4f);
        Vector3 focus = player.transform.position + Vector3.up * 1.25f;
        float distance = Vector3.Distance(focus, orbit.transform.position);
        Measure("cameraDistanceNearBuilding", distance, "m");
        Check(distance < 1.25f && orbit.transform.position.z <= wall.point.z + .05f,
            "Câmera aproxima do personagem para evitar atravessar a fachada");
    }

    void Teleport(Vector3 position, float yaw)
    {
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        controller.enabled = true;
        Physics.SyncTransforms();
        orbit.ResetView();
        orbit.yaw = yaw;
        orbit.pitch = 14;
        orbit.distance = 4.2f;
    }

    bool GroundAt(Vector3 position, out RaycastHit hit)
    {
        return EnvironmentRay(position + Vector3.up * .4f, Vector3.down, 3, out hit);
    }

    bool EnvironmentRay(Vector3 origin, Vector3 direction, float length, out RaycastHit nearest)
    {
        nearest = default;
        float closest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(origin, direction, length, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform == player.transform || hit.collider.transform.IsChildOf(player.transform)) continue;
            if (hit.distance < closest) { nearest = hit; closest = hit.distance; }
        }
        return nearest.collider;
    }

    void ValidateMaterials()
    {
        int rendererCount = 0, materialCount = 0, invalidCount = 0, texturedCount = 0;
        var distinctMaterials = new HashSet<Material>();
        foreach (var renderer in city.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            rendererCount++;
            foreach (var material in renderer.sharedMaterials)
            {
                if (!material || !material.shader || !material.shader.isSupported || material.shader.name.Contains("InternalErrorShader")) invalidCount++;
                if (!material || !distinctMaterials.Add(material)) continue;
                materialCount++;
                if (material.mainTexture) texturedCount++;
            }
        }
        Measure("cityRendererCount", rendererCount, "count");
        Measure("cityMaterialCount", materialCount, "count");
        Measure("texturedCityMaterialCount", texturedCount, "count");
        Check(rendererCount > 10 && invalidCount == 0 && texturedCount >= 3,
            "Cidade tem modelos e materiais texturizados sem shader ausente ou incompatível");
    }

    bool HasRenderedVariation(RenderTexture renderTexture)
    {
        if (!renderTexture || !renderTexture.IsCreated()) return false;
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        Color32[] pixels = texture.GetPixels32();
        Destroy(texture);
        float sum = 0, squareSum = 0;
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 31)
        {
            Color32 pixel = pixels[i];
            float brightness = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
            sum += brightness; squareSum += brightness * brightness; count++;
        }
        float variance = squareSum / count - (sum / count) * (sum / count);
        return variance > .0008f;
    }

    IEnumerator Capture(string name)
    {
        string path = Path.Combine(OutputFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.unscaledTime + 3f;
        long previousLength = -1;
        int stableFrames = 0;
        while (stableFrames < 2 && Time.unscaledTime < deadline)
        {
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            stableFrames = length > 2000 && length == previousLength ? stableFrames + 1 : 0;
            previousLength = length;
            yield return null;
        }
        Check(stableFrames >= 2, "Captura gravada: " + name);
    }

    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }

    void Measure(string name, float value, string unit)
    {
        measurements.Add(new Measurement { name = name, value = value, unit = unit });
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (player) { player.DemoInput = Vector2.zero; player.DemoRun = false; player.DemoMode = false; }
        var report = new Report
        {
            capturedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceName, screenWidth = Screen.width, screenHeight = Screen.height,
            passed = errors.Count == 0, checks = checks, errors = errors, measurements = measurements
        };
        File.WriteAllText(Path.Combine(OutputFolder, "validation.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(OutputFolder, "validation.txt"), string.Join("\n", checks) +
            "\nERRORS=" + errors.Count + "\n" + string.Join("\n", errors));
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
