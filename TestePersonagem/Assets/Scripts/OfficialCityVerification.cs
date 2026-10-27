using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Runs only with the explicit QA flag. Native session storage and the VPS are never used.
public sealed class OfficialCityVerification : MonoBehaviour
{
    const string Flag = "--verify-official-city";
    const string Folder = @"C:\SistemaAdr\gta\tools\gta-map-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> localRequests = new List<string>();
    readonly List<CaptureResult> captures = new List<CaptureResult>();
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> originalTransport;
    Func<string> originalRead;
    Action<string> originalWrite;
    Action originalClear;
    AccountSession account;
    ThirdPersonPlayer player;
    CharacterController controller;
    CityDayNight lighting;
    CitySectorStreaming streaming;
    Transform originalStreamingTarget;
    GameObject streamingProbe;
    bool probingStreaming, playerWasEnabled, controllerWasEnabled;
    ApiPlayerState state;
    string localToken;
    string originalEmail;
    bool hadEmail, finished, restored, lightingSaved, originalNight;
    int vaultWrites, vaultReads;
    int sectorTotal, sectorsAtSpawn, sectorsAtProbe, sectorsAfterReturn, completedSectorLoads, completedSectorUnloads;
    string probedSector;
    float started;

    [Serializable]
    sealed class CaptureResult
    {
        public string name;
        public double meanLuminance, variance;
    }

