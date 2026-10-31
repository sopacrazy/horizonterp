using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit local QA/preview. Authentication and checkpoints stay in this process only.
public sealed class NeighborhoodVerification : MonoBehaviour
{
    const string Folder = @"C:\SistemaAdr\gta\tools\neighborhood-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> localRequests = new List<string>();
    readonly List<CaptureMetrics> captureMetrics = new List<CaptureMetrics>();
    readonly Dictionary<string, double> captureLuminance = new Dictionary<string, double>();
    AccountSession account;
    ThirdPersonPlayer player;
    CharacterController controller;
    ThirdPersonCamera orbit;
    GameObject neighborhood;
    ApiPlayerState state;
    bool finished, previewActive, quitPreviewAfterLogout, hadEmail;
    string originalEmail;

    [Serializable]
    sealed class CaptureMetrics
    {
        public string name;
        public double meanLuminance, variance;
    }

    [Serializable]
    sealed class Report
    {
        public bool passed;
        public string capturedUtc;
        public List<string> checks, errors, localRequests;
        public List<CaptureMetrics> captures;
        public int remoteRequests;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-neighborhood") >= 0 &&
            !FindAnyObjectByType<NeighborhoodVerification>())
            new GameObject("Neighborhood Local Verification").AddComponent<NeighborhoodVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject); Application.runInBackground = true;
        Directory.CreateDirectory(Folder);
        hadEmail = PlayerPrefs.HasKey("account.email"); originalEmail = PlayerPrefs.GetString("account.email", "");
        AccountSession.VerificationTransport = FakeRequest;
        Application.logMessageReceived += OnLog; Application.quitting += OnQuitting;
        state = new ApiPlayerState { accountId = "local-neighborhood-account", characterId = "local-neighborhood-character",
            uid = "990001", firstName = "Bairro", lastName = "Teste", name = "Bairro Teste", gender = "male",
            accountRole = "user", xp = "0", playSeconds = "0", cashCents = "0", bankCents = "0", hzCoins = "0",
            model = "bybeyby", world = "centro_teste", version = "1", level = 1, health = 100, energy = 100,
            hunger = 100, thirst = 100, x = 0, y = -.1f, z = 0, yaw = 90, cameraYaw = 90, cameraPitch = 14 };
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace);
    }

    IEnumerator Start()
    {
        var pending = new Stack<IEnumerator>(); pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            object instruction = null; bool moved = false; Exception failure = null;
            try { moved = pending.Peek().MoveNext(); if (moved) instruction = pending.Peek().Current; }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); Finish(); yield break; }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested); else yield return instruction;
        }
        if (!finished && !previewActive) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return WaitUntil(() => AccountSession.Instance && AccountSession.Instance.AuthVisible, 45, "Login após carregamento inicial");
        account = AccountSession.Instance;
        VisualElement auth = account.Document.rootVisualElement;
        Submit(auth.Q<Button>("auth-login-email"));
        auth.Q<TextField>("auth-email").value = "neighborhood-local@example.invalid";
        auth.Q<TextField>("auth-password").value = "LocalNeighborhood12345";
        Submit(auth.Q<Button>("auth-submit"));
        yield return WaitUntil(() => AccountSession.HasCharacter && auth.Q("character-existing")?.resolvedStyle.display == DisplayStyle.Flex,
            8, "Personagem de fixture disponível");
        Check(!account.ReadyToEnterCity && !CityEntryLoading.Active, "Fixture exige confirmação antes de entrar");
        Submit(auth.Q<Button>("character-enter"));
        yield return WaitUntil(() => !StartupLoader.IsLoading && !CityEntryLoading.Active && FindAnyObjectByType<ThirdPersonPlayer>(),
            45, "Bairro pronto após a transição de entrada");
        player = FindAnyObjectByType<ThirdPersonPlayer>(); controller = player.GetComponent<CharacterController>();
        orbit = FindAnyObjectByType<ThirdPersonCamera>();
        neighborhood = GameObject.Find("Bairro Horizonte - Praca Central");
        player.DemoMode = true; player.DemoInput = Vector2.zero; player.DemoRun = false;
        yield return new WaitForSecondsRealtime(.3f);
        Check(neighborhood && FindAnyObjectByType<NeighborhoodMap>(), "Bairro e mapa de navegação presentes");
        Check(!MultiplayerWorld.Instance, "Multiplayer não inicia durante a verificação local");
        Check(Vector3.ProjectOnPlane(player.transform.position, Vector3.up).magnitude < .2f && controller.isGrounded,
            "Personagem inicia no centro, apoiado na superfície resolvida");
        if (!neighborhood || !controller || !orbit) throw new InvalidOperationException("Bairro jogável incompleto.");

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--neighborhood-preview") >= 0)
        {
            player.DemoMode = false; previewActive = true;
            var label = GameHud.Instance?.Root.Q<Label>("multiplayer-status"); if (label != null) label.text = "PRÉVIA LOCAL";
            File.WriteAllText(Path.Combine(Folder, "preview-ready.txt"), "PRÉVIA LOCAL · SEM SALVAMENTO ONLINE\nTransport=memory\nRemoteRequests=0");
            yield break;
        }

        VerifyScaleAndGround();
        VerifyOriginClear();
        yield return VerifyMovement();
        yield return VerifyCollision("banco", FindObstacle("bench"));
        yield return VerifyCollision("prédio", FindObstacle("building"));
        yield return VerifyCollision("limite do bairro", FindObstacle("boundary"));
        Teleport(Vector3.zero, 90);
        yield return new WaitForSecondsRealtime(.3f);
        VerifyMap();
        yield return CaptureNeighborhood();
        Check(localRequests.Count > 0 && AccountSession.VerificationTransport != null && !MultiplayerWorld.Instance,
            "Autenticação, checkpoint e sessão permanecem exclusivamente em memória");
        account.Logout();
        yield return WaitUntil(() => !AccountSession.Authenticated, 8, "Fixture encerrada localmente");
        Check(errors.Count == 0, "Execução sem erros, exceções ou asserts");
    }

    void VerifyScaleAndGround()
    {
        bool found = false; Bounds visible = default;
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || !skin.sharedMesh) continue;
            Bounds current = CharacterGeometry.GetWorldBounds(skin);
            if (!found) { visible = current; found = true; } else visible.Encapsulate(current);
        }
        Check(found && visible.size.y >= 1.77f && visible.size.y <= 1.83f,
            "Altura visual do personagem próxima de 1,80 m: " + visible.size.y.ToString("F3"));
        GameObject foundation = GameObject.Find("Base solida do bairro");
        Collider foundationCollider = foundation ? foundation.GetComponent<Collider>() : null;
        Check(foundationCollider && foundationCollider.enabled && foundationCollider.bounds.size.x >= 119 && foundationCollider.bounds.size.z >= 119,
            "Fundação física contínua cobre o bairro de aproximadamente 120 por 120 metros");
        var failures = new List<string>(); int samples = 0;
        foreach (float street in new[] { -22f, 22f })
            foreach (float along in new[] { -56f, -42f, -22f, -8f, 8f, 22f, 42f, 56f })
            {
                SampleFloor(new Vector3(street, 0, along), failures); samples++;
                SampleFloor(new Vector3(along, 0, street), failures); samples++;
            }
        foreach (Vector3 point in new[] { Vector3.zero, new Vector3(0,0,15), new Vector3(0,0,-15),
            new Vector3(15,0,0), new Vector3(-15,0,0), new Vector3(0,0,17.5f), new Vector3(0,0,-17.5f),
            new Vector3(17.5f,0,0), new Vector3(-17.5f,0,0) }) { SampleFloor(point, failures); samples++; }
        Check(failures.Count == 0, samples + " amostras de ruas, cruzamentos e quatro entradas da praça têm piso caminhável" +
            (failures.Count == 0 ? "" : ": " + string.Join(", ", failures)));
    }

    void SampleFloor(Vector3 position, List<string> failures)
    {
        if (!Ground(position, out RaycastHit hit) || hit.normal.y < .8f || hit.point.y < -.3f || hit.point.y > .3f)
            failures.Add(position.x.ToString("F1") + "/" + position.z.ToString("F1"));
    }

    void VerifyOriginClear()
    {
        Vector3 position = player.transform.position; int obstacles = 0;
        foreach (Collider hit in Physics.OverlapCapsule(position + Vector3.up * .4f, position + Vector3.up * 1.4f, .24f,
            ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player.transform) && hit.transform != player.transform) obstacles++;
        Check(obstacles == 0, "Ponto inicial não sobrepõe fonte, banco, árvore ou edifício");
    }

    IEnumerator VerifyMovement()
    {
        foreach (Vector2 corner in new[] { new Vector2(-22,-22), new Vector2(22,-22), new Vector2(-22,22), new Vector2(22,22) })
        {
            Teleport(new Vector3(corner.x - 5, 0, corner.y), 90);
            yield return new WaitForSecondsRealtime(.25f);
            Vector3 before = player.transform.position;
            player.DemoRun = false; player.DemoInput = Vector2.up;
            yield return new WaitForSecondsRealtime(1f);
            float walk = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
            before = player.transform.position; player.DemoRun = true;
            yield return new WaitForSecondsRealtime(1f);
            float run = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
            // Turn at the intersection only when the straight leg still lies inside its six-metre street.
            player.DemoInput = Vector2.zero; player.DemoRun = false;
            Check(walk > 1.25f && run > walk * 1.8f && controller.isGrounded && player.transform.position.y > -.4f,
                "Andar e correr atravessam cruzamento sem prender ou cair: " + corner);
        }
    }

    Collider FindObstacle(string kind)
    {
        foreach (Collider candidate in neighborhood.GetComponentsInChildren<Collider>())
        {
            if (!candidate.enabled || candidate.isTrigger || candidate == controller) continue;
            Bounds bounds = candidate.bounds;
            if (kind == "building" && bounds.size.y > 4 && bounds.size.x > 4 && bounds.size.z > 4) return candidate;
            if (kind == "boundary" && bounds.size.y > 1 &&
                (bounds.min.x >= 58.5f || bounds.max.x <= -58.5f || bounds.min.z >= 58.5f || bounds.max.z <= -58.5f)) return candidate;
            if (kind == "bench")
                for (Transform current = candidate.transform; current && current != neighborhood.transform; current = current.parent)
                    if (current.name.IndexOf("banco", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        current.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0) return candidate;
        }
        return null;
    }

    IEnumerator VerifyCollision(string description, Collider obstacle)
    {
        if (!Check(obstacle, "Obstáculo físico encontrado: " + description)) yield break;
        Bounds bounds = obstacle.bounds;
        Vector3 outward = Mathf.Abs(bounds.center.x) > Mathf.Abs(bounds.center.z) ?
            new Vector3(bounds.center.x > 0 ? -1 : 1, 0, 0) : new Vector3(0,0,bounds.center.z > 0 ? -1 : 1);
        float extent = outward.x != 0 ? bounds.extents.x : bounds.extents.z;
        Vector3 face = bounds.center + outward * extent;
        Vector3 start = face + outward * 1.2f; start.y = 0;
        Teleport(start, Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg);
        yield return new WaitForSecondsRealtime(.3f);
        player.DemoInput = Vector2.up; player.DemoRun = false;
        yield return new WaitForSecondsRealtime(1.35f);
        float separation = Vector3.Dot(player.transform.position - face, outward);
        float stoppedSpeed = player.CurrentSpeed;
        player.DemoInput = Vector2.zero;
        Check(separation >= controller.radius - .16f && separation <= controller.radius + .45f && stoppedSpeed < .3f && controller.isGrounded,
            "Personagem é bloqueado sem atravessar " + description + " (folga=" + separation.ToString("F2") + ")");
    }

    void Teleport(Vector3 position, float yaw)
    {
        player.DemoInput = Vector2.zero; player.DemoRun = false;
        if (Ground(position, out RaycastHit floor)) position.y = floor.point.y + .08f; else position.y = -.05f;
        controller.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.Euler(0,yaw,0)); controller.enabled = true;
        Physics.SyncTransforms(); orbit.ResetView(); orbit.yaw = yaw; orbit.pitch = 14; orbit.distance = 4.2f;
    }

    bool Ground(Vector3 position, out RaycastHit nearest)
    {
        nearest = default; float distance = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(position.x, 3, position.z), Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
        {
            if (player && (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))) continue;
            if (hit.distance < distance) { distance = hit.distance; nearest = hit; }
        }
        return nearest.collider;
    }

    void VerifyMap()
    {
        Camera area = null;
        foreach (Camera camera in GameHud.Instance.GetComponentsInChildren<Camera>())
            if (camera.orthographic && camera.targetTexture && (!area || camera.orthographicSize > area.orthographicSize)) area = camera;
        bool covered = area && area.orthographicSize >= 63;
        if (area) foreach (float x in new[] { -59f,59f }) foreach (float z in new[] { -59f,59f })
        {
            Vector3 viewport = area.WorldToViewportPoint(new Vector3(x,-.15f,z));
            covered &= viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1 &&
                viewport.z >= area.nearClipPlane && viewport.z <= area.farClipPlane;
        }
        Check(covered, "Câmera do mapa cobre as quatro extremidades do bairro e tem alcance de pelo menos 63 m");
        GameHud.Instance.RefreshMaps();
    }

    IEnumerator CaptureNeighborhood()
    {
        CityDayNight lighting = FindAnyObjectByType<CityDayNight>();
        if (!Check(lighting, "Alternância de dia e noite presente")) yield break;
        bool originalNight = lighting.IsNight;
        Camera main = Camera.main; bool mainEnabled = main.enabled;
        var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
        var displays = new List<StyleEnum<DisplayStyle>>();
        foreach (UIDocument document in documents) { displays.Add(document.rootVisualElement.style.display); document.rootVisualElement.style.display = DisplayStyle.None; }
        var cameraObject = new GameObject("Câmera QA do bairro"); Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox; camera.fieldOfView = 55; camera.nearClipPlane = .1f; camera.farClipPlane = 350;
        camera.cullingMask = main.cullingMask & ~(1 << player.gameObject.layer); main.enabled = false;
        lighting.SetNight(false); yield return new WaitForSecondsRealtime(.4f);
        camera.orthographic = true; camera.orthographicSize = 78;
        camera.transform.position = new Vector3(-83,105,-83); camera.transform.LookAt(new Vector3(0,0,0));
        var overviewLods = neighborhood.GetComponentsInChildren<LODGroup>();
        foreach (LODGroup lod in overviewLods) lod.ForceLOD(0);
        yield return Capture("overview");
        foreach (LODGroup lod in overviewLods) lod.ForceLOD(-1);
        camera.orthographic = false;
        camera.transform.position = new Vector3(-16,2.8f,-22); camera.transform.LookAt(new Vector3(22,3,-22));
        yield return Capture("day");
        int dayLights = EnabledLocalLights();
        lighting.SetNight(true); yield return new WaitForSecondsRealtime(.5f);
        int allLocalLights = 0; bool localLightsWithoutShadows = true;
        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include)) if (light.type != LightType.Directional)
        { allLocalLights++; localLightsWithoutShadows &= light.shadows == LightShadows.None; }
        Check(allLocalLights <= 8 && EnabledLocalLights() > dayLights && EnabledLocalLights() <= 8,
            "Noite acende postes respeitando o orçamento máximo de oito luzes locais");
        Check(localLightsWithoutShadows, "Luzes locais permanecem sem sombras em tempo real");
        yield return Capture("night");
        CompareNightCapture("day", "night", "Rua");
        lighting.SetNight(false); yield return new WaitForSecondsRealtime(.4f);
        camera.transform.position = new Vector3(12,4.2f,-15); camera.transform.LookAt(new Vector3(-2,1.6f,5));
        yield return Capture("plaza");
        lighting.SetNight(true); yield return new WaitForSecondsRealtime(.5f);
        yield return Capture("plaza-night");
        CompareNightCapture("plaza", "plaza-night", "Praça");
        lighting.SetNight(originalNight); main.enabled = mainEnabled;
        for (int i=0;i<documents.Length;i++) if (documents[i]) documents[i].rootVisualElement.style.display = displays[i];
        Destroy(cameraObject);
        yield return null;
        Check(main.enabled == mainEnabled && GameHud.Instance.Root.resolvedStyle.display != DisplayStyle.None,
            "Câmera principal e interface são restauradas após as capturas");
    }

    int EnabledLocalLights()
    {
        int count = 0; foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (light.enabled && light.intensity > 0 && light.type != LightType.Directional) count++;
        return count;
    }

    void CompareNightCapture(string dayName, string nightName, string location)
    {
        bool dayMeasured = captureLuminance.TryGetValue(dayName, out double day);
        bool nightMeasured = captureLuminance.TryGetValue(nightName, out double night);
        double ratio = dayMeasured && nightMeasured && day > 0 ? night / day : 0;
        Check(dayMeasured && nightMeasured && ratio >= .30 && ratio < 1.05,
            location + " noturna mantém visibilidade em relação à mesma câmera diurna: " + ratio.ToString("F3"));
    }

    IEnumerator FakeRequest(string method,string path,string body,bool authenticated,Action<ApiReply,string> done)
    {
        localRequests.Add(method + " " + path);
        yield return new WaitForSecondsRealtime(.05f);
        if (method == "POST" && path == "/v1/auth/login")
            done(new ApiReply { token = "local-neighborhood-fixture", state = CloneState(), needsCharacter = false }, null);
        else if (method == "PUT" && path == "/v1/player/checkpoint")
        {
            CheckpointBody checkpoint = JsonUtility.FromJson<CheckpointBody>(body);
            state.x = checkpoint.x; state.y = checkpoint.y; state.z = checkpoint.z; state.yaw = checkpoint.yaw;
            state.cameraYaw = checkpoint.cameraYaw; state.cameraPitch = checkpoint.cameraPitch;
            state.version = (int.Parse(state.version)+1).ToString(); done(new ApiReply { state = CloneState() }, null);
        }
        else if (method == "POST" && path == "/v1/auth/logout")
        {
            done(new ApiReply(), null); if (previewActive) quitPreviewAfterLogout = true;
        }
        else { Check(false,"Rota inesperada bloqueada localmente: " + method + " " + path); done(null,"Rota não prevista na prévia local."); }
    }

    ApiPlayerState CloneState() { return JsonUtility.FromJson<ApiPlayerState>(JsonUtility.ToJson(state)); }

    void Update()
    {
        if (quitPreviewAfterLogout && !AccountSession.Authenticated) Application.Quit();
    }

    IEnumerator WaitUntil(Func<bool> condition,float seconds,string description)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!Check(condition(),description)) throw new InvalidOperationException("Tempo esgotado: " + description);
    }

    static void Submit(Button button)
    {
        if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Botão de entrada indisponível.");
        button.Focus(); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
    }

    IEnumerator Capture(string name)
    {
        string path = Path.Combine(Folder,name + ".png"); if (File.Exists(path)) File.Delete(path);
        yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 4; long previous = -1; int stable = 0;
        while (stable < 2 && Time.realtimeSinceStartup < deadline)
        { long length = File.Exists(path) ? new FileInfo(path).Length : 0; stable = length > 2000 && length == previous ? stable+1 : 0; previous=length; yield return null; }
        if (!Check(stable >= 2,"Captura gravada: " + name)) yield break;
        var texture = new Texture2D(2,2,TextureFormat.RGB24,false); bool decoded = texture.LoadImage(File.ReadAllBytes(path));
        Color32[] pixels = decoded ? texture.GetPixels32() : Array.Empty<Color32>(); double sum=0,square=0; int count=0;
        for (int i=0;i<pixels.Length;i+=29)
        { double v=(.2126*pixels[i].r+.7152*pixels[i].g+.0722*pixels[i].b)/255; sum+=v;square+=v*v;count++; }
        Destroy(texture); double mean=count>0?sum/count:0, variance=count>0?square/count-mean*mean:0;
        captureLuminance[name] = mean;
        captureMetrics.Add(new CaptureMetrics { name=name,meanLuminance=mean,variance=variance });
        Debug.Log("NEIGHBORHOOD_CAPTURE " + name + " luminance=" + mean.ToString("F4") + " variance=" + variance.ToString("F4"));
        Check(decoded && mean > .002 && variance > .00001,"Captura contém cenário renderizado: " + name);
    }

    void OnGUI()
    {
        if (!previewActive) return;
        GUI.depth = -5000;
        var rect = new Rect(10,10,390,30); GUI.color = new Color(0,0,0,.8f); GUI.DrawTexture(rect,Texture2D.whiteTexture);
        GUI.color = new Color(1,.85f,.3f); GUI.Label(new Rect(20,15,380,25),"PRÉVIA LOCAL · SEM SALVAMENTO ONLINE"); GUI.color = Color.white;
    }

    bool Check(bool passed,string description)
    { checks.Add((passed?"OK: ":"FAIL: ")+description); if (!passed) errors.Add(description); return passed; }
    void RestorePreference()
    { if (hadEmail) PlayerPrefs.SetString("account.email",originalEmail); else PlayerPrefs.DeleteKey("account.email"); PlayerPrefs.Save(); }
    void Finish()
    {
        if (finished) return; finished=true; RestorePreference();
        var report = new Report { passed=errors.Count==0,capturedUtc=DateTime.UtcNow.ToString("o"),checks=checks,errors=errors,localRequests=localRequests,captures=captureMetrics,remoteRequests=0 };
        File.WriteAllText(Path.Combine(Folder,"validation.json"),JsonUtility.ToJson(report,true));
        File.WriteAllText(Path.Combine(Folder,"validation.txt"),string.Join("\n",checks)+"\nERRORS="+errors.Count+"\n"+string.Join("\n",errors));
        Application.Quit(errors.Count==0?0:1);
    }
    void OnQuitting() { RestorePreference(); AccountSession.VerificationTransport=null; }
    void OnDestroy() { Application.logMessageReceived-=OnLog; Application.quitting-=OnQuitting; }
}
