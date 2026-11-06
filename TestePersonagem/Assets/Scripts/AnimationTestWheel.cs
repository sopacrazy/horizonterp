using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;

public sealed class AnimationTestWheel : MonoBehaviour
{
    internal const string Walk = "ped.WOMAN_walksexy";
    internal const string Run = "ped.WOMAN_runsexy";
    static readonly (string Label, string Clip)[] Choices =
    {
        ("Guarda D", "fight_d.FightD_IDLE"), ("Golpe C M", "fight_c.FightC_M"),
        ("Golpe C 2", "fight_c.FightC_2"), ("Guarda B", "fight_b.FightB_IDLE"),
        ("Chamar táxi", "ped.IDLE_taxi"), ("Andar agachado", "ped.GunCrouchFwd"),
        ("Reação ao gás", "ped.gas_cwr")
    };
    PlayableGraph graph;
    AnimationClipPlayable playingClip;
    RenderTexture previewTexture;
    System.Action onClose;
    static AnimationClip[] clips;
    ThirdPersonPlayer worldPlayer;
    Animator worldAnimator;
    float finishTime;
    Transform leftFoot, rightFoot;
    float footHeight, visualLift;

    internal static AnimationClip FindClip(string name) =>
        (clips ??= Resources.LoadAll<AnimationClip>("AnimationWheel"))
            .FirstOrDefault(clip => clip.humanMotion && clip.name == name);

