using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// Temporary house entrance for testing the enter/exit flow in the official city.
public sealed class HousePortal : MonoBehaviour
{
    const float InteriorY = -60f;
    static readonly Vector3 InteriorCenter = new Vector3(0, InteriorY, 0);
    static readonly Vector3 InteriorMarkerLocal = new Vector3(0, .04f, -3.95f);
    static readonly Vector3 InteriorSpawn = InteriorCenter + InteriorMarkerLocal;
    const float InteriorSpawnYaw = 0f;
    static readonly Vector3 ExteriorMarkerPosition = new Vector3(-24.282f, -.649f, 1.255f);
    const float ExteriorMarkerYaw = 326f;
    const string PrefsPrefix = "house-test.entrance.";

    ThirdPersonPlayer player;
    GameObject exterior, interior, exitMarker;
    Transform exteriorArrow, exitArrow;
    VisualElement prompt;
    VisualElement fadeCover;
    GameObject fadeObject;
    Button action;
    Label actionLabel;
    Vector3 entrancePosition;
    float entranceYaw;
    float nextInteraction;
    bool inside;
    bool transitioning;
    bool previousCombatLock;

    public Transform ActiveArrow => inside ? exitArrow : exteriorArrow;
    public VisualElement PromptElement => prompt;
    public Vector3 ExteriorPosition => exterior ? exterior.transform.position : ExteriorMarkerPosition;
    public float ExteriorYaw => exterior ? exterior.transform.eulerAngles.y : ExteriorMarkerYaw;
    public Vector3 InteriorArrivalPosition => InteriorSpawn;
    public Vector3 InteriorMarkerPosition => exitMarker ? exitMarker.transform.position : InteriorSpawn;
    public float InteriorArrivalYaw => InteriorSpawnYaw;
    public float ExteriorArrivalYaw => ExteriorMarkerYaw;

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
        entranceYaw = ExteriorMarkerYaw;
    }

    IEnumerator Start()
    {
        while (player && (StartupLoader.IsLoading || AccountSession.InputBlocked)) yield return null;
        if (!player) yield break;
        inside = IsInside(player.transform.position);
        var streaming = FindAnyObjectByType<CitySectorStreaming>();
        float deadline = Time.realtimeSinceStartup + 8f;
        while (streaming && !streaming.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        BuildExterior();
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
            if (available && actionLabel != null) actionLabel.text = inside ? "E  SAIR" : "E  ENTRAR";
        }
        if (available && Input.GetKeyDown(KeyCode.E)) Interact();
        AnimateArrow(exteriorArrow, 0f);
        AnimateArrow(exitArrow, 1.2f);
    }

    static bool IsInside(Vector3 position) => position.y < -45f &&
        Mathf.Abs(position.x - InteriorCenter.x) < 8f && Mathf.Abs(position.z - InteriorCenter.z) < 8f;

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0;
        return Vector3.Distance(a, b);
    }

    static void AnimateArrow(Transform arrow, float phase)
    {
        if (!arrow || !arrow.gameObject.activeInHierarchy) return;
        arrow.Rotate(Vector3.up, 72f * Time.deltaTime, Space.World);
        float wave = Mathf.Sin(Time.time * 2.8f + phase);
        arrow.localPosition = new Vector3(0, 1.8f + wave * .045f, 0);
        float scale = .34f * (1f + wave * .035f);
        arrow.localScale = Vector3.one * scale;
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
            entranceYaw = ExteriorMarkerYaw;
            PlayerPrefs.SetFloat(PrefsPrefix + "x", entrancePosition.x);
            PlayerPrefs.SetFloat(PrefsPrefix + "y", entrancePosition.y);
            PlayerPrefs.SetFloat(PrefsPrefix + "z", entrancePosition.z);
            PlayerPrefs.SetFloat(PrefsPrefix + "yaw", entranceYaw);
            PlayerPrefs.Save();
            // The exit door is behind the actor; arrive looking into the room.
            player.TeleportTo(InteriorSpawn, InteriorSpawnYaw);
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
        prompt.style.marginLeft = -70;
        prompt.style.bottom = 128;
        prompt.style.width = 140;
        prompt.style.height = 112;
        prompt.style.alignItems = Align.Center;
        prompt.style.justifyContent = Justify.Center;
        prompt.style.backgroundColor = Color.clear;
        action = new Button(Interact) { name = "house-door-action" };
        action.style.width = 72;
        action.style.height = 72;
        action.style.paddingLeft = action.style.paddingRight = 0;
        action.style.paddingTop = action.style.paddingBottom = 0;
        action.style.backgroundColor = new Color(.035f, .045f, .055f, .92f);
        action.style.borderTopWidth = action.style.borderBottomWidth = 2;
        action.style.borderLeftWidth = action.style.borderRightWidth = 2;
        action.style.borderTopColor = action.style.borderBottomColor = new Color(1f, .76f, .12f);
        action.style.borderLeftColor = action.style.borderRightColor = new Color(1f, .76f, .12f);
        action.style.borderTopLeftRadius = action.style.borderTopRightRadius = 36;
        action.style.borderBottomLeftRadius = action.style.borderBottomRightRadius = 36;
        action.style.alignItems = Align.Center;
        action.style.justifyContent = Justify.Center;
        var doorIcon = new HudIcon("door") { name = "house-door-icon" };
        doorIcon.style.width = 42;
        doorIcon.style.height = 42;
        doorIcon.SetInk(new Color(1f, .83f, .3f));
        action.Add(doorIcon);
        prompt.Add(action);
        actionLabel = new Label("E  ENTRAR") { name = "house-door-label" };
        actionLabel.style.marginTop = 5;
        actionLabel.style.paddingLeft = actionLabel.style.paddingRight = 10;
        actionLabel.style.paddingTop = actionLabel.style.paddingBottom = 3;
        actionLabel.style.backgroundColor = new Color(.025f, .035f, .045f, .82f);
        actionLabel.style.color = Color.white;
        actionLabel.style.fontSize = 13;
        actionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        actionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        actionLabel.style.borderTopLeftRadius = actionLabel.style.borderTopRightRadius = 8;
        actionLabel.style.borderBottomLeftRadius = actionLabel.style.borderBottomRightRadius = 8;
        prompt.Add(actionLabel);
        hud.Root.Add(prompt);
        hud.RegisterPointerBlocker(prompt);
    }

    void RefreshVisibility()
    {
        if (exterior) exterior.SetActive(!inside);
        if (exitMarker) exitMarker.SetActive(inside);
        if (prompt != null) prompt.style.display = DisplayStyle.None;
    }

    void BuildExterior()
    {
        exterior = new GameObject("Entrada da casa de teste");
        exterior.transform.SetPositionAndRotation(ExteriorMarkerPosition, Quaternion.Euler(0, ExteriorMarkerYaw, 0));
        BuildArrowMarker(exterior.transform, out exteriorArrow);
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
        exitMarker.transform.localPosition = InteriorMarkerLocal;
        BuildArrowMarker(exitMarker.transform, out exitArrow);
        exitMarker.SetActive(false);
    }

    static void BuildArrowMarker(Transform parent, out Transform arrow)
    {
        arrow = new GameObject("Seta 3D giratória de entrada").transform;
        arrow.SetParent(parent, false);
        arrow.localPosition = new Vector3(0, 1.8f, 0);
        var gold = MaterialColor(new Color(1f, .66f, .04f), true);
        var shaft = Box("Corpo da seta", arrow, new Vector3(0, .24f, 0),
            new Vector3(.24f, .52f, .22f), gold, false);
        shaft.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var head = new GameObject("Ponta da seta", typeof(MeshFilter), typeof(MeshRenderer));
        head.transform.SetParent(arrow, false);
        head.GetComponent<MeshFilter>().sharedMesh = ArrowHeadMesh();
        var renderer = head.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = gold;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Mesh ArrowHeadMesh()
    {
        var vertices = new[]
        {
            new Vector3(-.48f, .04f, -.34f), new Vector3(.48f, .04f, -.34f),
            new Vector3(.48f, .04f, .34f), new Vector3(-.48f, .04f, .34f),
            new Vector3(0, -.55f, 0)
        };
        var triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4
        };
        var mesh = new Mesh { name = "Solid 3D downward arrow head", vertices = vertices,
            triangles = triangles };
        mesh.RecalculateNormals();
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
