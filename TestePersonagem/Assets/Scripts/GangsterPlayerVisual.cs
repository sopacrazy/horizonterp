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
                { "IdleRetarget", "GangsterMusculo/GangsterMusculo" },
                { "Andar_WalkingOriginal", "GangsterMusculo/Animations/Walking" },
                { "Correr_FastRunEnviado", "GangsterMusculo/Animations/Running" },
                { "JumpRetarget", "GangsterMusculo/Animations/Jump" }
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
            overrides.ApplyOverrides(clips);
            animator.runtimeAnimatorController = overrides;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            original.gameObject.SetActive(false);
            player.animator = animator;
            Destroy(original.gameObject);
            installedGender = female ? "female" : "male";
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
        // A command can finish while the player is driving or in the firing range.
        // Apply the confirmed gender as soon as those animations are no longer using this avatar.
        var gender = AccountSession.Instance?.State?.gender;
        if (!string.IsNullOrEmpty(gender) && !string.Equals(installedGender, gender, StringComparison.OrdinalIgnoreCase))
            SwitchGender(gender);
    }

    // Returns false while another action owns the avatar, so callers can retry later.
    public bool SwitchGender(string gender)
    {
        if (!string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(installedGender, gender, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(failedGender,gender,StringComparison.OrdinalIgnoreCase)&&
            Time.unscaledTime<nextVisualRetry) return false;

        if (!player) player = GetComponent<ThirdPersonPlayer>();
        if (!player || !player.animator || !player.animator.runtimeAnimatorController ||
            player.animator.gameObject == gameObject || !player.animator.gameObject.activeInHierarchy ||
            player.InPoliceCar || player.CombatLocked || player.IsJumping || player.IsTeleporting ||
            MissionVehicle.Driver == player || PoliceVehicleController.BlocksPlayer(player) ||
            (ShootingRange.Instance && ShootingRange.Instance.IsActive)) return false;

        bool female = IsFemale(gender);
        var source = Resources.Load<GameObject>(female ? FemalePath : MalePath);
        if (!source){DeferVisualRetry(gender);return false;}

        var previous = player.animator;
        var state = previous.GetCurrentAnimatorStateInfo(0);
        float speed = player.CurrentSpeed;
        var visual = Instantiate(source, transform, false);
        visual.name = female ? "ByBeyby feminina" : "Gangster Musculo";
        try
        {
            var animator = PrepareVisual(visual, female);
            animator.runtimeAnimatorController = previous.runtimeAnimatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.SetFloat("Speed", speed);
            animator.SetBool("Jumping", false);
            if (state.fullPathHash != 0)
                animator.Play(state.fullPathHash, 0, state.normalizedTime % 1f);
            animator.Update(0f);

            player.RebindVisual(animator);
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
            installedGender = female ? "female" : "male";
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

    static Animator PrepareVisual(GameObject visual,bool female)
    {
        var importedAnimator=visual.GetComponentInChildren<Animator>();
        var avatar=importedAnimator?importedAnimator.avatar:
            Resources.LoadAll<Avatar>(female?FemalePath:MalePath).FirstOrDefault(candidate=>candidate&&candidate.isValid);
        var animator=visual.GetComponent<Animator>()??visual.AddComponent<Animator>();
        if(importedAnimator&&importedAnimator!=animator)
        {importedAnimator.enabled=false;Destroy(importedAnimator);}
        animator.avatar=avatar;
        if(!avatar||!avatar.isValid||!avatar.isHuman)
            throw new InvalidOperationException("Avatar Humanoid inválido para "+(female?"ByBeyby":"Gangster Musculo"));
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var collider in visual.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        if(!female)ApplyMaleMaterials(visual);
        foreach(var child in visual.GetComponentsInChildren<Transform>(true))child.gameObject.layer=2;
        visual.transform.localPosition=Vector3.zero;
        if(female)MatchFemaleHeight(visual);
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
