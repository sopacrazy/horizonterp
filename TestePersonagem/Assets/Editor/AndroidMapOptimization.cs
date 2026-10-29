using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class AndroidMapOptimization
{
    const string Root="Assets/GTASanAndreasModFull";
    public static void OptimizeAndBuild()
    {
        LosSantosAndroidSetup.Prepare();
        string backup="../tools/android-map-optimization/backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup);
        int textures=0,models=0;
        string[] used=AssetDatabase.GetDependencies(GameUpdateSetup.Scenes,true);
        var collisionModels=new HashSet<string>(StringComparer.Ordinal);
        foreach(string scene in GameUpdateSetup.Scenes)
            foreach(Match block in Regex.Matches(File.ReadAllText(scene),@"(?ms)^MeshCollider:\r?\n.*?(?=^---|\z)"))
            {
                var mesh=Regex.Match(block.Value,@"m_Mesh:.*?guid: ([a-f0-9]{32})");
                if(mesh.Success)collisionModels.Add(AssetDatabase.GUIDToAssetPath(mesh.Groups[1].Value));
            }
        Directory.CreateDirectory("../tools/android-map-optimization");
        File.WriteAllLines("../tools/android-map-optimization/dependencies.txt",used);
        Debug.Log("ANDROID_MAP_DEPENDENCIES "+used.Length);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(string path in used.Where(p=>p.StartsWith(Root+"/",StringComparison.Ordinal)))
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(!importer)continue;
                var settings=importer.GetPlatformTextureSettings("Android");
                if(settings.overridden&&settings.maxTextureSize==512&&settings.format==TextureImporterFormat.ASTC_8x8)continue;
                Backup(path,backup);
                settings.name="Android";settings.overridden=true;settings.maxTextureSize=512;
                settings.format=TextureImporterFormat.ASTC_8x8;settings.compressionQuality=50;
                settings.textureCompression=TextureImporterCompression.Compressed;
                importer.SetPlatformTextureSettings(settings);importer.SaveAndReimport();textures++;
            }
            foreach(string path in used.Where(p=>p.StartsWith(Root+"/ImportedModels/",StringComparison.Ordinal)))
            {
                if(collisionModels.Contains(path))continue;
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(!importer||importer.meshCompression==ModelImporterMeshCompression.Low)continue;
                Backup(path,backup);
                importer.meshCompression=ModelImporterMeshCompression.Low;
                importer.optimizeMeshPolygons=true;importer.optimizeMeshVertices=true;
                importer.SaveAndReimport();models++;
            }
        }
        finally{AssetDatabase.StopAssetEditing();}
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../tools/android-map-optimization");
        File.WriteAllText("../tools/android-map-optimization/result.txt","Android texture overrides: ASTC 8x8, max 512\nTextures changed="+textures+"\nVisual meshes changed="+models+"\nCollider import settings preserved\nBackup="+Path.GetFullPath(backup));
        Debug.Log("ANDROID_MAP_OPTIMIZED textures="+textures+" models="+models);
        Build();
    }
    static void Backup(string asset,string folder)
    {
        string destination=Path.Combine(folder,asset+".meta");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(asset+".meta",destination,false);
    }
    public static void Build()
    {
        Hud2Setup.Prepare();
        AndroidSetup.Build();
    }
}