    [Serializable]
    sealed class Report
    {
        public bool passed, nativeVaultUntouched;
        public string capturedUtc;
        public int remoteRequests, vaultReads, vaultWrites;
        public int sectorTotal, sectorsAtSpawn, sectorsAtProbe, sectorsAfterReturn, completedSectorLoads, completedSectorUnloads;
        public string probedSector, materialCoverage;
        public List<string> checks, errors, localRequests;
        public List<CaptureResult> captures;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0 &&
            !FindAnyObjectByType<OfficialCityVerification>())
            new GameObject("Official City Offline Verification").AddComponent<OfficialCityVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        started = Time.realtimeSinceStartup;
        Directory.CreateDirectory(Folder);
        hadEmail = PlayerPrefs.HasKey("account.email");
        originalEmail = PlayerPrefs.GetString("account.email", "");
        originalTransport = AccountSession.VerificationTransport;
        originalRead = SessionVault.TestRead; originalWrite = SessionVault.TestWrite; originalClear = SessionVault.TestClear;
        AccountSession.VerificationTransport = FakeRequest;
        // These hooks are consulted only while the fake transport is installed.
        // Starting empty forces the real login form without reading/deleting a native token.
        SessionVault.TestRead = () => { vaultReads++; return localToken; };
        SessionVault.TestWrite = value => { localToken = value; vaultWrites++; };
        SessionVault.TestClear = () => localToken = null;
        Application.logMessageReceived += OnLog;
        Application.quitting += OnQuitting;
        state = new ApiPlayerState { accountId = "official-city-local-account", characterId = "official-city-local-character",
            uid = "990002", firstName = "Cidade", lastName = "Teste", name = "Cidade Teste", gender = "male",
            accountRole = "user", xp = "0", playSeconds = "0", cashCents = "0", bankCents = "0", hzCoins = "0",
            model = "bybeyby", world = "centro_teste", version = "1", level = 1,
            health = 100, energy = 100, hunger = 100, thirst = 100,
            x = 0, y = -.1f, z = 0, yaw = 0, cameraYaw = 0, cameraPitch = 14 };
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
        if (!finished) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        Check(AccountSession.VerificationTransport == (Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator>)FakeRequest,
            "Transporte de QA instalado antes do boot");
        yield return WaitUntil(() => AccountSession.Instance && AccountSession.Instance.AuthVisible, 60, "Boot exibe autenticação real");
        account = AccountSession.Instance;
        var auth = account.Document.rootVisualElement;
        Submit(auth.Q<Button>("auth-login-email"));
        var email = auth.Q<TextField>("auth-email"); var password = auth.Q<TextField>("auth-password");
        if (email == null || password == null) throw new InvalidOperationException("Campos de login ausentes.");
        email.value = "official-city-local@example.invalid"; password.value = "OfficialCityLocal12345";
        Submit(auth.Q<Button>("auth-submit"));
        yield return WaitUntil(() => AccountSession.HasCharacter && auth.Q("character-existing")?.resolvedStyle.display == DisplayStyle.Flex,
            10, "Login local recupera o personagem existente");
        Check(!account.ReadyToEnterCity && !CityEntryLoading.Active, "Entrada exige confirmação do personagem");
        Check(vaultWrites == 1 && localToken == "official-city-local-fixture", "Token da fixture fica somente no cofre em memória");
        Submit(auth.Q<Button>("character-enter"));
        yield return WaitUntil(() => !StartupLoader.IsLoading && !CityEntryLoading.Active && FindAnyObjectByType<ThirdPersonPlayer>(),
            60, "Transição real carrega a cidade oficial");
        player = FindAnyObjectByType<ThirdPersonPlayer>();
        controller = player.GetComponent<CharacterController>();
        player.DemoMode = true; player.DemoInput = Vector2.zero; player.DemoRun = false;
        yield return WaitUntil(() => FindAnyObjectByType<CitySectorStreaming>(), 10, "Controlador de setores aditivos está presente");
        streaming = FindAnyObjectByType<CitySectorStreaming>();
        yield return WaitUntil(() => streaming.Ready && !CitySectorStreaming.IsPreparing && streaming.PendingCount == 0,
            60, "Setores do spawn terminam de carregar antes da verificação da cidade");
        yield return new WaitForSecondsRealtime(.5f);
        var map = NeighborhoodMap.Active;
        var environment = FindAnyObjectByType<GtaCityEnvironment>();
        Check(SceneManager.GetActiveScene().name == "TestePersonagem", "Cena oficial TestePersonagem está ativa");
        Check(map && map.isOfficialCity, "NeighborhoodMap identifica a cidade oficial");
        Check(environment && environment.player == player, "Ambiente da cidade controla o personagem autenticado");
        Check(!FindAnyObjectByType<GtaRecoveredPreview>(), "Prévia separada não está presente na cidade oficial");
        Check(!MultiplayerWorld.Instance, "Multiplayer não inicia na execução com transporte local");
        Check(account.ReadyToEnterCity && AccountSession.HasCharacter && !AccountSession.InputBlocked,
            "Conta e personagem concluem a entrada sem bloquear os controles");
        Check(GameHud.Instance && GameHud.Instance.Root != null &&
            GameHud.Instance.Root.resolvedStyle.display != DisplayStyle.None, "HUD oficial fica visível após a entrada");
        yield return WaitUntil(() => { var portal = FindAnyObjectByType<HousePortal>();
            return portal && portal.ActiveArrow && portal.PromptElement != null; }, 10,
            "Entrada da casa cria o novo marcador e sua ação contextual");
        var housePortal = FindAnyObjectByType<HousePortal>();
        var portalArrow = housePortal.ActiveArrow;
        Check(Vector3.Distance(housePortal.ExteriorPosition, new Vector3(-24.282f, -.649f, 1.255f)) < .001f &&
            Mathf.Abs(Mathf.DeltaAngle(housePortal.ExteriorYaw, 326f)) < .01f,
            "Entrada exterior fica nas coordenadas e rotação solicitadas");
        Check(Vector3.Distance(housePortal.InteriorArrivalPosition, housePortal.InteriorMarkerPosition) < .001f,
            "Entrada na casa posiciona o personagem no mesmo ponto da seta interna");
        Check(Mathf.Abs(Mathf.DeltaAngle(housePortal.InteriorArrivalYaw, 0f)) < .01f,
            "Personagem entra olhando para o interior e fica de costas para a porta");
        Check(portalArrow.name == "Seta 3D giratória de entrada" &&
            portalArrow.GetComponentsInChildren<MeshRenderer>().Length == 2,
            "Portal usa somente a seta 3D suspensa com corpo e ponta");
        Check(housePortal.PromptElement.Q<HudIcon>("house-door-icon")?.Kind == "door" &&
            housePortal.PromptElement.Q<Label>("house-door-label") != null,
            "Proximidade exibe botão circular com ícone de porta e legenda");
        float arrowYaw = portalArrow.eulerAngles.y;
        yield return new WaitForSecondsRealtime(.16f);
        Check(Mathf.Abs(Mathf.DeltaAngle(arrowYaw, portalArrow.eulerAngles.y)) > 5f,
            "Seta 3D gira continuamente para sinalizar a entrada");
        Check(portalArrow.parent.GetComponentInChildren<Light>() == null,
            "Portal antigo, brilho no piso e luz amarela foram removidos");
        Check(Camera.main && Camera.main.GetComponent<ThirdPersonCamera>() && player.cameraTransform == Camera.main.transform,
            "Câmera em terceira pessoa acompanha o personagem atual");
        if (!map || !environment || !controller || !Camera.main) throw new InvalidOperationException("Cidade oficial incompleta.");

