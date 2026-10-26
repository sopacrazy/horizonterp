using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// Temporary house entrance for testing the enter/exit flow in the official city.
public sealed class HousePortal : MonoBehaviour
{
    const float InteriorY = -60f;
    static readonly Vector3 InteriorCenter = new Vector3(0, InteriorY, 0);
    static readonly Vector3 InteriorSpawn = InteriorCenter + new Vector3(0, .08f, 1.25f);
    const string PrefsPrefix = "house-test.entrance.";

    ThirdPersonPlayer player;
    GameObject exterior, interior, exitMarker;
    Transform exteriorBeam, exitBeam;
    Transform exteriorGlow, exitGlow;
    Light exteriorLight, exitLight;
    VisualElement prompt;
    VisualElement fadeCover;
    GameObject fadeObject;
    Button action;
    Vector3 entrancePosition;
    float entranceYaw;
    float nextInteraction;
    bool inside;
    bool transitioning;
    bool previousCombatLock;

    public static void Create(ThirdPersonPlayer target)
    {
        if (!target || !NeighborhoodMap.Active || !NeighborhoodMap.Active.isOfficialCity ||
            target.GetComponent<HousePortal>()) return;
        var portal = target.gameObject.AddComponent<HousePortal>();
        portal.player = target;
    }

    void Awake()
    {
        player = GetComponent<ThirdPersonPlayer>();
        BuildInterior();
        entrancePosition = new Vector3(PlayerPrefs.GetFloat(PrefsPrefix + "x", 0),
            PlayerPrefs.GetFloat(PrefsPrefix + "y", .08f), PlayerPrefs.GetFloat(PrefsPrefix + "z", 0));
        entranceYaw = PlayerPrefs.GetFloat(PrefsPrefix + "yaw", 0);
    }

    IEnumerator Start()
    {
        while (player && (StartupLoader.IsLoading || AccountSession.InputBlocked)) yield return null;
        if (!player) yield break;
        inside = IsInside(player.transform.position);
        var start = inside ? entrancePosition : player.transform.position;
        var streaming = FindAnyObjectByType<CitySectorStreaming>();
        float deadline = Time.realtimeSinceStartup + 8f;
        while (streaming && !streaming.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        BuildExterior(start, inside ? entranceYaw : player.transform.eulerAngles.y);
        CreatePrompt();
        CreateFade();
        RefreshVisibility();
    }

    void Update()
    {
        if (!player || !exterior) return;
        bool nowInside = IsInside(player.transform.position);
        if (inside != nowInside) { inside = nowInside; RefreshVisibility(); }
        var marker = inside ? exitMarker : exterior;
        bool nearby = marker && FlatDistance(player.transform.position, marker.transform.position) < 1.55f &&
            Mathf.Abs(player.transform.position.y - marker.transform.position.y) < 1.8f;
        bool available = nearby && !transitioning && !player.IsTeleporting && !player.IsJumping &&
            !player.CombatLocked && Time.unscaledTime >= nextInteraction && !StartupLoader.IsLoading &&
            !AccountSession.InputBlocked && !InGameChat.Editing &&
            (!GameHud.Instance || !GameHud.Instance.IsPanelOpen);
        if (prompt != null)
        {
            prompt.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (available) action.text = inside ? "E   SAIR" : "E   ENTRAR";
        }
        if (available && Input.GetKeyDown(KeyCode.E)) Interact();
        float pulse = 1f + Mathf.Sin(Time.time * 2.1f) * .045f;
        if (exteriorBeam) { exteriorBeam.Rotate(Vector3.up, 11f * Time.deltaTime); exteriorBeam.localScale = new Vector3(pulse, 1, pulse); }
        if (exitBeam) { exitBeam.Rotate(Vector3.up, 11f * Time.deltaTime); exitBeam.localScale = new Vector3(pulse, 1, pulse); }
        if (exteriorGlow) exteriorGlow.localScale = new Vector3(pulse, 1, pulse);
        if (exitGlow) exitGlow.localScale = new Vector3(pulse, 1, pulse);
        if (exteriorLight) exteriorLight.intensity = 1.45f + Mathf.Sin(Time.time * 2.1f) * .22f;
        if (exitLight) exitLight.intensity = 1.45f + Mathf.Sin(Time.time * 2.1f) * .22f;
    }

    static bool IsInside(Vector3 position) => position.y < -45f &&
        Mathf.Abs(position.x - InteriorCenter.x) < 8f && Mathf.Abs(position.z - InteriorCenter.z) < 8f;

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0;
        return Vector3.Distance(a, b);
    }