    internal static void Open(VisualElement parent, Animator animator, string previewGender = null,
        System.Action onClose = null, System.Action<AnimationClip> playInWorld = null)
    {
        if (parent.Q("animation-test-wheel") != null) return;
        var host = new GameObject("Teste de animações");
        var session = host.AddComponent<AnimationTestWheel>();
        session.onClose = onClose;
        var overlay = new VisualElement { name = "animation-test-wheel", focusable = true };
        overlay.style.position = Position.Absolute;
        overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(.02f, .04f, .07f, .48f);
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        var wheel = new VisualElement();
        wheel.generateVisualContent += context =>
        {
            var p = context.painter2D;
            float size = wheel.contentRect.width;
            var center = new Vector2(size / 2, size / 2);
            for (int i = 0; i < Choices.Length; i++)
            {
                float angle = -90 + i * 360f / Choices.Length;
                p.fillColor = new Color(.04f, .09f, .14f, .84f);
                p.BeginPath();
                p.Arc(center, size * .48f, angle - 24, angle + 24);
                p.Arc(center, size * .28f, angle + 24, angle - 24, ArcDirection.CounterClockwise);
                p.ClosePath();
                p.Fill();
            }
        };
        overlay.Add(wheel);
        var status = new Label("Escolha uma animação");
        status.style.color = Color.white;
        status.style.unityTextAlign = TextAnchor.MiddleCenter;
        overlay.Add(status);
        if (previewGender != null)
        {
            try
            {
                animator = session.CreatePreview(previewGender);
                var image = new Image { image = session.previewTexture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = image.style.top = new Length(25, LengthUnit.Percent);
                image.style.width = image.style.height = new Length(50, LengthUnit.Percent);
                wheel.Add(image);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Prévia de animação: " + error.Message);
                status.text = "Modelo de prévia indisponível.";
            }
        }
        for (int i = 0; i < Choices.Length; i++)
        {
            var choice = Choices[i];
            var clip = FindClip(choice.Clip);
            var button = new Button(() =>
            {
                if (playInWorld != null)
                {
                    overlay.RemoveFromHierarchy();
                    playInWorld(clip);
                    return;
                }
                if (!animator || !animator.isActiveAndEnabled) { status.text = "Personagem indisponível."; return; }
                session.Play(animator, clip);
                status.text = choice.Clip;
            }) { text = choice.Label, tooltip = choice.Clip };
            button.SetEnabled(clip);
            float angle = (-90 + i * 360f / Choices.Length) * Mathf.Deg2Rad;
            button.style.position = Position.Absolute;
            button.style.left = new Length(50 + Mathf.Cos(angle) * 37, LengthUnit.Percent);
            button.style.top = new Length(50 + Mathf.Sin(angle) * 37, LengthUnit.Percent);
            button.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            button.style.fontSize = 12;
            button.style.color = Color.white;
            button.style.backgroundColor = new Color(.09f, .17f, .23f, .92f);
            wheel.Add(button);
        }
        var close = new Button(overlay.RemoveFromHierarchy) { text = "FECHAR" };
        overlay.Add(close);
        overlay.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            float size = Mathf.Clamp(Mathf.Min(evt.newRect.width - 20, evt.newRect.height - 62), 180, 470);
            wheel.style.width = wheel.style.height = size;
        });
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            overlay.RemoveFromHierarchy();
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
        overlay.RegisterCallback<DetachFromPanelEvent>(_ => { if (session) Destroy(session.gameObject); });
        parent.Add(overlay);
        close.Focus();
    }

    Animator CreatePreview(string gender)
    {
        transform.position = new Vector3(0, -300, 0);
        var visual = GangsterPlayerVisual.CreateRemoteVisual(gender, null, transform);
        var renderers = visual.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        foreach (var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
        previewTexture = new RenderTexture(384, 384, 16);
        var camera = new GameObject("Câmera da prévia").AddComponent<Camera>();
        camera.transform.SetParent(transform, false);
        camera.transform.position = bounds.center + visual.transform.forward * 3;
        camera.transform.LookAt(bounds.center);
        camera.cullingMask = 1 << 31;
        camera.farClipPlane = 8;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(.6f, bounds.extents.y * 1.12f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .08f, .12f, 1);
        camera.targetTexture = previewTexture;
        var light = camera.gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.cullingMask = camera.cullingMask;
        light.intensity = 1.4f;
        var animator = visual.GetComponent<Animator>();
        var idle = FindClip(GangsterPlayerVisual.IsFemale(gender) ? Walk : "fight_b.FightB_IDLE");
        if (idle) Play(animator, idle);
        return animator;
    }

    void Play(Animator animator, AnimationClip clip)
    {
        if (graph.IsValid()) graph.Destroy();
        graph = PlayableGraph.Create("Prévia de animação");
        graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
        playingClip = AnimationClipPlayable.Create(graph, clip);
        var output = AnimationPlayableOutput.Create(graph, "Personagem", animator);
        output.SetSourcePlayable(playingClip);
        output.SetWeight(1);
        graph.Play();
        graph.Evaluate(0);
    }

    internal static void PlayInWorld(ThirdPersonPlayer player, AnimationClip clip)
    {
        if (!player || !player.animator || !clip || !player.IsGrounded || player.InPoliceCar || player.IsCuffed) return;
        var playback = player.GetComponent<AnimationTestWheel>() ?? player.gameObject.AddComponent<AnimationTestWheel>();
        playback.ClearVisualLift();
        playback.worldPlayer = player;
        playback.worldAnimator = player.animator;
        playback.leftFoot = player.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        playback.rightFoot = player.animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (playback.leftFoot && playback.rightFoot)
            playback.footHeight = playback.LowestFoot() - player.transform.position.y;
        playback.finishTime = Time.unscaledTime + (clip.isLooping ? Mathf.Max(3f, clip.length) : clip.length);
        playback.Play(playback.worldAnimator, clip);
    }

    void LateUpdate()
    {
        if (!worldPlayer || !graph.IsValid()) return;
        if (Time.unscaledTime >= finishTime || worldPlayer.CurrentSpeed > .15f || worldPlayer.IsJumping ||
            worldPlayer.IsCuffed || worldPlayer.InPoliceCar || worldPlayer.animator != worldAnimator)
        {
            ClearVisualLift();
            graph.Destroy();
            return;
        }
        // Correct the rendered avatar only. The player and its collision stay on the ground.
        if (leftFoot && rightFoot && worldAnimator.transform != worldPlayer.transform)
        {
            float uncorrectedFoot = LowestFoot() - visualLift;
            float lift = Mathf.Max(0, worldPlayer.transform.position.y + footHeight - uncorrectedFoot);
            worldAnimator.transform.position += Vector3.up * (lift - visualLift);
            visualLift = lift;
        }
    }

    float LowestFoot() => Mathf.Min(leftFoot.position.y, rightFoot.position.y);

    void ClearVisualLift()
    {
        if (worldAnimator && visualLift != 0) worldAnimator.transform.position -= Vector3.up * visualLift;
        visualLift = 0;
    }

    void OnDisable()
    {
        ClearVisualLift();
        if (graph.IsValid()) graph.Destroy();
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
        onClose?.Invoke();
    }
}