        Check(map.Contains(map.SpawnPosition) && map.halfExtent >= 1000 && map.mapCameraHeight >= 150,
            "Spawn, extensão e altura do mapa foram ampliados para a cidade");
        Check(map.Contains(new Vector3(1000, 0, 1000)) && map.Contains(new Vector3(-1000, 0, -1000)) &&
            !map.Contains(new Vector3(1000.01f, 0, 0)) && !map.Contains(new Vector3(float.NaN, 0, 0)),
            "Limites oficiais aceitam +/-1000 m e rejeitam coordenadas inválidas");
        Check(controller.isGrounded && Ground(player.transform.position, out var initialFloor) &&
            Mathf.Abs(initialFloor.point.y - FeetY(player.transform.position)) < .3f,
            "Save legado inicia apoiado no piso da cidade");
        VerifySaveRecovery(map);
        VerifyMaterials("setores do spawn");
        yield return VerifyStreaming();
        // The probe never teleports the real actor. Restore its sectors before floor/save checks and captures.
        VerifySaveRecovery(map);
        Check(controller.isGrounded && Ground(player.transform.position, out _), "Piso do personagem permanece disponível após a ida e volta do streaming");
        if (GameHud.Instance)
        {
            GameHud.Instance.RefreshMaps();
            bool areaMap = false;
            foreach (var camera in GameHud.Instance.GetComponentsInChildren<Camera>())
                if (camera.orthographic && camera.targetTexture && camera.orthographicSize >= map.MapHalfExtent - .1f &&
                    camera.farClipPlane >= map.mapCameraHeight - map.minY)
                    areaMap = true;
            Check(areaMap, "Mapa do HUD cobre a cidade e seu intervalo vertical");
        }

