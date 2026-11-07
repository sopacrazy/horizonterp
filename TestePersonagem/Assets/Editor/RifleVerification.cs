using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RifleVerification
{
    [Serializable] sealed class Report{public bool passed;public List<string> checks=new List<string>();public string error;}
    static void Check(Report report,bool condition,string message){if(!condition)throw new Exception(message);report.checks.Add(message);}
    [MenuItem("Horizonte/Testes locais/Validar rifle nos dois personagens")]
    public static void Run()
    {
        string folder=Path.GetFullPath("../tools/rifle-verification");Directory.CreateDirectory(folder);
        var report=new Report();var preview=EditorSceneManager.NewPreviewScene();var objects=new List<GameObject>();
        try
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Locomocao.controller");
            Check(report,controller&&controller.layers.Any(l=>l.name=="SARifle"||l.name=="Rifle"),"Camada de locomoção armada disponível");
            foreach(string gender in new[]{"male","female"})
            {
                var root=new GameObject("Rifle review "+gender);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);objects.Add(root);
                var model=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,root.transform);
                var animator=model.GetComponent<Animator>();animator.Rebind();animator.Update(0);
                var visual=model.AddComponent<RifleAvatar>();visual.Configure(animator,root.transform);
                int nativeLayer=animator.GetLayerIndex("SARifle");
                bool native=nativeLayer>=0;
                int activeLayer=native?nativeLayer:animator.GetLayerIndex("Rifle");
                int fireLayer=animator.GetLayerIndex(native?"SARifleFire":"RifleFire");
                Check(report,visual.Ready,gender+": rifle e Humanoid preparados");
                foreach(float speed in new[]{0,1.65f,4.5f})
                {
                    visual.Apply(true,speed<3,0,3,speed);visual.PrepareAnimationFrame(.1f);
                    animator.SetLayerWeight(activeLayer,1);animator.SetFloat(native?"SARifleSpeed":"RifleSpeed",speed);
                    animator.Play(native?(speed<3?"SARifle.Aim":"SARifle.Carry"):"Rifle.Locomotion",activeLayer,.25f);animator.Update(.016f);
                    visual.ApplyAttachmentPose();
                    Check(report,visual.Visible&&visual.Muzzle&&float.IsFinite(visual.Muzzle.position.x),gender+": pose válida em "+speed+" m/s");
                    var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    Check(report,Vector3.Distance(hand.position,visual.Muzzle.position)<1.1f,gender+": arma ligada à mão");
                    Check(report,visual.GripError<.12f,gender+": mãos alinhadas aos encaixes em "+speed+" m/s, erro="+visual.GripError);
                    if(speed==0)Render(preview,root,folder+"/"+gender+"-aim.png");
                    if(speed>4)Render(preview,root,folder+"/"+gender+"-run.png");
                }
                visual.Apply(true,true,0,3,0);visual.PrepareAnimationFrame(.1f);animator.SetFloat(native?"SARifleSpeed":"RifleSpeed",0);animator.Update(.2f);
                visual.PlayShot();Check(report,animator.GetLayerWeight(fireLayer)>0,gender+": disparo inicia sem esperar transição");
                animator.Update(.15f);
                visual.ApplyAttachmentPose();
                Render(preview,root,folder+"/"+gender+"-fire.png");
                visual.Apply(false,false,0,0,0);
                visual.PrepareAnimationFrame(.1f);visual.ApplyAttachmentPose();
                Check(report,!visual.Visible,gender+": guardar remove arma");root.SetActive(false);
            }
            report.passed=true;Debug.Log("RIFLE_VERIFIED checks="+report.checks.Count);
        }
        catch(Exception error){report.error=error.ToString();throw;}
        finally{File.WriteAllText(folder+"/validation.json",JsonUtility.ToJson(report,true));foreach(var item in objects)UnityEngine.Object.DestroyImmediate(item);EditorSceneManager.ClosePreviewScene(preview);}
    }
    static void Render(UnityEngine.SceneManagement.Scene scene,GameObject root,string path)
    {
        // Freeze current bone transforms: edit-mode Camera.Render can otherwise
        // resample the Animator after the IK correction and show a false gap.
        var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();var snapshots=new List<GameObject>();var meshes=new List<Mesh>();
        var bake=typeof(ShootingGripReview).GetMethod("SkinWorld",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var skin in skins)
        {
            var mesh=(Mesh)bake.Invoke(null,new object[]{skin});meshes.Add(mesh);
            var snapshot=new GameObject("Frozen rifle review mesh");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(snapshot,scene);
            snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            snapshots.Add(snapshot);skin.enabled=false;
        }
        var cameraObject=new GameObject("Rifle preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.22f,.26f,.3f);
        camera.transform.position=new Vector3(2.5f,1.8f,2.4f);camera.transform.LookAt(new Vector3(0,1.1f,.15f));
        var lightObject=new GameObject("Preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-120,0);
        var texture=new RenderTexture(1000,900,24);var before=RenderTexture.active;camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
        var image=new Texture2D(1000,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,900),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=before;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
        foreach(var snapshot in snapshots)UnityEngine.Object.DestroyImmediate(snapshot);
        foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
        foreach(var skin in skins)skin.enabled=true;
    }
}
