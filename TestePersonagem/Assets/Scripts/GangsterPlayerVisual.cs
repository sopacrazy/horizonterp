using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Installs the Gangster visual before the player and multiplayer systems start.
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class GangsterPlayerVisual : MonoBehaviour
{
    static readonly Dictionary<string, string[]> SurfaceTextures = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "Gangsta2_HIK_Dealer_Musculo_Cloth", new[] { "Dealerguy_El_Musculo_Male_Pants_B", "Dealerguy_El_Musculo_Male_Pants_N" } },
        { "Gangsta2_HIK_Dealer_Musculo_Body", new[] { "Dealerguy_El_Musculo_Male_Body_B", "Dealerguy_El_Musculo_Male_Body_N" } },
        { "Gangsta2_HIK_Dealer_Musculo_Head", new[] { "Dealerguy_El_Musculo_Male_Face_B", "Dealerguy_El_Musculo_Male_Face_N" } }
    };

    void Awake()
    {
        var player = GetComponent<ThirdPersonPlayer>();
        var original = player.animator;
        var source = Resources.Load<GameObject>("GangsterMusculo/GangsterMusculo");
        if (!source || !original || !original.runtimeAnimatorController)
        {
            Debug.LogError("Gangster Musculo: modelo ou animador original ausente.");
            return;
        }

        var visual = Instantiate(source, transform, false);
        visual.name = "Gangster Musculo";
        var importedAnimator = visual.GetComponentInChildren<Animator>();
        var avatar = importedAnimator ? importedAnimator.avatar :
            Resources.LoadAll<Avatar>("GangsterMusculo/GangsterMusculo").FirstOrDefault(candidate => candidate && candidate.isValid);
        var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
        if (importedAnimator && importedAnimator != animator)
        {
            importedAnimator.enabled = false;
            Destroy(importedAnimator);
        }
        animator.avatar = avatar;
        if (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
        {
            Debug.LogError("Gangster Musculo: Avatar Humanoid inválido.");
            Destroy(visual);
            return;
        }

        try
        {
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

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var sourceMaterial = materials[i];
                    if (!sourceMaterial || !SurfaceTextures.TryGetValue(sourceMaterial.name, out var names)) continue;
                    var color = Resources.Load<Texture2D>("GangsterMusculo/textures/" + names[0]);
                    var normal = Resources.Load<Texture2D>("GangsterMusculo/textures/" + names[1]);
                    if (!color || !normal) throw new InvalidOperationException("Texturas ausentes: " + sourceMaterial.name);
                    var material = new Material(Shader.Find("Standard")) { name = sourceMaterial.name };
                    material.mainTexture = color;
                    material.color = Color.white;
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", .6f);
                    material.EnableKeyword("_NORMALMAP");
                    material.SetFloat("_Glossiness", .16f);
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            foreach (var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;

            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                if (bounds.size.y > .1f)
                {
                    visual.transform.localScale *= 1.8f / bounds.size.y;
                }
            }
            // SkinnedMeshRenderer bounds include padding below the actual shoes.
            // The baked soles sit at the model origin, which matches the controller floor.
            visual.transform.localPosition = Vector3.zero;

            original.gameObject.SetActive(false);
            player.animator = animator;
            Destroy(original.gameObject);
            if (!GetComponent<FighterCombo>()) gameObject.AddComponent<FighterCombo>();
        }
        catch (Exception exception)
        {
            Debug.LogError("Gangster Musculo: falha ao substituir visual: " + exception);
            Destroy(visual);
        }
    }

}