        lighting = environment.lighting;
        if (!Check(lighting && lighting.sun && lighting.moon, "Ciclo de dia/noite mantém sol e lua configurados"))
            throw new InvalidOperationException("Iluminação oficial ausente.");
        originalNight = lighting.IsNight; lightingSaved = true;
        lighting.SetNight(false); yield return new WaitForSecondsRealtime(.4f);
        Check(!lighting.IsNight && lighting.sun.enabled && !lighting.moon.enabled, "Modo diurno acende o sol e desliga a lua");
        Check(lighting.daySky&&lighting.daySky.shader.name=="Skybox/Panoramic"&&
              lighting.daySky.HasProperty("_MainTex")&&lighting.daySky.GetTexture("_MainTex")&&
              lighting.sun.color.r>lighting.sun.color.b&&RenderSettings.fog&&RenderSettings.fogColor.maxColorComponent>.65f,
            "Cidade oficial usa céu com nuvens, sol quente e neblina de fim de tarde");
        Capture(Camera.main, "official-day");
        lighting.SetNight(true); yield return new WaitForSecondsRealtime(.5f);
        Check(lighting.IsNight && !lighting.sun.enabled && lighting.moon.enabled, "Modo noturno preserva a alternância para a lua");
        bool streetLights = true;
        foreach (var light in lighting.streetLights) if (light) streetLights &= light.enabled;
        Check(streetLights, "Postes configurados acendem durante a noite");
        Capture(Camera.main, "official-night");
        lighting.SetNight(originalNight); lightingSaved = false;
        yield return new WaitForSecondsRealtime(.1f);
        Check(lighting.IsNight == originalNight && Camera.main.enabled && GameHud.Instance.Root.resolvedStyle.display != DisplayStyle.None,
            "Estado de iluminação, câmera e HUD é restaurado após as imagens");
        yield return account.Save();
        Check(localRequests.Contains("PUT /v1/player/checkpoint"), "Checkpoint oficial foi validado somente em memória");
        account.Logout();
        yield return WaitUntil(() => !AccountSession.Authenticated, 10, "Logout da fixture encerra a sessão sem acessar a VPS");
        Check(localRequests.Contains("POST /v1/auth/login") && localRequests.Contains("POST /v1/auth/logout"),
            "Fluxo real de login, entrada e saída foi concluído com rotas locais");
        Check(localToken == null, "Logout remove apenas o token da fixture");
    }

    void VerifySaveRecovery(NeighborhoodMap map)
    {
        bool enabled = controller.enabled;
        try
        {
            controller.enabled = false;
            Vector3 result = map.ResolveSavedPosition(new Vector3(5000, 5000, 5000), controller);
            Check(map.Contains(result) && Vector3.ProjectOnPlane(result - map.SpawnPosition, Vector3.up).magnitude < 9,
                "Save fora da cidade é resolvido perto do spawn seguro");
            Check(Ground(result, out var floor) && floor.normal.y > .65f && Mathf.Abs(floor.point.y - FeetY(result)) < .25f,
                "Recuperação de save inválido encontra piso caminhável");
            Vector3 valid = map.ResolveSavedPosition(result, controller);
            Check(map.Contains(valid) && Vector3.ProjectOnPlane(valid - result, Vector3.up).magnitude < .3f,
                "Posição válida mantém a localização ao ser restaurada novamente");
        }
        finally { controller.enabled = enabled; Physics.SyncTransforms(); }
    }

    IEnumerator VerifyStreaming()
    {
        sectorTotal = streaming.sectors == null ? 0 : streaming.sectors.Length;
        sectorsAtSpawn = streaming.LoadedCount;
        Check(sectorTotal > 1 && sectorsAtSpawn > 0 && sectorsAtSpawn < sectorTotal,
            "Streaming carrega apenas a região inicial: " + sectorsAtSpawn + "/" + sectorTotal + " setores");
        Check(string.IsNullOrEmpty(streaming.LastError), "Carga inicial não registra erro de setor");
        if (sectorTotal <= 1 || sectorsAtSpawn >= sectorTotal) yield break;

        int probeIndex = -1; float farthest = -1;
        for (int i = 0; i < streaming.sectors.Length; i++)
        {
            var sector = streaming.sectors[i];
            if (streaming.IsSectorLoaded(sector.scene)) continue;
            Vector3 offset = sector.bounds.center - player.transform.position;
            float distance = offset.x * offset.x + offset.z * offset.z;
            if (distance > farthest) { farthest = distance; probeIndex = i; }
        }
        if (!Check(probeIndex >= 0, "Há um setor distante fora da região carregada para testar streaming")) yield break;
        var destination = streaming.sectors[probeIndex];
        probedSector = destination.scene;
        int loadsBefore = streaming.CompletedLoads, unloadsBefore = streaming.CompletedUnloads;
        var initiallyLoaded = streaming.GetLoadedSectorNames();
        originalStreamingTarget = streaming.target;
        playerWasEnabled = player.enabled; controllerWasEnabled = controller.enabled;
        player.enabled = false; controller.enabled = false;
        probingStreaming = true;
        streamingProbe = new GameObject("Official QA sector target");
        streamingProbe.transform.position = destination.bounds.center;
        streaming.target = streamingProbe.transform;
        streaming.RefreshNow();
        yield return WaitUntil(() => streaming.Ready && !CitySectorStreaming.IsPreparing && streaming.PendingCount == 0 &&
            streaming.IsSectorLoaded(probedSector), 60, "Setor distante é carregado por alvo de QA sem mover o personagem");
        sectorsAtProbe = streaming.LoadedCount;
        bool unloadedInitial = false;
        foreach (var scene in initiallyLoaded) if (!streaming.IsSectorLoaded(scene)) unloadedInitial = true;
        Check(streaming.CompletedLoads > loadsBefore && streaming.CompletedUnloads > unloadsBefore && unloadedInitial,
            "Troca de região realmente carrega novos setores e descarrega setores anteriores");
        Check(sectorsAtProbe > 0 && sectorsAtProbe < sectorTotal && string.IsNullOrEmpty(streaming.LastError),
            "Região distante permanece parcial e termina sem erro de streaming: " + sectorsAtProbe + "/" + sectorTotal);
        VerifyMaterials("setores da sonda distante");

        streaming.target = originalStreamingTarget ? originalStreamingTarget : player.transform;
        streaming.RefreshNow();
        yield return WaitUntil(() => streaming.Ready && !CitySectorStreaming.IsPreparing && streaming.PendingCount == 0,
            60, "Região do personagem volta a carregar antes de reativar movimento e colisão");
        sectorsAfterReturn = streaming.LoadedCount;
        completedSectorLoads = streaming.CompletedLoads; completedSectorUnloads = streaming.CompletedUnloads;
        CompleteStreamingProbe();
        Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.35f);
        Check(sectorsAfterReturn > 0 && sectorsAfterReturn < sectorTotal && controller.enabled == controllerWasEnabled &&
            player.enabled == playerWasEnabled && string.IsNullOrEmpty(streaming.LastError),
            "Retorno restaura personagem, colisão e carga parcial da cidade");
    }

    void CompleteStreamingProbe()
    {
        if (!probingStreaming) return;
        probingStreaming = false;
        if (streaming)
        {
            streaming.target = originalStreamingTarget ? originalStreamingTarget : player ? player.transform : null;
            streaming.RefreshNow();
        }
        if (streamingProbe) Destroy(streamingProbe);
        streamingProbe = null;
        if (controller) controller.enabled = controllerWasEnabled;
        if (player) player.enabled = playerWasEnabled;
    }

    void VerifyMaterials(string coverage)
    {
        int slots = 0, missing = 0, unsupported = 0;
        foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            foreach (var material in renderer.sharedMaterials)
            {
                slots++;
                if (!material) { missing++; continue; }
                if (!material.shader || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader") unsupported++;
            }
        Check(slots > 0 && missing == 0 && unsupported == 0,
            "Materiais dos " + coverage + " (somente cenas carregadas): slots=" + slots + ", ausentes=" + missing + ", shaders inválidos=" + unsupported);
    }

    float FeetY(Vector3 position) => position.y + controller.center.y - controller.height * .5f;

    bool Ground(Vector3 point, out RaycastHit floor)
    {
        floor = default; float distance = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 3, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
        {
            if (player && (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))) continue;
            if (hit.normal.y < .65f || hit.distance >= distance) continue;
            floor = hit; distance = hit.distance;
        }
        return floor.collider;
    }

    void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1280, 720, 24);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), texture.EncodeToPNG());
            var pixels = texture.GetPixels32(); double sum = 0, square = 0; int count = 0;
            for (int i = 0; i < pixels.Length; i += 29)
            { double value = (.2126 * pixels[i].r + .7152 * pixels[i].g + .0722 * pixels[i].b) / 255; sum += value; square += value * value; count++; }
            double mean = count > 0 ? sum / count : 0, variance = count > 0 ? square / count - mean * mean : 0;
            captures.Add(new CaptureResult { name = name, meanLuminance = mean, variance = variance });
            Check(mean > .002 && variance > .00001, "Imagem renderizada contém cenário: " + name);
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(texture);
        }
    }

    IEnumerator FakeRequest(string method, string path, string body, bool authenticated, Action<ApiReply,string> done)
    {
        localRequests.Add(method + " " + path);
        yield return null;
        if (method == "POST" && path == "/v1/auth/login")
            done(new ApiReply { token = "official-city-local-fixture", state = CloneState(), needsCharacter = false }, null);
        else if (method == "GET" && path == "/v1/player") done(new ApiReply { state = CloneState() }, null);
        else if (method == "PUT" && path == "/v1/player/checkpoint")
        {
            var checkpoint = JsonUtility.FromJson<CheckpointBody>(body);
            Check(authenticated && checkpoint != null, "Checkpoint usa sessão local autenticada");
            if (checkpoint == null) { done(null, "Checkpoint inválido."); yield break; }
            state.x = checkpoint.x; state.y = checkpoint.y; state.z = checkpoint.z; state.yaw = checkpoint.yaw;
            state.cameraYaw = checkpoint.cameraYaw; state.cameraPitch = checkpoint.cameraPitch;
            state.version = (int.Parse(state.version) + 1).ToString();
            done(new ApiReply { state = CloneState() }, null);
        }
        else if (method == "POST" && path == "/v1/auth/logout") done(new ApiReply(), null);
        else { Check(false, "Rota inesperada bloqueada na fixture: " + method + " " + path); done(null, "Rota bloqueada no QA local."); }
    }

    ApiPlayerState CloneState() => JsonUtility.FromJson<ApiPlayerState>(JsonUtility.ToJson(state));

    IEnumerator WaitUntil(Func<bool> condition, float seconds, string description)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!finished && !condition() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!Check(condition(), description)) throw new InvalidOperationException("Tempo esgotado: " + description);
    }

    static void Submit(Button button)
    {
        if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Botão de autenticação indisponível.");
        button.Focus(); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
    }

    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }

    void Update()
    {
        if (!finished && Time.realtimeSinceStartup - started > 240)
        { errors.Add("Limite global de 240 segundos excedido."); Finish(); }
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + trace);
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        CompleteStreamingProbe();
        if (lightingSaved && lighting) { lighting.SetNight(originalNight); lightingSaved = false; }
        if (player) { player.DemoInput = Vector2.zero; player.DemoRun = false; }
        RestorePreference();
        var report = new Report { passed = errors.Count == 0, nativeVaultUntouched = true,
            capturedUtc = DateTime.UtcNow.ToString("o"), remoteRequests = 0, vaultReads = vaultReads, vaultWrites = vaultWrites,
            sectorTotal = sectorTotal, sectorsAtSpawn = sectorsAtSpawn, sectorsAtProbe = sectorsAtProbe,
            sectorsAfterReturn = sectorsAfterReturn, completedSectorLoads = completedSectorLoads, completedSectorUnloads = completedSectorUnloads,
            probedSector = probedSector, materialCoverage = "Somente os setores carregados no spawn e no alvo distante; não equivale à inspeção de todos os setores.",
            checks = checks, errors = errors, localRequests = localRequests, captures = captures };
        File.WriteAllText(Path.Combine(Folder, "official-runtime.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(Folder, "official-runtime.txt"), string.Join("\n", checks) + "\nRemoteRequests=0\nNativeVaultUntouched=True\nErrors=" + errors.Count + "\n" + string.Join("\n", errors));
        // Keep the fake transport until quitting: AccountSession may checkpoint while handling wantsToQuit.
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    void RestorePreference()
    {
        if (hadEmail) PlayerPrefs.SetString("account.email", originalEmail); else PlayerPrefs.DeleteKey("account.email");
        PlayerPrefs.Save();
    }

    void OnQuitting()
    {
        if (restored) return; restored = true;
        RestorePreference();
        AccountSession.VerificationTransport = originalTransport;
        SessionVault.TestRead = originalRead; SessionVault.TestWrite = originalWrite; SessionVault.TestClear = originalClear;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        Application.quitting -= OnQuitting;
    }
}
