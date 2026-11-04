using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Presentation only: locations and every economy action remain server-owned.
public sealed class CriminalEconomyWorld : MonoBehaviour
{
    sealed class Npc
    {
        public GameObject root;
        public TextMesh label;
        public CriminalEconomyLocation location;
    }

    static CriminalEconomyWorld instance;
    static readonly CriminalEconomyLocation[] NoLocations = Array.Empty<CriminalEconomyLocation>();
    public static IReadOnlyList<CriminalEconomyLocation> Locations => instance ? (IReadOnlyList<CriminalEconomyLocation>)instance.locations : NoLocations;
    readonly List<CriminalEconomyLocation> locations = new List<CriminalEconomyLocation>();
    readonly Dictionary<string, Npc> npcs = new Dictionary<string, Npc>();
    readonly HashSet<string> live = new HashSet<string>();
    readonly List<string> removed = new List<string>();
    CriminalEconomyState state;
    ThirdPersonPlayer player;
    GameHud hud;
    Button prompt;
    CriminalEconomyLocation nearest;
    string sessionKey;
    float nextRefresh, nextInteraction;
    const float SpawnDistance = 160f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!instance) new GameObject("Economia criminal • pontos de atendimento").AddComponent<CriminalEconomyWorld>();
    }

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        CriminalEconomyPilotPanel.StateChanged += ApplyState;
        ApplyState(CriminalEconomyPilotPanel.LastState);
    }

    void ApplyState(CriminalEconomyState value)
    {
        state = value;
        sessionKey = AccountSession.Instance ? AccountSession.Instance.CriminalEconomySessionKey : null;
        locations.Clear();
        ClearVisuals();
        nextRefresh = 0;
    }

    void Update()
    {
        string currentSession = AccountSession.Instance ? AccountSession.Instance.CriminalEconomySessionKey : null;
        if (sessionKey != currentSession)
        {
            ApplyState(null);
            sessionKey = currentSession;
        }
        var owner = GameHud.Instance;
        if (hud != owner || player != (owner ? owner.Player : null))
        {
            ClearVisuals();
            if (prompt != null) prompt.RemoveFromHierarchy();
            prompt = null;
            hud = owner;
            player = hud ? hud.Player : null;
            nextRefresh = 0;
        }
        bool ready = player && hud && hud.Root != null && AccountSession.HasCharacter &&
            !AccountSession.InputBlocked && !StartupLoader.IsLoading;
        if (!ready)
        {
            locations.Clear();
            if (npcs.Count != 0) ClearVisuals();
            if (prompt != null) prompt.style.display = DisplayStyle.None;
            nearest = null;
            return;
        }
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + .4f;
            RefreshLocations();
            RefreshNpcs();
        }
        RefreshInteraction();
    }

    void RefreshLocations()
    {
        locations.Clear();
        if (state?.locations == null) return;
        string world = AccountSession.Instance.State.world;
        foreach (var location in state.locations)
        {
            if (location == null || string.IsNullOrEmpty(location.code) ||
                !string.Equals(location.world, world, StringComparison.OrdinalIgnoreCase) ||
                !float.IsFinite(location.x) || !float.IsFinite(location.y) || !float.IsFinite(location.z) ||
                !float.IsFinite(location.radius) || location.radius <= 0 ||
                (location.kind != "activity" && location.kind != "warehouse" && location.kind != "craft" && location.kind != "shop")) continue;
            if (location.kind != "shop" && !string.IsNullOrEmpty(location.organizationCode) && location.organizationCode != state.organization?.code) continue;
            locations.Add(location);
        }
    }

    void RefreshNpcs()
    {
        live.Clear();
        foreach (var location in locations)
        {
            var position = new Vector3(location.x, location.y, location.z);
            if (FlatDistance(player.transform.position, position) > SpawnDistance || !Ground(position, out var grounded)) continue;
            live.Add(location.code);
            if (!npcs.TryGetValue(location.code, out var npc))
            {
                if (!player.animator || !player.animator.runtimeAnimatorController) continue;
                npc = BuildNpc(location, grounded);
                npcs.Add(location.code, npc);
            }
            npc.location = location;
            npc.root.transform.position = grounded;
        }
        removed.Clear();
        foreach (var entry in npcs) if (!live.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (var code in removed) { Destroy(npcs[code].root); npcs.Remove(code); }
    }

    static bool Ground(Vector3 position, out Vector3 grounded)
    {
        grounded = position;
        // Missing sector collisions postpone spawning instead of leaving an NPC floating.
        float best = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * 3, Vector3.down, 6, 1, QueryTriggerInteraction.Ignore))
        {
            float difference = Mathf.Abs(hit.point.y - position.y);
            if (hit.normal.y < .5f || difference > 3 || difference >= best) continue;
            best = difference;
            grounded.y = hit.point.y;
        }
        return !float.IsPositiveInfinity(best);
    }

    Npc BuildNpc(CriminalEconomyLocation location, Vector3 position)
    {
        var root = new GameObject("Atendente • " + location.code);
        root.layer = 2;
        root.transform.SetParent(transform, false);
        root.transform.position = position;
        var visual = GangsterPlayerVisual.CreateRemoteVisual("male", player.animator.runtimeAnimatorController, root.transform);
        visual.name = "Visual do atendente";
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var animator = visual.GetComponent<Animator>();
        if (animator)
        {
            animator.applyRootMotion = false;
            animator.SetFloat("Speed", 0);
            animator.SetBool("Jumping", false);
            if (animator.layerCount > 1) animator.SetLayerWeight(1, 0);
        }
        var shape = root.AddComponent<CapsuleCollider>();
        shape.center = Vector3.up * .9f; shape.height = 1.8f; shape.radius = .3f;
        var caption = new GameObject("Identificação do ponto", typeof(TextMesh));
        caption.layer = 2;
        caption.transform.SetParent(root.transform, false);
        caption.transform.localPosition = Vector3.up * 2.15f;
        var text = caption.GetComponent<TextMesh>();
        text.text = Caption(location);
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 36;
        text.characterSize = .027f;
        text.color = LocationColor(location.kind);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        caption.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        return new Npc { root = root, label = text, location = location };
    }

    void RefreshInteraction()
    {
        if (prompt == null)
        {
            prompt = new Button(Interact) { name = "criminal-economy-interact" };
            prompt.style.position = Position.Absolute;
            prompt.style.left = Length.Percent(50); prompt.style.marginLeft = -165;
            prompt.style.bottom = 158; prompt.style.width = 330; prompt.style.minHeight = 52;
            prompt.style.whiteSpace = WhiteSpace.Normal;
            prompt.style.color = Color.white;
            prompt.style.backgroundColor = new Color(.12f, .08f, .19f, .96f);
            prompt.style.fontSize = 14;
            prompt.style.unityFontStyleAndWeight = FontStyle.Bold;
            prompt.style.unityTextAlign = TextAnchor.MiddleCenter;
            prompt.style.borderTopLeftRadius = prompt.style.borderTopRightRadius = 12;
            prompt.style.borderBottomLeftRadius = prompt.style.borderBottomRightRadius = 12;
            prompt.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            prompt.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            hud.Root.Add(prompt);
            hud.RegisterPointerBlocker(prompt);
        }
        nearest = null;
        float best = float.PositiveInfinity;
        foreach (var npc in npcs.Values)
        {
            float distance = FlatDistance(player.transform.position, npc.root.transform.position);
            if (distance > npc.location.radius || Mathf.Abs(player.transform.position.y - npc.root.transform.position.y) > 3 || distance >= best) continue;
            nearest = npc.location;
            best = distance;
        }
        bool available = nearest != null && !hud.IsPanelOpen && !InGameChat.Editing && !player.IsTeleporting &&
            !player.IsCuffed && !player.InPoliceCar && !player.CombatLocked && Time.unscaledTime >= nextInteraction;
        prompt.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
        if (!available) return;
        prompt.text = "E  ·  " + Caption(nearest) + "\nTOQUE PARA INTERAGIR";
        if (Input.GetKeyDown(KeyCode.E)) Interact();
    }

    void Interact()
    {
        if (nearest == null || !player || !hud || hud.IsPanelOpen || AccountSession.InputBlocked || StartupLoader.IsLoading ||
            InGameChat.Editing || player.IsTeleporting || player.IsCuffed || player.InPoliceCar || player.CombatLocked ||
            Time.unscaledTime < nextInteraction) return;
        if (!npcs.TryGetValue(nearest.code, out var npc) ||
            FlatDistance(player.transform.position, npc.root.transform.position) > nearest.radius ||
            Mathf.Abs(player.transform.position.y - npc.root.transform.position.y) > 3) return;
        nextInteraction = Time.unscaledTime + .4f;
        CriminalEconomyPilotPanel.OpenSection(nearest.kind, nearest.activityCode);
        if (prompt != null) prompt.style.display = DisplayStyle.None;
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        foreach (var npc in npcs.Values)
        {
            if (player && FlatDistance(player.transform.position, npc.root.transform.position) <= 12)
            {
                var direction = player.transform.position - npc.root.transform.position;
                direction.y = 0;
                if (direction.sqrMagnitude > .01f)
                    npc.root.transform.rotation = Quaternion.Slerp(npc.root.transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5);
            }
            if (camera) npc.label.transform.rotation = camera.transform.rotation;
        }
    }

    public static string Caption(CriminalEconomyLocation location) => string.IsNullOrEmpty(location.label) ?
        (location.kind == "warehouse" ? "Depósito da organização" : location.kind == "craft" ? "Bancada de fabricação" : "Atividade criminal") : location.label;
    public static Color LocationColor(string kind) => kind == "warehouse" ? new Color(.56f, .75f, 1f) :
        kind == "craft" ? new Color(1f, .72f, .28f) : kind == "shop" ? new Color(.3f, 1f, .55f) : new Color(.78f, .54f, 1f);
    static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    void ClearVisuals()
    {
        foreach (var npc in npcs.Values) if (npc.root) Destroy(npc.root);
        npcs.Clear();
        nearest = null;
        if (prompt != null) prompt.style.display = DisplayStyle.None;
    }

    void OnDestroy()
    {
        CriminalEconomyPilotPanel.StateChanged -= ApplyState;
        ClearVisuals();
        if (prompt != null) prompt.RemoveFromHierarchy();
        if (instance == this) instance = null;
    }
}