    void Interact()
    {
        if (!player || !exterior || transitioning || Time.unscaledTime < nextInteraction) return;
        StartCoroutine(ChangeRoom());
    }

    IEnumerator ChangeRoom()
    {
        transitioning = true;
        previousCombatLock = player.CombatLocked;
        player.CombatLocked = true;
        nextInteraction = Time.unscaledTime + 1.2f;
        if (prompt != null) prompt.style.display = DisplayStyle.None;
        if (fadeCover != null) fadeCover.style.display = DisplayStyle.Flex;
        yield return Fade(0, 1, .38f);

        bool entering = !inside;
        if (entering)
        {
            entrancePosition = player.transform.position;
            entranceYaw = player.transform.eulerAngles.y;
            PlayerPrefs.SetFloat(PrefsPrefix + "x", entrancePosition.x);
            PlayerPrefs.SetFloat(PrefsPrefix + "y", entrancePosition.y);
            PlayerPrefs.SetFloat(PrefsPrefix + "z", entrancePosition.z);
            PlayerPrefs.SetFloat(PrefsPrefix + "yaw", entranceYaw);
            PlayerPrefs.Save();
            player.TeleportTo(InteriorSpawn, 180f);
        }
        else player.TeleportTo(entrancePosition, entranceYaw);

        while (player && player.IsTeleporting) yield return null;
        if (!player) yield break;
        inside = entering;
        RefreshVisibility();
        var camera = FindAnyObjectByType<ThirdPersonCamera>();
        if (camera) camera.ResetView();
        yield return null; // Render one complete frame at the destination while still covered.
        yield return Fade(1, 0, .52f);
        if (fadeCover != null) fadeCover.style.display = DisplayStyle.None;
        player.CombatLocked = previousCombatLock;
        transitioning = false;
        nextInteraction = Time.unscaledTime + .45f;
        GameHud.Instance?.ShowToast(entering ? "Você entrou na casa de teste" : "Você saiu da casa");
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        if (fadeCover == null) yield break;
        float elapsed = 0;
        fadeCover.style.opacity = from;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeCover.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / seconds)));
            yield return null;
        }
        fadeCover.style.opacity = to;
    }

    void CreateFade()
    {
        fadeObject = new GameObject("Transição suave da casa");
        fadeObject.transform.SetParent(transform, false);
        var document = fadeObject.AddComponent<UIDocument>();
        document.panelSettings = Resources.Load<PanelSettings>("HudPanel");
        document.sortingOrder = 500;
        fadeCover = new VisualElement { name = "house-transition-fade" };
        fadeCover.style.position = Position.Absolute;
        fadeCover.style.left = 0;
        fadeCover.style.right = 0;
        fadeCover.style.top = 0;
        fadeCover.style.bottom = 0;
        fadeCover.style.backgroundColor = Color.black;
        fadeCover.style.opacity = 0;
        fadeCover.style.display = DisplayStyle.None;
        fadeCover.pickingMode = PickingMode.Position;
        document.rootVisualElement.Add(fadeCover);
    }

    void CreatePrompt()
    {
        var hud = GameHud.Instance;
        if (!hud) return;
        prompt = new VisualElement { name = "house-portal-prompt" };
        prompt.style.position = Position.Absolute;
        prompt.style.left = Length.Percent(50);
        prompt.style.marginLeft = -120;
        prompt.style.bottom = 140;
        prompt.style.width = 240;
        prompt.style.height = 82;
        prompt.style.alignItems = Align.Center;
        prompt.style.justifyContent = Justify.Center;
        prompt.style.backgroundColor = new Color(.055f, .075f, .105f, .94f);
        prompt.style.borderTopWidth = prompt.style.borderBottomWidth = 1;
        prompt.style.borderLeftWidth = prompt.style.borderRightWidth = 1;
        prompt.style.borderTopColor = prompt.style.borderBottomColor = new Color(1f, .77f, .15f);
        prompt.style.borderLeftColor = prompt.style.borderRightColor = new Color(1f, .77f, .15f);
        prompt.style.borderTopLeftRadius = prompt.style.borderTopRightRadius = 12;
        prompt.style.borderBottomLeftRadius = prompt.style.borderBottomRightRadius = 12;
        var title = new Label("CASA DE TESTE");
        title.style.color = new Color(1f, .79f, .22f);
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        prompt.Add(title);
        action = new Button(Interact) { text = "E   ENTRAR" };
        action.style.width = 200;
        action.style.height = 38;
        action.style.marginTop = 5;
        action.style.backgroundColor = new Color(1f, .76f, .12f);
        action.style.color = new Color(.08f, .09f, .11f);
        action.style.fontSize = 18;
        action.style.unityFontStyleAndWeight = FontStyle.Bold;
        action.style.unityTextAlign = TextAnchor.MiddleCenter;
        prompt.Add(action);
        hud.Root.Add(prompt);
        hud.RegisterPointerBlocker(prompt);
    }

    void RefreshVisibility()
    {
        if (exterior) exterior.SetActive(!inside);
        if (exitMarker) exitMarker.SetActive(inside);
        if (prompt != null) prompt.style.display = DisplayStyle.None;
    }

    void BuildExterior(Vector3 near, float facingYaw)
    {
        var marker = EntranceThreshold(near, facingYaw);
        exterior = new GameObject("Entrada da casa de teste");
        exterior.transform.position = marker;
        BuildMarker(exterior.transform, out exteriorBeam, out exteriorGlow, out exteriorLight);
    }

    // The player is facing the existing recessed entrance shown at this test spot.
    // Put only the interaction effect at its threshold; never cover the facade with a generated door.
    static Vector3 EntranceThreshold(Vector3 near, float facingYaw)
    {
        var point = near + Quaternion.Euler(0, facingYaw, 0) * Vector3.forward * 1.05f;
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 5f, 1,
            QueryTriggerInteraction.Ignore);
        float nearestHeight = float.MaxValue;
        foreach (var hit in hits)
        {
            float difference = Mathf.Abs(hit.point.y - near.y);
            if (hit.normal.y < .7f || difference > 1.2f || difference >= nearestHeight) continue;
            nearestHeight = difference;
            point.y = hit.point.y + .04f;
        }
        if (nearestHeight == float.MaxValue) point.y = near.y - .04f;
        return point;
    }

    void BuildInterior()
    {
        interior = new GameObject("Casa de teste · interior");
        interior.transform.position = InteriorCenter;
        var t = interior.transform;
        var floor = MaterialColor(new Color(.42f, .34f, .27f));
        var wall = MaterialColor(new Color(.81f, .79f, .7f));
        var trim = MaterialColor(new Color(.23f, .18f, .15f));
        var rug = MaterialColor(new Color(.28f, .43f, .53f));
        Box("Piso", t, new Vector3(0, -.14f, 0), new Vector3(11, .28f, 11), floor, true);
        Box("Parede esquerda", t, new Vector3(-5.5f, 1.6f, 0), new Vector3(.25f, 3.2f, 11), wall, true);
        Box("Parede direita", t, new Vector3(5.5f, 1.6f, 0), new Vector3(.25f, 3.2f, 11), wall, true);
        Box("Parede da frente", t, new Vector3(0, 1.6f, 5.5f), new Vector3(11, 3.2f, .25f), wall, true);
        Box("Parede da saída", t, new Vector3(0, 1.6f, -5.5f), new Vector3(11, 3.2f, .25f), wall, true);
        Box("Teto", t, new Vector3(0, 3.22f, 0), new Vector3(11, .22f, 11), wall, true);
        Box("Rodapé esquerdo", t, new Vector3(-5.3f, .12f, 0), new Vector3(.12f, .24f, 10.6f), trim, false);
        Box("Rodapé direito", t, new Vector3(5.3f, .12f, 0), new Vector3(.12f, .24f, 10.6f), trim, false);
        Box("Tapete", t, new Vector3(0, .018f, 2.3f), new Vector3(4, .035f, 2.5f), rug, false);
        Box("Sofá", t, new Vector3(-3.6f, .42f, 2.4f), new Vector3(1.5f, .7f, 2.8f), rug, true);
        Box("Encosto do sofá", t, new Vector3(-4.3f, .9f, 2.4f), new Vector3(.32f, 1.1f, 2.8f), rug, true);
        Box("Mesa", t, new Vector3(1.7f, .52f, 2.3f), new Vector3(1.8f, .12f, 1.2f), trim, true);
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Box("Pé da mesa", t, new Vector3(1.7f + x * .72f, .25f, 2.3f + z * .42f),
                    new Vector3(.12f, .5f, .12f), trim, false);
        Box("Quadro", t, new Vector3(0, 1.75f, 5.32f), new Vector3(2.8f, 1.3f, .09f), rug, false);
        Box("Janela iluminada", t, new Vector3(5.3f, 1.85f, 1.3f), new Vector3(.08f, 1.35f, 2.1f),
            MaterialColor(new Color(.48f, .7f, .77f), true), false);
        var lightObject = new GameObject("Luz da casa");
        lightObject.transform.SetParent(t, false);
        lightObject.transform.localPosition = new Vector3(0, 2.65f, 0);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, .89f, .72f);
        light.range = 14;
        light.intensity = 3;
        exitMarker = new GameObject("Saída da casa de teste");
        exitMarker.transform.SetParent(t, false);
        exitMarker.transform.localPosition = new Vector3(0, .04f, -3.95f);
        BuildMarker(exitMarker.transform, out exitBeam, out exitGlow, out exitLight);
        exitMarker.SetActive(false);
    }

    static void BuildMarker(Transform parent, out Transform beam, out Transform glow, out Light pointLight)
    {
        var shader = Resources.Load<Shader>("PortalGlow");
        if (!shader) throw new System.InvalidOperationException("Shader PortalGlow não encontrado.");
        beam = new GameObject("Cilindro amarelo translúcido").transform;
        beam.SetParent(parent, false);
        MakeGlowMesh("Cilindro central", beam, CylinderMesh(.72f, 1.28f,
            new[] { .68f, .58f, .42f, .20f, 0f }), shader, new Color(1f, .71f, .045f, .62f));
        MakeGlowMesh("Halo externo", beam, CylinderMesh(1.04f, 1.32f,
            new[] { .35f, .28f, .16f, .065f, 0f }), shader, new Color(1f, .76f, .11f, .45f));
        glow = new GameObject("Brilho amarelo no chão").transform;
        glow.SetParent(parent, false);
        MakeGlowMesh("Luz suave no piso", glow, DiscMesh(1.65f, .035f), shader,
            new Color(1f, .72f, .04f, .55f));
        MakeGlowMesh("Centro luminoso", glow, DiscMesh(.77f, .043f), shader,
            new Color(1f, .86f, .19f, .42f));
        var lamp = new GameObject("Luz amarela da entrada");
        lamp.transform.SetParent(parent, false);
        lamp.transform.localPosition = new Vector3(0, .28f, 0);
        pointLight = lamp.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = new Color(1f, .73f, .16f);
        pointLight.range = 3.2f;
        pointLight.intensity = 1.45f;
        pointLight.shadows = LightShadows.None;
    }

    static void MakeGlowMesh(string name, Transform parent, Mesh mesh, Shader shader, Color color)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(shader) { color = color };
        material.SetColor("_Color", color);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Mesh CylinderMesh(float radius, float height, float[] alpha)
    {
        const int sides = 40;
        int rings = alpha.Length;
        var vertices = new Vector3[(sides + 1) * rings];
        var colors = new Color[vertices.Length];
        var triangles = new int[sides * (rings - 1) * 6];
        for (int row = 0; row < rings; row++)
            for (int side = 0; side <= sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                int index = row * (sides + 1) + side;
                vertices[index] = new Vector3(Mathf.Sin(angle) * radius,
                    height * row / (rings - 1), Mathf.Cos(angle) * radius);
                colors[index] = new Color(1, 1, 1, alpha[row]);
            }
        int t = 0;
        for (int row = 0; row < rings - 1; row++)
            for (int side = 0; side < sides; side++)
            {
                int a = row * (sides + 1) + side;
                int b = a + sides + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
        var mesh = new Mesh { name = "Portal translucent cylinder", vertices = vertices,
            colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh DiscMesh(float radius, float y)
    {
        const int sides = 40;
        float[] radii = { 0f, .22f, .48f, .72f, 1f };
        float[] alpha = { 1f, .93f, .58f, .25f, 0f };
        var vertices = new Vector3[(sides + 1) * radii.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[sides * (radii.Length - 1) * 6];
        for (int row = 0; row < radii.Length; row++)
            for (int side = 0; side <= sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                int index = row * (sides + 1) + side;
                vertices[index] = new Vector3(Mathf.Sin(angle) * radius * radii[row], y,
                    Mathf.Cos(angle) * radius * radii[row]);
                colors[index] = new Color(1, 1, 1, alpha[row]);
            }
        int t = 0;
        for (int row = 0; row < radii.Length - 1; row++)
            for (int side = 0; side < sides; side++)
            {
                int a = row * (sides + 1) + side;
                int b = a + sides + 1;
                triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                triangles[t++] = a + 1; triangles[t++] = b + 1; triangles[t++] = b;
            }
        var mesh = new Mesh { name = "Portal soft ground glow", vertices = vertices,
            colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Destroy(go.GetComponent<Collider>());
        return go;
    }

    static Material MaterialColor(Color color, bool luminous = false)
    {
        var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader) { color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (luminous)
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.7f);
        }
        return material;
    }

    void OnDestroy()
    {
        if (transitioning && player) player.CombatLocked = previousCombatLock;
        if (exterior) Destroy(exterior);
        if (interior) Destroy(interior);
        if (fadeObject) Destroy(fadeObject);
        prompt?.RemoveFromHierarchy();
    }
}
