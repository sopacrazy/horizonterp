using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Preview and inspection only. AuthoredWalkSetup imports the original Walking.fbx.
public static class StraightWalkSetup
{
    const string ClipPath="Assets/Animations/WalkingRetarget.anim";
    const string ModelPath="Assets/Personagem/ByBeyby.fbx";
    internal static void Render(AnimationClip clip,string prefix)
    {
        var snapshot=UnityEngine.Object.Instantiate(clip);snapshot.hideFlags=HideFlags.HideAndDontSave;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.7f,.7f,.7f);RenderSettings.fog=false;
        var light=new GameObject("Review light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(45,-35,0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.28f,.3f,.33f)};
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));RetargetIdle.RestoreBindPose(model);
        var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked=new GameObject("Review mesh",typeof(MeshFilter),typeof(MeshRenderer));
        var mesh=new Mesh();baked.GetComponent<MeshFilter>().sharedMesh=mesh;baked.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
        baked.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);baked.transform.localScale=skin.transform.lossyScale;skin.enabled=false;
        var camera=new GameObject("Review camera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.02f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.2f,.24f);
        var rt=new RenderTexture(300,350,24);camera.targetTexture=rt;
        var tile=new Texture2D(300,350,TextureFormat.RGB24,false);
        for(int view=0;view<3;view++)
        {
            string name=new[]{"front","back","side"}[view];
            camera.transform.position=new[]{new Vector3(0,.94f,3),new Vector3(0,.94f,-3),new Vector3(3,.94f,0)}[view];
            camera.transform.LookAt(new Vector3(0,.94f,0));
            var sheet=new Texture2D(1200,700,TextureFormat.RGB24,false);
            for(int frame=0;frame<8;frame++)
            {
                snapshot.SampleAnimation(model,snapshot.length*frame/8);skin.BakeMesh(mesh);
                camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,300,350),0,0);tile.Apply();
                sheet.SetPixels(frame%4*300,(1-frame/4)*350,300,350,tile.GetPixels());
            }
            sheet.Apply();File.WriteAllBytes("../tools/walk-refinement/"+prefix+"-"+name+".png",sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
        }
        camera.targetTexture=null;RenderTexture.active=null;rt.Release();UnityEngine.Object.DestroyImmediate(tile);
        UnityEngine.Object.DestroyImmediate(snapshot);
    }
    public static void Inspect()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        RetargetIdle.RestoreBindPose(model);
        var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var lines=new List<string>();
        lines.Add("duration="+clip.length);
        foreach(var name in new[]{" Pelvis"," L Thigh"," L Calf"," L Foot"," L Toe0"," R Thigh"," R Calf"," R Foot"," R Toe0"})
            lines.Add("REST "+name+" "+bones[name].position.ToString("F4"));
        for(int i=0;i<12;i++)
        {
            clip.SampleAnimation(model,clip.length*i/12);
            lines.Add("FRAME "+i+" hip="+bones[" Pelvis"].position.ToString("F4"));
            foreach(var side in new[]{" L"," R"})
            {
                var thigh=bones[side+" Thigh"];var knee=bones[side+" Calf"];var foot=bones[side+" Foot"];var toe=bones[side+" Toe0"];
                float bend=180-Vector3.Angle(thigh.position-knee.position,foot.position-knee.position);
                var forward=toe.position-foot.position;
                lines.Add(side+" knee="+knee.position.ToString("F4")+" foot="+foot.position.ToString("F4")+" bend="+bend+" toeYaw="+Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg);
            }
        }
        Directory.CreateDirectory("../tools/walk-refinement");File.WriteAllLines("../tools/walk-refinement/before.txt",lines);
        UnityEngine.Object.DestroyImmediate(model);Debug.Log("WALK_INSPECTED");
    }
}
