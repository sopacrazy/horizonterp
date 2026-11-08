using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Local animation lab in the real city. The existing controller retains movement,
// ground collision and camera; a separate Generic rig displays the authored FBX.
[DefaultExecutionOrder(-900)]
public sealed class TesteGameplaySession : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public GameObject characterPrefab;
    public bool automated;
    public bool Active { get; private set; }
    public bool Ready { get; private set; }
    public bool Crouched { get; private set; }
    public bool Armed { get; private set; }
    public bool Aiming { get; private set; }
    public Animator VisualAnimator { get; private set; }
    public SkinnedMeshRenderer Skin { get; private set; }
    public Transform Weapon { get; private set; }
    public Transform Muzzle { get; private set; }
    public string CurrentAnimation { get; private set; }
    public int ShotBursts { get; private set; }
    public float JumpGravity => 8 * jumpLift / (.6f * .6f);
    public bool MovementLocked => Active && (action == "ComboSocos" || action == "LevandoSoco" || action == "Abaixando");
    public bool CanJump => Active && !Crouched && action == null;
    public bool TestAim, TestFire;
    public readonly HashSet<string> Played = new HashSet<string>();
    GameObject visual, flash;
    CharacterController capsule;
    ThirdPersonCamera orbit;
    readonly Dictionary<Renderer, bool> hiddenRenderers = new Dictionary<Renderer, bool>();
    readonly Dictionary<Behaviour, bool> suspended = new Dictionary<Behaviour, bool>();
    Vector3 basePosition, originalCenter;
    float originalHeight, originalJumpHeight, originalJumpPreparation, jumpLift, visualScale;
    float actionStart, actionDuration, flashUntil;
    string action;
    bool recovering, wasJumping, fire;
    int lastShot = -1;
    void Awake() { player.AnimationTest = this; }
    IEnumerator Start()
    {
        while (IntegratedMapEntry.Loading || !player.gameObject.activeInHierarchy) yield return null;
        yield return null; // Let the player's Start install its normal visual and HUD.
        capsule = player.GetComponent<CharacterController>();
        orbit = FindAnyObjectByType<ThirdPersonCamera>();
        originalHeight = capsule.height; originalCenter = capsule.center;
        originalJumpHeight = player.jumpHeight; originalJumpPreparation = player.jumpPreparation;
        visual = Instantiate(characterPrefab, player.transform, false);
        visual.name = "Teste FBX - personagem jogavel";
        foreach (var item in visual.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 2;
        VisualAnimator = visual.GetComponent<Animator>();
        VisualAnimator.applyRootMotion = false; VisualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        VisualAnimator.Play("Parado", 0, 0); VisualAnimator.Update(0);
        Skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => !s.name.StartsWith("Pistola"));
        var mesh = new Mesh(); Skin.BakeMesh(mesh);
        var points = mesh.vertices.Select(v => visual.transform.InverseTransformPoint(Skin.transform.TransformPoint(v))).ToArray();
        float bottom = points.Min(v => v.y), top = points.Max(v => v.y);
        Destroy(mesh);
        visualScale = originalHeight / (top - bottom);
        visual.transform.localScale = Vector3.one * visualScale;
        basePosition = Vector3.up * (originalCenter.y - originalHeight * .5f - bottom * visualScale);
        visual.transform.localPosition = basePosition;
        jumpLift = .48f * visualScale;
        Weapon = Bone("Pistola_Colt"); Muzzle = Bone("Pistola_Muzzle");
        flash = GameObject.CreatePrimitive(PrimitiveType.Sphere); flash.name = "Clarao - teste local";
        Destroy(flash.GetComponent<Collider>()); flash.transform.SetParent(Muzzle, false);
        flash.transform.localPosition = new Vector3(0, .02f, 0); flash.transform.localScale = new Vector3(.032f, .075f, .032f);
        flash.GetComponent<Renderer>().sharedMaterial = Weapon.GetComponentsInChildren<Renderer>()[0].sharedMaterial;
        var flashMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1, .7f, .1f) };
        flash.GetComponent<Renderer>().material = flashMaterial;
        flash.SetActive(false);
        Ready = true; SetActive(true);
    }
    Transform Bone(string name) => visual.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    public bool SetActive(bool value)
    {
        if (!Ready || Active == value) return Ready;
        if (player.IsJumping || action == "Pulo") return false;
        if (value)
        {
            if (player.TryGetComponent<RifleWeapon>(out var rifle) && rifle.Equipped) rifle.SetEquipped(false);
            hiddenRenderers.Clear();
            foreach (var renderer in player.animator.GetComponentsInChildren<Renderer>(true))
            { hiddenRenderers[renderer] = renderer.enabled; renderer.enabled = false; }
            suspended.Clear();
            Suspend(player.GetComponent<GangsterPlayerVisual>()); Suspend(player.GetComponent<FighterCombo>());
            Suspend(player.GetComponent<RifleWeapon>()); Suspend(player.GetComponent<AnimationTestWheel>());
            player.animator.GetComponent<CombatAvatarAnimation>()?.Clear();
            player.jumpHeight = jumpLift; player.jumpPreparation = .3f;
        }
        else
        {
            SetCrouched(false);
            foreach (var item in hiddenRenderers) if (item.Key) item.Key.enabled = item.Value;
            foreach (var item in suspended) if (item.Key) item.Key.enabled = item.Value;
            player.jumpHeight = originalJumpHeight; player.jumpPreparation = originalJumpPreparation;
            capsule.height = originalHeight; capsule.center = originalCenter;
            orbit?.EndAim();
        }
        Active = value; visual.SetActive(value);
        action = null; recovering = Crouched = Armed = Aiming = TestAim = TestFire = false;
        CurrentAnimation = null;
        if (value) Play("Parado");
        return true;
    }
    void Suspend(Behaviour item) { if (item) { suspended[item] = item.enabled; item.enabled = false; } }
    bool Blocked => !Ready || StartupLoader.IsLoading || IntegratedMapEntry.Loading || AccountSession.InputBlocked ||
        InGameChat.Editing || (GameHud.Instance && GameHud.Instance.IsPanelOpen) || player.IsCuffed || player.InPoliceCar ||
        player.IsSpectating || player.IsTeleporting || player.Health <= 0 || MissionVehicle.Driver == player || PoliceVehicleController.BlocksPlayer(player);
    void Update()
    {
        if (!automated && Ready && !Blocked && Input.GetKeyDown(KeyCode.F9)) SetActive(!Active);
        if (!Active) return;
        if (Blocked) { fire = Aiming = false; orbit?.EndAim(); return; }
        if (!automated)
        {
            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)) SetCrouched(!Crouched);
            if (Input.GetKeyDown(KeyCode.Q)) SetPistol(!Armed);
            if (Input.GetKeyDown(KeyCode.F) && !Armed) TriggerPunch();
            if (Input.GetKeyDown(KeyCode.G)) TriggerHit();
            if (Input.GetKeyDown(KeyCode.J)) { SetCrouched(false); SetPistol(false); recovering = false; }
        }
        bool pointerBlocked = GameHud.Instance && GameHud.Instance.BlocksPointer(Input.mousePosition);
        fire = Armed && action == null && (automated ? TestFire : !pointerBlocked && (Input.GetMouseButton(0) || Input.GetKey(KeyCode.F)));
        Aiming = Armed && action == null && (fire || (automated ? TestAim : !pointerBlocked && Input.GetMouseButton(1)));
        if (Aiming) orbit?.BeginWeaponAim(); else orbit?.EndAim();
    }
    public bool SetCrouched(bool value)
    {
        if (!Active || Crouched == value) return Active;
        if (player.IsJumping || action != null) return false;
        if (!value)
        {
            Vector3 bottom = player.transform.TransformPoint(originalCenter - Vector3.up * (originalHeight * .5f));
            if (Physics.CheckCapsule(bottom + Vector3.up * capsule.radius,
                bottom + Vector3.up * (originalHeight - capsule.radius), capsule.radius * .95f, 1, QueryTriggerInteraction.Ignore)) return false;
        }
        Crouched = value; recovering = false;
        if (value)
        {
            Armed = false; capsule.height = originalHeight * .62f;
            capsule.center = originalCenter - Vector3.up * (originalHeight - capsule.height) * .5f;
            BeginAction("Abaixando", .6f);
        }
        else { capsule.height = originalHeight; capsule.center = originalCenter; }
        return true;
    }
    public bool SetPistol(bool value)
    {
        if (!Active || action != null || player.IsJumping) return false;
        if (Crouched && !SetCrouched(false)) return false;
        Armed = value; recovering = false; return true;
    }
    public bool TriggerPunch()
    {
        if (!Active || action != null || Crouched || player.IsJumping || !player.IsGrounded) return false;
        Armed = false; recovering = false; BeginAction("ComboSocos", 52f / 30); return true;
    }
    public bool TriggerHit()
    {
        if (!Active || action != null || Crouched || player.IsJumping || !player.IsGrounded) return false;
        Armed = false; recovering = false; BeginAction("LevandoSoco", 1.1f); return true;
    }
    void BeginAction(string state, float duration)
    { action = state; actionStart = Time.time; actionDuration = duration; Play(state); }
    void Play(string state)
    {
        if (CurrentAnimation == state) return;
        CurrentAnimation = state; lastShot = -1;
        VisualAnimator.speed = 1;
        VisualAnimator.CrossFadeInFixedTime(state, .12f, 0, 0);
    }
    void LateUpdate()
    {
        if (!Active) return;
        if (player.IsJumping && !wasJumping)
        { recovering = false; BeginAction("Pulo", 1.4f); actionStart -= Time.deltaTime; }
        wasJumping = player.IsJumping;
        float time = Time.time - actionStart;
        if (action != null && time >= actionDuration && !(action == "Pulo" && player.IsJumping))
        { recovering = action == "LevandoSoco"; action = null; }
        bool moving = player.CurrentSpeed > .1f;
        if (moving) recovering = false;
        string desired = action ?? (Crouched ? moving ? "AndandoAbaixado" : "Abaixado" :
            Armed ? fire ? moving ? "PistolaAtirandoAndando" : "PistolaAtirando" :
                Aiming ? moving ? "PistolaMirandoAndando" : "PistolaMirando" :
                moving ? player.CurrentSpeed > 2.4f ? "PistolaCorrendo" : "PistolaMirandoAndando" : "PistolaParado" :
            recovering ? "RecuperandoSoco" : moving ? player.CurrentSpeed > 2.4f ? "Correndo" : "Andando" : "Parado");
        Play(desired);
        if (action == null && moving)
            VisualAnimator.speed = Mathf.Clamp(player.CurrentSpeed / (Crouched ? .858f : desired == "Correndo" || desired == "PistolaCorrendo" ? 4.5f : 1.65f), .35f, 1.5f);
        else VisualAnimator.speed = 1;
        // The imported jump already has an arc. Remove that visual arc while
        // the capsule supplies the same rise, so the jump is not applied twice.
        float phase = (time - .3f) / .6f;
        float bakedLift = action == "Pulo" && phase > 0 && phase < 1 ? jumpLift * 4 * phase * (1 - phase) : 0;
        visual.transform.localPosition = basePosition - Vector3.up * bakedLift;
        Weapon.gameObject.SetActive(Armed && action == null && !Crouched);
        var state = VisualAnimator.GetCurrentAnimatorStateInfo(0);
        if (!VisualAnimator.IsInTransition(0) && state.IsName(desired)) Played.Add(desired);
        if (fire && Weapon.gameObject.activeSelf && state.IsName(desired) && !VisualAnimator.IsInTransition(0))
        {
            int shot = Mathf.FloorToInt(state.normalizedTime * state.length / .5f);
            if (shot != lastShot) { lastShot = shot; ShotBursts++; flashUntil = Time.time + .045f; }
        }
        flash.SetActive(fire && Weapon.gameObject.activeSelf && Time.time < flashUntil);
    }
    void OnGUI()
    {
        if (!Ready) return;
        GUILayout.BeginArea(new Rect(18, Screen.height - 214, 440, 196), GUI.skin.box);
        GUILayout.Label("TESTE DO PERSONAGEM NO JOGO" + (Active ? "" : " - visual original"));
        GUILayout.Label("WASD: andar | Shift: correr | Ctrl: agachar");
        GUILayout.Label("Espaco: pular | Q: equipar/guardar pistola");
        GUILayout.Label("Mouse direito: mirar | Mouse esquerdo/F: atirar");
        GUILayout.Label("F sem arma: dois socos | G: receber soco | J: parado");
        GUILayout.Label("F9: alternar personagem | C: mudar camera");
        if (Active) GUILayout.Label("Animacao: " + CurrentAnimation);
        GUILayout.Label("Teste local de animacoes");
        GUILayout.EndArea();
    }
    void OnDestroy()
    {
        if (player && player.AnimationTest == this) player.AnimationTest = null;
        if (visual) Destroy(visual);
    }
}
