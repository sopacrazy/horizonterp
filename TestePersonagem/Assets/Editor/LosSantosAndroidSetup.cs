using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A reversible distribution profile. Full-map scenes/assets remain unchanged.
public static class LosSantosAndroidSetup
{
    public const string Scene="Assets/Scenes/AndroidLosSantos/TestePersonagem.unity";
    public const string Boot="Assets/Scenes/AndroidLosSantos/Entrada.unity";
    public const string Marker="Assets/Scenes/LosSantosAndroid.enabled";
    const string Sectors="Assets/Scenes/GTASanAndreasModFullSectors";
    public static string[] SectorPaths => Directory.GetFiles(Sectors,"*.unity")
        .Where(p=>Selected(Path.GetFileNameWithoutExtension(p))).OrderBy(p=>p)
        .Select(p=>p.Replace('\\','/')).ToArray();
    public static string[] Scenes => new[]{Boot,Scene}.Concat(SectorPaths).ToArray();
    public static bool Selected(string name)
    {
        var m=Regex.Match(name,@"^GTASASector_([mp])(\d+)_([mp])(\d+)$");
        if(!m.Success)return false;
        int x=int.Parse(m.Groups[2].Value)*(m.Groups[1].Value=="m"?-1:1);
        int y=int.Parse(m.Groups[4].Value)*(m.Groups[3].Value=="m"?-1:1);
        // City rectangle plus one neighbouring tile for crossing ground meshes.
        return x>=-2&&x<=12&&y>=-13&&y<=-2;
    }
    public static void Prepare()
    {
        string backup="../tools/android-los-santos/backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(Path.GetDirectoryName(Scene));
        File.Copy("ProjectSettings/EditorBuildSettings.asset",backup+"/EditorBuildSettings.asset",false);
        var scene=EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
        var streaming=UnityEngine.Object.FindAnyObjectByType<GtaSaSectorStreaming>();
        var map=UnityEngine.Object.FindAnyObjectByType<NeighborhoodMap>();
        if(!streaming||!map)throw new Exception("Mapa San Andreas integrado ausente");
        streaming.sectors=streaming.sectors.Where(s=>Selected(s.scene)).ToArray();
        if(streaming.sectors.Length<20||streaming.sectors.Length>=300)throw new Exception("Seleção de setores Los Santos inválida");
        if(streaming.sectors.Any(s=>!File.Exists(Sectors+"/"+s.scene+".unity")))throw new Exception("Setor selecionado ausente");
        map.restrictRegion=true;map.regionMin=new Vector2(-256,512);map.regionMax=new Vector2(3072,3072);
        map.displayName="Los Santos · teste";
        var boundary=map.gameObject.GetComponent<MapRegionBoundary>()??map.gameObject.AddComponent<MapRegionBoundary>();
        boundary.player=streaming.target.GetComponent<ThirdPersonPlayer>();
        var preview=UnityEngine.Object.FindAnyObjectByType<GtaSaMapPreview>();
        if(preview)
        {
            preview.regionalReview=true;
            preview.destinations=preview.destinations.Where(d=>map.ContainsXZ(d.position)).ToArray();
            preview.reviewDirectory=Path.GetFullPath("../tools/android-los-santos/review");
        }
        if(!map.ContainsXZ(map.SpawnPosition))throw new Exception("Spawn fora de Los Santos");
        int selectedCount=streaming.sectors.Length;
        EditorSceneManager.SaveScene(scene,Scene);
        var boot=EditorSceneManager.OpenScene(GameUpdateSetup.BootScene);
        var loader=UnityEngine.Object.FindAnyObjectByType<StartupLoader>();
        if(!loader)throw new Exception("Entrada ausente");
        loader.cityScene=Path.GetFileNameWithoutExtension(Scene);
        EditorSceneManager.SaveScene(boot,Boot);
        File.WriteAllText(Marker,"Android distribution: Los Santos only. Full source map preserved.");
        AssetDatabase.Refresh();
        EditorBuildSettings.scenes=Scenes.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
        AssetDatabase.SaveAssets();
        File.WriteAllLines("../tools/android-los-santos/sectors.txt",SectorPaths);
        Debug.Log("LOS_SANTOS_PROFILE_OK sectors="+selectedCount+" fullMapPreserved=true");
    }
}
