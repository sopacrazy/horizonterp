using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArrivalMissionSetup
{
    public static void Prepare()
    {
        Directory.CreateDirectory("Assets/Resources/Arrival");
        File.Copy("../Backend/api/arrival-config.json","Assets/Resources/Arrival/Config.json",true);
        const string bannerPath="Assets/Resources/Arrival/DialogBanner.png";
        File.Copy("../personagem/banner.png",bannerPath,true);
        AssetDatabase.ImportAsset(bannerPath,ImportAssetOptions.ForceUpdate);
        var bannerImporter=AssetImporter.GetAtPath(bannerPath) as TextureImporter;
        if(bannerImporter!=null&&(bannerImporter.mipmapEnabled||!bannerImporter.alphaIsTransparency||bannerImporter.npotScale!=TextureImporterNPOTScale.None||bannerImporter.maxTextureSize<4096))
        {
            bannerImporter.mipmapEnabled=false;bannerImporter.alphaIsTransparency=true;bannerImporter.npotScale=TextureImporterNPOTScale.None;bannerImporter.maxTextureSize=4096;bannerImporter.SaveAndReimport();
        }
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GTARecoveredMap/Mesh/SkyCarBody.asset");
        if(mesh&&!File.Exists("Assets/Resources/Arrival/CarBody.asset"))AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(mesh),"Assets/Resources/Arrival/CarBody.asset");
        var details=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GTARecoveredMap/Mesh/SkyCarComponents.asset");
        if(details&&!File.Exists("Assets/Resources/Arrival/CarDetails.asset"))AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(details),"Assets/Resources/Arrival/CarDetails.asset");
        AssetDatabase.Refresh();
    }
    [Serializable]class Point{public float x,y,z;}
    [Serializable]class Survey{public List<Point> points=new List<Point>();}
    public static void SurveyGround()
    {
        EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
        var streaming=UnityEngine.Object.FindAnyObjectByType<CitySectorStreaming>();
        foreach(var sector in streaming.sectors.Where(s=>s.bounds.SqrDistance(Vector3.zero)<160*160))
        {
            string path="Assets/Scenes/CitySectors/"+sector.scene+".unity";
            EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        }
        Physics.SyncTransforms();var report=new Survey();
        foreach(float x in new[]{-80f,-60,-40,-20,-10,0,5,10,20,40,60,80})
        foreach(float z in new[]{-8f,-4,0,4,8})
        {
            var ray=new Vector3(x,8,z);
            if(!Physics.Raycast(ray,Vector3.down,out var hit,20,1,QueryTriggerInteraction.Ignore)||hit.normal.y<.8f)continue;
            var point=hit.point+Vector3.up*.08f;
            if(Physics.CheckCapsule(point+Vector3.up*.6f,point+Vector3.up*1.5f,.5f,1,QueryTriggerInteraction.Ignore))continue;
            report.points.Add(new Point{x=point.x,y=point.y,z=point.z});
        }
        Directory.CreateDirectory("../tools/arrival-review");File.WriteAllText("../tools/arrival-review/ground-survey.json",JsonUtility.ToJson(report,true));
        Directory.CreateDirectory("Assets/Resources/Arrival");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GTARecoveredMap/Mesh/SkyCarBody.asset");
        if(mesh&&!File.Exists("Assets/Resources/Arrival/CarBody.asset"))AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(mesh),"Assets/Resources/Arrival/CarBody.asset");
        AssetDatabase.SaveAssets();Debug.Log("ARRIVAL_SURVEY_OK points="+report.points.Count);
    }
}
