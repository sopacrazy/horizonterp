using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GangsterMusculoSetup
{
    public const string ModelPath = "Assets/Resources/GangsterMusculo/GangsterMusculo.fbx";

    [MenuItem("Horizonte/Diagnosticar Gangster Musculo")]
    public static void Diagnose()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var name in new[] { "Body", "Face", "Pants" })
        {
            var path = "Assets/Resources/GangsterMusculo/textures/Dealerguy_El_Musculo_Male_" + name + "_N.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var texture = AssetImporter.GetAtPath(path) as TextureImporter;
            if (texture == null || texture.textureType != TextureImporterType.NormalMap)
                throw new Exception("Normal map não importado corretamente: " + path);
        }
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new Exception("FBX Gangster Musculo não importado");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            var animator = model.GetComponentInChildren<Animator>();
            Debug.Log("GANGSTER_AVATAR valid=" + (animator && animator.avatar && animator.avatar.isValid) +
                " human=" + (animator && animator.avatar && animator.avatar.isHuman));
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new Exception("Avatar Humanoid do Gangster inválido");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer;
                Debug.Log("GANGSTER_RENDERER " + renderer.name + " type=" + renderer.GetType().Name +
                    " bounds=" + renderer.bounds + " vertices=" + (skin && skin.sharedMesh ? skin.sharedMesh.vertexCount : 0) +
                    " materials=" + string.Join(",", renderer.sharedMaterials.Select(m => m ? m.name : "NULL")));
            }
            foreach (var path in new[] { ModelPath, "Assets/Resources/GangsterMusculo/Animations/Walking.fbx",
                         "Assets/Resources/GangsterMusculo/Animations/Running.fbx",
                         "Assets/Resources/GangsterMusculo/Animations/Jump.fbx",
                         "Assets/Resources/GangsterMusculo/Animations/Shooting.fbx" })
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview")).ToArray();
                if (!clips.Any(clip => clip.length > 0 && clip.humanMotion))
                    throw new Exception("Animação Humanoid ausente: " + path);
                foreach (var clip in clips)
                    if (!clip.name.StartsWith("__preview"))
                        Debug.Log("GANGSTER_CLIP " + path + " name=" + clip.name + " length=" + clip.length +
                            " human=" + clip.humanMotion);
            }
            var ownClip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview"));
            if (ownClip && animator && animator.avatar && animator.avatar.isHuman)
            {
                var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                foreach (var time in new[] { 0f, 1f, 2f, 4f, 6f, 8f, 9.7f })
                {
                    ownClip.SampleAnimation(model, time);
                    Debug.Log("GANGSTER_SAMPLE t=" + time + " upper=" + leftArm.position +
                        " elbow=" + leftElbow.position + " hand=" + leftHand.position +
                        " leftFoot=" + leftFoot.position + " rightFoot=" + rightFoot.position);
                    if (time == 0f || time == 8f) Debug.Log("GANGSTER_SOLES idle=" + time + " minY=" + BakedMeshMinY(model));
                }
            }
            foreach (var name in new[] { "Walking", "Running" })
            {
                var path = "Assets/Resources/GangsterMusculo/Animations/" + name + ".fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .First(candidate => candidate.length > 0 && candidate.humanMotion && !candidate.name.StartsWith("__preview"));
                var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                clip.SampleAnimation(model, 0f);
                var startFoot = foot.position;
                var startHand = hand.position;
                clip.SampleAnimation(model, clip.length * .25f);
                float footTravel = Vector3.Distance(startFoot, foot.position);
                float handTravel = Vector3.Distance(startHand, hand.position);
                Debug.Log("GANGSTER_MOTION " + name + " footTravel=" + footTravel + " handTravel=" + handTravel);
                if (footTravel < .02f || handTravel < .02f)
                    throw new Exception("Movimento insuficiente ao retargetar " + name);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                for (int frame = 0; frame < 16; frame++)
                {
                    clip.SampleAnimation(model, clip.length * frame / 16f);
                    var hipAxis = hips.rotation * Vector3.up;
                    Debug.Log("GANGSTER_GAIT " + name + " phase=" + frame +
                        " hips=" + hips.position + " roll=" + Mathf.Atan2(hipAxis.x, hipAxis.y) * Mathf.Rad2Deg +
                        " spine=" + spine.position + " feet=" + leftFoot.position + "/" + rightFoot.position);
                    if (frame % 4 == 0) Debug.Log("GANGSTER_SOLES " + name + " phase=" + frame + " minY=" + BakedMeshMinY(model));
                }
            }
            foreach (var bone in model.GetComponentsInChildren<Transform>(true))
                Debug.Log("GANGSTER_BONE " + bone.name + " parent=" + (bone.parent ? bone.parent.name : "-") +
                    " position=" + bone.position);
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }

    static float BakedMeshMinY(GameObject model)
    {
        float lowest = float.PositiveInfinity;
        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            renderer.BakeMesh(mesh);
            foreach (var vertex in mesh.vertices)
                lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(vertex).y);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return lowest;
    }
}

public sealed class GangsterMusculoModelImport : AssetPostprocessor
{
    const string Root = "Assets/Resources/GangsterMusculo/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root + "textures/", StringComparison.Ordinal) ||
            !assetPath.EndsWith("_N.png", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Root, StringComparison.Ordinal) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = assetPath == Root + "GangsterMusculo.fbx" ?
            ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(Root, StringComparison.Ordinal) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        foreach (var clip in clips)
        {
            if (assetPath.EndsWith("Punching.fbx", StringComparison.OrdinalIgnoreCase))
                clip.name = "Punching";
            if (assetPath.EndsWith("Shooting.fbx", StringComparison.OrdinalIgnoreCase))
                clip.name = "Shooting";
            clip.loopTime = !assetPath.EndsWith("Jump.fbx", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.EndsWith("Punching.fbx", StringComparison.OrdinalIgnoreCase);
            clip.loopPose = clip.loopTime;
        }
        importer.clipAnimations = clips;
    }
}
