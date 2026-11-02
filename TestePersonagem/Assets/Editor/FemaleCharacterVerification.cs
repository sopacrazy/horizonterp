using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FemaleCharacterVerification
{
    [MenuItem("Horizonte/Testes/Verificar personagem feminino")]
    public static void Verify()
    {
        VerifyDependencyGraph();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/ByBeyby.prefab");
        var imported=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ByBeyby/Prefabs/ByBeyby.prefab");
        if(!prefab||!imported)throw new InvalidOperationException("Prefab ByBeyby não foi importado.");
        var importedAvatar=AssetDatabase.LoadAssetAtPath<Avatar>("Assets/ByBeyby/Models/ByBeybyAvatar.asset");
        if(!importedAvatar||!importedAvatar.isValid||!importedAvatar.isHuman)
            throw new InvalidOperationException("Avatar ByBeyby importado ausente ou inválido.");
        var source=Resources.Load<GameObject>("Characters/ByBeyby");
        if(source!=prefab)throw new InvalidOperationException("Prefab feminino indisponível em Resources.");
        var portrait=Resources.Load<Texture2D>("Characters/ByBeybyPreview");
        if(!portrait||portrait.width<512||portrait.height<512)
            throw new InvalidOperationException("Prévia feminina ausente ou pequena demais.");
        var controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
        if(!controller)throw new InvalidOperationException("Controlador de animações atual ausente.");
        foreach(var path in new[]{"Assets/Resources/GangsterMusculo/Animations/Walking.fbx",
            "Assets/Resources/GangsterMusculo/Animations/Running.fbx",
            "Assets/Resources/GangsterMusculo/Animations/Jump.fbx",
            "Assets/Resources/GangsterMusculo/Animations/Shooting.fbx",
            "Assets/Resources/Combat/PunchCombo.fbx",
            "Assets/Resources/Combat/HitReaction.fbx",
            "Assets/Resources/Combat/Stunned.fbx",
            "Assets/Resources/Vehicles/Police/EnteringCar.fbx",
            "Assets/Resources/Vehicles/Police/ExitingCar.fbx"})
            if(!AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Any(clip=>clip.humanMotion))
                throw new InvalidOperationException("Animação Humanoid ausente: "+path);
        var instance=UnityEngine.Object.Instantiate(prefab);
        try
        {
            var animator=instance.GetComponentInChildren<Animator>();
            var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
            if(!animator||!animator.avatar||!animator.avatar.isValid||!animator.avatar.isHuman)
                throw new InvalidOperationException("Avatar feminino não é Humanoid válido.");
            if(animator.avatar!=importedAvatar)
                throw new InvalidOperationException("Prefab feminino não referencia o Avatar ByBeyby importado.");
            if(!skin||!skin.sharedMesh||skin.bones.Length==0)
                throw new InvalidOperationException("Malha feminina sem esqueleto.");
            if(skin.sharedMaterials.Length<5||skin.sharedMaterials.Any(material=>!material||!material.mainTexture))
                throw new InvalidOperationException("Materiais ou texturas da personagem feminina incompletos.");
            animator.runtimeAnimatorController=controller;
            if(!animator.runtimeAnimatorController)throw new InvalidOperationException("Animações não vinculadas.");
            float femaleHeight=0,maleHeight=0,femaleGround=0,maleGround=0;
            foreach(var gender in new[]{"female","male"})
            {
                var remote=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,null);
                try
                {
                    var remoteAnimator=remote.GetComponent<Animator>();
                    if(!remoteAnimator||!remoteAnimator.avatar||!remoteAnimator.avatar.isHuman||
                       remoteAnimator.runtimeAnimatorController!=controller)
                        throw new InvalidOperationException("Visual remoto inválido para "+gender);
                    var geometry=GangsterPlayerVisual.MeasureGeometryBounds(remote);
                    if(gender=="female"){femaleHeight=geometry.size.y;femaleGround=geometry.min.y;}
                    else {maleHeight=geometry.size.y;maleGround=geometry.min.y;}
                }
                finally{UnityEngine.Object.DestroyImmediate(remote);}
            }
            if(Mathf.Abs(femaleHeight-maleHeight)>.005f||Mathf.Abs(femaleGround-maleGround)>.005f)
                throw new InvalidOperationException($"Alturas/pés diferentes: feminino={femaleHeight:F4}/{femaleGround:F4}, masculino={maleHeight:F4}/{maleGround:F4}");
            Debug.Log($"CHARACTER_HEIGHT_VERIFIED female={femaleHeight:F4} male={maleHeight:F4} femaleGround={femaleGround:F4} maleGround={maleGround:F4}");
            Debug.Log($"FEMALE_CHARACTER_VERIFIED bones={skin.bones.Length} vertices={skin.sharedMesh.vertexCount} controller={controller.name}");
        }
        finally{UnityEngine.Object.DestroyImmediate(instance);}
    }

    // GetDependencies omits unresolved GUIDs. Read serialized references too, so a
    // partially copied prefab cannot pass merely because the prefab itself exists.
    public static void VerifyDependencyGraph()
    {
        var pending=new Queue<string>(new[]{"Assets/Resources/Characters/ByBeyby.prefab",
            "Assets/ByBeyby/Prefabs/ByBeyby.prefab"});
        var visited=new HashSet<string>(StringComparer.Ordinal);
        while(pending.Count>0)
        {
            string path=pending.Dequeue();
            if(!visited.Add(path))continue;
            if(!File.Exists(path)||!File.Exists(path+".meta"))
                throw new InvalidOperationException("Dependência feminina ou meta ausente: "+path);
            using(var reader=new StreamReader(path))
            {
                var header=new char[5];
                if(reader.Read(header,0,header.Length)!=header.Length||new string(header)!="%YAML")continue;
                string text=reader.ReadToEnd();
                foreach(Match reference in Regex.Matches(text,@"\bguid: ([a-fA-F0-9]{32})\b"))
                {
                    string guid=reference.Groups[1].Value.ToLowerInvariant();
                    if(guid.StartsWith("0000000000000000",StringComparison.Ordinal))continue;
                    string dependency=AssetDatabase.GUIDToAssetPath(guid);
                    if(string.IsNullOrEmpty(dependency)||!File.Exists(dependency))
                        throw new InvalidOperationException("GUID feminino não resolvido: "+guid+" em "+path);
                    pending.Enqueue(dependency);
                }
            }
        }
        Debug.Log("FEMALE_DEPENDENCIES_VERIFIED assets="+visited.Count);
    }
}

// Every release target must validate the imported avatar and its full dependency
// graph in the actual build project, including isolated Android copies.
public sealed class FemaleCharacterBuildVerification : IPreprocessBuildWithReport
{
    public int callbackOrder=>-1000;
    public void OnPreprocessBuild(BuildReport report){FemaleCharacterVerification.Verify();}
}
