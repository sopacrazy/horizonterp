using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class WuzimuImport
{
    const string Path = "Assets/Resources/Characters/Wuzimu/Wuzimu.fbx";
    static void Configure()
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(Path);
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation=false;
        importer.isReadable=true;
        importer.bakeAxisConversion=true;
        var standard=(ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx");
        var description=standard.humanDescription;
        // Raybans is a prop in the original model, not an eye in this skin.
        description.human=description.human.Where(b=>b.boneName!="Raybans").ToArray();
        description.skeleton=Array.Empty<SkeletonBone>();
        importer.humanDescription=description;
        importer.SaveAndReimport();
    }
    [MenuItem("Horizonte/Testes/Preparar e verificar Wuzimu")]
    public static void Verify()
    {
        AssetDatabase.ImportAsset(Path,ImportAssetOptions.ForceUpdate);
        Configure();
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        var avatar=AssetDatabase.LoadAllAssetsAtPath(Path).OfType<Avatar>().FirstOrDefault(a=>a.isHuman&&a.isValid);
        if(!source||!avatar)throw new Exception("Wuzimu sem Avatar Humanoid válido. Verifique o mapeamento dos ossos.");
        var walking=AssetDatabase.LoadAllAssetsAtPath("Assets/Walking.fbx").OfType<AnimationClip>().FirstOrDefault(c=>c.humanMotion&&!c.name.StartsWith("__preview"));
        if(walking)
        {
            var controller=AnimatorController.CreateAnimatorControllerAtPath("Assets/WuzimuProbe.controller");
            controller.AddMotion(walking);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(source);
            Debug.Log("WUZIMU_REST root="+instance.transform.eulerAngles);
            try
            {
                var animator=instance.GetComponent<Animator>();animator.avatar=avatar;animator.runtimeAnimatorController=controller;
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                var leg=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();AnimationMode.SampleAnimationClip(instance,walking,.1f);AnimationMode.EndSampling();
                instance.transform.rotation=Quaternion.identity;
                var first=leg.localRotation;
                AnimationMode.BeginSampling();AnimationMode.SampleAnimationClip(instance,walking,.4f);AnimationMode.EndSampling();
                instance.transform.rotation=Quaternion.identity;
                float delta=Quaternion.Angle(first,leg.localRotation);
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                float upright=Vector3.Dot((head.position-hips.position).normalized,Vector3.up);
                Debug.Log("WUZIMU_POSTURE upright="+upright+" root="+instance.transform.eulerAngles+" hips="+hips.position+" head="+head.position);
                AnimationMode.StopAnimationMode();
                if(delta<.1f)throw new Exception("A caminhada Humanoid não movimentou o esqueleto Wuzimu.");
                if(upright<.8f)throw new Exception("Wuzimu fica deitado ou inclinado ao amostrar caminhada.");
                Debug.Log("WUZIMU_WALK_RETARGET_OK delta="+delta);
            }
            finally { if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();UnityEngine.Object.DestroyImmediate(instance);AssetDatabase.DeleteAsset("Assets/WuzimuProbe.controller"); }
        }
        Debug.Log("WUZIMU_HUMANOID_OK bones="+source.GetComponentsInChildren<Transform>(true).Length);
        File.WriteAllText("wuzimu-unity-validation.txt","WUZIMU_HUMANOID_OK\n"+DateTime.UtcNow.ToString("O"));
    }
}
