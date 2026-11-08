using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Installs the Gangster visual before the player and multiplayer systems start.
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class GangsterPlayerVisual : MonoBehaviour
{
    public static bool IsFemale(string gender) => string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase);
    const string FemalePath = "Characters/ByBeyby";
    const string MalePath = "GangsterMusculo/GangsterMusculo";
    const string WuzimuPath = "Characters/Wuzimu/Wuzimu";
    const string FlaPath = "Characters/Fla/Fla";
    internal const string FlaJumpClip = "FlaJump";
    const string RunClipName = "Correr_FastRunEnviado";
    const string TestRunPath = "GangsterMusculo/Animations/Gangster_Corrida_Base";
    string previewModel;
    RuntimeAnimatorController previewOriginalController;
    bool saLocomotionApplied;
    float nextSaLocomotionRetry;
    AnimatorOverrideController runOverrides;
    AnimationClip originalRunClip;
    bool testRunActive;
    float runLabelUntil;
    string runLabel;
    static bool maleReferenceReady;
    static float maleReferenceHeight,maleReferenceGround;
    string installedGender;
    string failedGender;
    float nextVisualRetry;
    const float VisualRetryDelay=5f;
    ThirdPersonPlayer player;
    static readonly Dictionary<string, string[]> SurfaceTextures = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "Gangsta2_HIK_Dealer_Musculo_Cloth", new[] { "Dealerguy_El_Musculo_Male_Pants_B", "Dealerguy_El_Musculo_Male_Pants_N" } },
        { "Gangsta2_HIK_Dealer_Musculo_Body", new[] { "Dealerguy_El_Musculo_Male_Body_B", "Dealerguy_El_Musculo_Male_Body_N" } },
        { "Gangsta2_HIK_Dealer_Musculo_Head", new[] { "Dealerguy_El_Musculo_Male_Face_B", "Dealerguy_El_Musculo_Male_Face_N" } }
    };

    void Awake()
    {
        player = GetComponent<ThirdPersonPlayer>();
        var original = player.animator;
        bool female=IsFemale(AccountSession.Instance?.State?.gender);
        var source = Resources.Load<GameObject>(female?FemalePath:MalePath);
        if (!source || !original || !original.runtimeAnimatorController)
        {
            DeferVisualRetry(female?"female":"male");
            Debug.LogError("Visual do personagem: modelo ou animador original ausente.");
            return;
        }

        var visual = Instantiate(source, transform, false);
        visual.name = female?"ByBeyby feminina":"Gangster Musculo";

        try
        {
            var animator=PrepareVisual(visual,female);
            var overrides = new AnimatorOverrideController(original.runtimeAnimatorController);
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(clips);
            var replacements = new Dictionary<string, string>
            {
                { "IdleRetarget", female ? "GangsterMusculo/GangsterMusculo" : "Characters/Wuzimu/Animations/ParadoSA" },
                { "Andar_WalkingOriginal", female ? "GangsterMusculo/Animations/Walking" : "Characters/Wuzimu/Animations/AndarSA" },
                { RunClipName, female ? "GangsterMusculo/Animations/Running" : "Characters/Wuzimu/Animations/CorrerSA" },
                { "JumpRetarget", female ? "GangsterMusculo/Animations/Jump" : "Characters/Wuzimu/Animations/PuloSA_Personagem" }
            };
            for (int i = 0; i < clips.Count; i++)
            {
                var originalClip = clips[i].Key;
                if (!originalClip || !replacements.TryGetValue(originalClip.name, out var path)) continue;
                var clip = Resources.LoadAll<AnimationClip>(path)
                    .FirstOrDefault(candidate => candidate && candidate.length > 0 && candidate.humanMotion &&
                        !candidate.name.StartsWith("__preview"));
                if (!clip) throw new InvalidOperationException("Animação Humanoid ausente: " + path);
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(originalClip, clip);
                replacements.Remove(originalClip.name);
            }
            if (replacements.Count != 0)
                throw new InvalidOperationException("Clipes do controlador não encontrados: " + string.Join(", ", replacements.Keys));
            originalRunClip = clips.First(pair => pair.Key && pair.Key.name == RunClipName).Value;
            overrides.ApplyOverrides(clips);
            runOverrides = overrides;
            animator.runtimeAnimatorController = overrides;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            original.gameObject.SetActive(false);
            player.animator = animator;
            Destroy(original.gameObject);
            installedGender = female ? "female" : "male";
            saLocomotionApplied = !female;
            ShowRunLabel(female ? "Locomoção feminina (F8 para testar outra corrida)" : "Parado, andar, correr e pular SA ativos");
            ClearVisualRetry();
            if (!GetComponent<FighterCombo>()) gameObject.AddComponent<FighterCombo>();
        }
        catch (Exception exception)
        {
            DeferVisualRetry(female?"female":"male");
            Debug.LogError("Visual do personagem: falha ao substituir visual: " + exception);
            Destroy(visual);
        }
    }

    void Update()
    {
        ApplySaLocomotionToCurrentVisual();
        if (Input.GetKeyDown(KeyCode.F8) && !InGameChat.Editing &&
            !(GameHud.Instance && GameHud.Instance.IsPanelOpen))
            ToggleRunAnimation();
        // A command can finish while the player is driving or in the firing range.
        // Apply the confirmed gender as soon as those animations are no longer using this avatar.
        var gender = AccountSession.Instance?.State?.gender;
        if (!string.IsNullOrEmpty(gender) && !string.Equals(installedGender, gender, StringComparison.OrdinalIgnoreCase))
            SwitchGender(gender);
    }

    // Also updates an existing Play session after the editor reloads scripts.
    // It replaces clips on the current Animator without instantiating a skin.
    void ApplySaLocomotionToCurrentVisual()
    {
        if (saLocomotionApplied || previewModel != null || Time.unscaledTime < nextSaLocomotionRetry) return;
        if (!player) player = GetComponent<ThirdPersonPlayer>();
        if (!player || !player.animator || !player.animator.runtimeAnimatorController || IsFemale(installedGender)) return;
        if (player.InPoliceCar || player.CombatLocked || player.IsJumping || player.IsTeleporting ||
            (ShootingRange.Instance && ShootingRange.Instance.IsActive) || GetComponent<RifleWeapon>()?.Equipped == true) return;
        nextSaLocomotionRetry = Time.unscaledTime + 3;
        try
        {
            var overrides = CreateSaPreviewController(player.animator.runtimeAnimatorController);
            player.animator.runtimeAnimatorController = overrides;
            player.animator.applyRootMotion = false;
            runOverrides = overrides;
            saLocomotionApplied = true;
            ShowRunLabel("Parado, andar, correr e pular SA ativos");
        }
        catch (Exception e) { Debug.LogWarning("Animacoes SA aguardando importacao: " + e.Message); }
    }

    void ToggleRunAnimation()
    {
        if (!runOverrides || !originalRunClip || !player || !player.animator) return;
        if (!testRunActive)
        {
            var testClip = Resources.LoadAll<AnimationClip>(TestRunPath)
                .FirstOrDefault(candidate => candidate && candidate.length > 0 && candidate.humanMotion &&
                    !candidate.name.StartsWith("__preview"));
            if (!testClip)
            {
                ShowRunLabel("Corrida de teste indisponível; confira a importação do FBX");
                Debug.LogError("Corrida de teste: clipe Humanoid ausente em " + TestRunPath);
                return;
            }
            runOverrides[RunClipName] = testClip;
            testRunActive = true;
            ShowRunLabel("Corrida Blender ativa (F8 para voltar)");
        }
        else
        {
            runOverrides[RunClipName] = originalRunClip;
            testRunActive = false;
            ShowRunLabel("Corrida original ativa (F8 para trocar)");
        }
    }

    void ShowRunLabel(string message)
    {
        runLabel = message;
        runLabelUntil = Time.unscaledTime + 5f;
        Debug.Log(message);
    }

    void OnGUI()
    {
        if (Time.unscaledTime < runLabelUntil && !string.IsNullOrEmpty(runLabel))
            GUI.Box(new Rect(16f, Screen.height - 58f, 390f, 38f), runLabel);
    }

    // Returns false while another action owns the avatar, so callers can retry later.
    public bool SwitchGender(string gender)
        => SwitchVisual(gender, null);

    // Local test selection only; never changes the saved gender or account identity.
    public bool SwitchPreviewModel(string model)
        => SwitchVisual(AccountSession.Instance?.State?.gender ?? installedGender ?? "male",
            model switch
            {
                "wuzimu" or "sa" => WuzimuPath,
                "fla" => FlaPath,
                _ => null
            });

    bool SwitchVisual(string gender, string modelPath)
    {
        if (!string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(installedGender, gender, StringComparison.OrdinalIgnoreCase) && previewModel == modelPath) return true;
        if (string.Equals(failedGender,gender,StringComparison.OrdinalIgnoreCase)&&
            Time.unscaledTime<nextVisualRetry) return false;

        if (!player) player = GetComponent<ThirdPersonPlayer>();
        if (!player || !player.animator || !player.animator.runtimeAnimatorController ||
            player.animator.gameObject == gameObject || !player.animator.gameObject.activeInHierarchy ||
            player.InPoliceCar || player.CombatLocked || player.IsJumping || player.IsTeleporting ||
            MissionVehicle.Driver == player || PoliceVehicleController.BlocksPlayer(player) ||
            (ShootingRange.Instance && ShootingRange.Instance.IsActive)) return false;
        if (player.GetComponent<RifleWeapon>()?.Equipped == true) return false;

        bool female = IsFemale(gender);
        var source = Resources.Load<GameObject>(modelPath ?? (female ? FemalePath : MalePath));
        if (!source){DeferVisualRetry(gender);return false;}

        var previous = player.animator;
        var state = previous.GetCurrentAnimatorStateInfo(0);
        float speed = player.CurrentSpeed;
        var visual = Instantiate(source, transform, false);
        visual.name = modelPath == FlaPath ? "Fla • teste local" :
            modelPath != null ? "Personagem SA • teste local" : female ? "ByBeyby feminina" : "Gangster Musculo";
        try
        {
            var animator = PrepareVisual(visual, modelPath == null && female, modelPath);
            var normalizedScale=visual.transform.localScale;
            var normalizedPosition=visual.transform.localPosition;
            var originalController = previewOriginalController ? previewOriginalController : previous.runtimeAnimatorController;
            animator.runtimeAnimatorController = modelPath != null
                ? CreatePreviewController(originalController, modelPath == FlaPath)
                : originalController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.SetBool("Jumping", false);
            if (modelPath == FlaPath)
            {
                animator.SetFloat("Speed", 0f);
                animator.Play("Locomocao", 0, 0f);
            }
            else
            {
                animator.SetFloat("Speed", speed);
                if (state.fullPathHash != 0)
                    animator.Play(state.fullPathHash, 0, state.normalizedTime % 1f);
            }
            animator.Update(0f);
            if (modelPath != null)
            {
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale=normalizedScale;
                visual.transform.localPosition=normalizedPosition;
                // The imported bind pose differs from the evaluated Humanoid pose.
                // Size the visible body only after the actual controller has evaluated.
                CharacterSkinSizing.Match(visual,maleReferenceHeight,maleReferenceGround);
            }
            if (modelPath == FlaPath)
            {
                animator.SetFloat("Speed", speed);
                if (state.fullPathHash != 0)
                    animator.Play(state.fullPathHash, 0, state.normalizedTime % 1f);
                animator.Update(0f);
            }

            player.RebindVisual(animator);
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
            installedGender = female ? "female" : "male";
            saLocomotionApplied = false;
            previewOriginalController = modelPath != null ? originalController : null;
            previewModel = modelPath;
            ClearVisualRetry();
            return true;
        }
        catch (Exception exception)
        {
            DeferVisualRetry(gender);
            Debug.LogError("Visual do personagem: troca de gênero falhou: " + exception);
            Destroy(visual);
            return false;
        }
    }

    void DeferVisualRetry(string gender)
    {failedGender=gender;nextVisualRetry=Time.unscaledTime+VisualRetryDelay;}

    void ClearVisualRetry()
    {failedGender=null;nextVisualRetry=0f;}

    public static GameObject CreateRemoteVisual(string gender,RuntimeAnimatorController controller,Transform parent)
    {
        bool female=IsFemale(gender);
        var source=Resources.Load<GameObject>(female?FemalePath:MalePath);
        if(!source)throw new InvalidOperationException("Visual remoto ausente: "+(female?FemalePath:MalePath));
        var visual=Instantiate(source,parent,false);
        visual.name=female?"ByBeyby feminina remota":"Gangster Musculo remoto";
        try
        {
            var animator=PrepareVisual(visual,female);
            animator.runtimeAnimatorController=controller;
            return visual;
        }
        catch{Destroy(visual);throw;}
    }

    static AnimatorOverrideController CreateSaPreviewController(RuntimeAnimatorController original)
        => CreatePreviewController(original, false);

    static AnimatorOverrideController CreatePreviewController(RuntimeAnimatorController original, bool fla)
    {
        var result = new AnimatorOverrideController(original);
        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        if (original is AnimatorOverrideController previousOverrides)
        {
            previousOverrides.GetOverrides(clips);
            result.ApplyOverrides(clips);
            clips.Clear();
        }
        result.GetOverrides(clips);
        var paths = new Dictionary<string, string>
        {
            { "IdleRetarget", "Characters/Wuzimu/Animations/ParadoSA" },
            { "Andar_WalkingOriginal", "Characters/Wuzimu/Animations/AndarSA" },
            { RunClipName, "Characters/Wuzimu/Animations/CorrerSA" },
            { "JumpRetarget", "Characters/Wuzimu/Animations/PuloSA_Personagem" }
        };
        var flaClips = fla ? Resources.LoadAll<AnimationClip>(FlaPath) : Array.Empty<AnimationClip>();
        for (int i = 0; i < clips.Count; i++)
        {
            if (!clips[i].Key || !paths.TryGetValue(clips[i].Key.name, out string path)) continue;
            string flaClip = fla ? clips[i].Key.name switch
            {
                "IdleRetarget" => "FlaIdle",
                "Andar_WalkingOriginal" => "FlaWalk",
                "JumpRetarget" => FlaJumpClip,
                _ => null
            } : null;
            var clip = flaClip != null
                ? flaClips.FirstOrDefault(c => c && c.humanMotion && c.name == flaClip)
                : Resources.LoadAll<AnimationClip>(path).FirstOrDefault(c => c && c.humanMotion && !c.name.StartsWith("__preview"));
            if (!clip) throw new InvalidOperationException("Animação de teste ausente: " + (flaClip ?? path));
            clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clips[i].Key, clip);
            paths.Remove(clips[i].Key.name);
        }
        if (paths.Count != 0) throw new InvalidOperationException("Controlador sem os clipes de locomocao SA: " + string.Join(", ", paths.Keys));
        result.ApplyOverrides(clips);
        return result;
    }

    static Animator PrepareVisual(GameObject visual,bool female,string modelPath=null)
    {
        var importedAnimator=visual.GetComponentInChildren<Animator>();
        var avatar=importedAnimator?importedAnimator.avatar:
            Resources.LoadAll<Avatar>(modelPath ?? (female?FemalePath:MalePath)).FirstOrDefault(candidate=>candidate&&candidate.isValid);
        var animator=visual.GetComponent<Animator>()??visual.AddComponent<Animator>();
        if(importedAnimator&&importedAnimator!=animator)
        {importedAnimator.enabled=false;Destroy(importedAnimator);}
        animator.avatar=avatar;
        if(!avatar||!avatar.isValid||!avatar.isHuman)
            throw new InvalidOperationException("Avatar Humanoid inválido para "+(female?"ByBeyby":"Gangster Musculo"));
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var collider in visual.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        if(!female && modelPath == null)ApplyMaleMaterials(visual);
        foreach(var child in visual.GetComponentsInChildren<Transform>(true))child.gameObject.layer=2;
        visual.transform.localPosition=Vector3.zero;
        if (modelPath != null) visual.transform.localRotation = Quaternion.identity;
        if(modelPath != null)
        {
            EnsureMaleReference();
            CharacterSkinSizing.Match(visual,maleReferenceHeight,maleReferenceGround);
        }
        else if(female)MatchFemaleHeight(visual);
        else
        {
            var renderers=visual.GetComponentsInChildren<Renderer>();
            if(renderers.Length>0)
            {
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                if(bounds.size.y>.1f)visual.transform.localScale*=1.8f/bounds.size.y;
            }
        }
        return animator;
    }

    static void MatchFemaleHeight(GameObject visual)
    {
        EnsureMaleReference();
        var geometry=MeasureGeometryBounds(visual);
        if(geometry.size.y<.1f)throw new InvalidOperationException("Altura geométrica feminina inválida.");
        visual.transform.localScale*=maleReferenceHeight/geometry.size.y;
        geometry=MeasureGeometryBounds(visual);
        visual.transform.localPosition+=Vector3.up*(maleReferenceGround-geometry.min.y);
    }

    static void EnsureMaleReference()
    {
        if(maleReferenceReady)return;
        var source=Resources.Load<GameObject>(MalePath);
        if(!source)throw new InvalidOperationException("Modelo masculino ausente para normalizar altura.");
        var reference=Instantiate(source);
        reference.name="Referência temporária de altura";reference.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            // Keep the male visual's established scale. Its renderer bounds determine
            // that scale, but only actual mesh bounds determine the resulting body height.
            var renderers=reference.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)throw new InvalidOperationException("Modelo masculino sem renderizadores.");
            var culling=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1))culling.Encapsulate(renderer.bounds);
            if(culling.size.y<.1f)throw new InvalidOperationException("Altura masculina inválida.");
            reference.transform.localScale*=1.8f/culling.size.y;
            reference.transform.localPosition=Vector3.zero;
            var geometry=MeasureGeometryBounds(reference);
            if(geometry.size.y<.1f)throw new InvalidOperationException("Geometria masculina inválida.");
            maleReferenceHeight=geometry.size.y;maleReferenceGround=geometry.min.y;maleReferenceReady=true;
        }
        finally
        {
            reference.SetActive(false);
            if(Application.isPlaying)Destroy(reference);else DestroyImmediate(reference);
        }
    }

    // Measure the bind-pose mesh bounds in the visual parent's space. Skinned
    // renderer bounds contain extra animation/culling margins (ByBeyby adds 0.5m).
    // Mesh bounds do not need Read/Write and do not include those margins.
    public static Bounds MeasureGeometryBounds(GameObject visual)
    {
        bool found=false;var result=new Bounds();
        var rootToParent=Matrix4x4.TRS(visual.transform.localPosition,visual.transform.localRotation,visual.transform.localScale);
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(!mesh)continue;
            var matrix=rootToParent*visual.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            var bounds=mesh.bounds;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                var point=matrix.MultiplyPoint3x4(corner);
                if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
            }
        }
        if(!found)throw new InvalidOperationException("Personagem sem geometria para medir altura.");
        return result;
    }

    static void ApplyMaleMaterials(GameObject visual)
    {
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var source=materials[i];
                if(!source||!SurfaceTextures.TryGetValue(source.name,out var names))continue;
                var color=Resources.Load<Texture2D>("GangsterMusculo/textures/"+names[0]);
                var normal=Resources.Load<Texture2D>("GangsterMusculo/textures/"+names[1]);
                if(!color||!normal)throw new InvalidOperationException("Texturas ausentes: "+source.name);
                var material=new Material(Shader.Find("Standard")){name=source.name,mainTexture=color,color=Color.white};
                material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",.6f);
                material.EnableKeyword("_NORMALMAP");material.SetFloat("_Glossiness",.16f);
                materials[i]=material;
            }
            renderer.sharedMaterials=materials;
        }
    }

}
