using UnityEngine;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class LosSantosRepairBatch
{
    public static void ApplyCollisionAndVerify()
    {
        LosSantosRepairVerification.Targeted();
        LosSantosSurfaceCollisionRepair.ApplyAndVerify();
        LosSantosCollisionPatchCatalog.Capture();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
        LosSantosStreamingVerification.Verify();
        Debug.Log("LOS_SANTOS_COLLISION_BATCH_VERIFIED");
    }

    public static void ApplyGeometryAndReview()
    {
        LosSantosGeometryRepair.ApplyAndVerify();
        LosSantosGeometryRepair.VerifyExisting();
        LosSantosMarketCollisionRepair.ApplyAndVerify();
        string marketScene = "Assets/Scenes/GTASanAndreasModFullSectors/GTASASector_p9_m7.unity";
        byte[] marketSaved = File.ReadAllBytes(marketScene);
        File.Copy("../tools/los-santos-repair/market-collision-repair.json", "../tools/los-santos-repair/market-collision-repair-first-pass.json", true);
        LosSantosMarketCollisionRepair.ApplyAndVerify();
        if (!marketSaved.SequenceEqual(File.ReadAllBytes(marketScene)))
            throw new Exception("Repetir o reparo do mercado modificou a cena.");
        LosSantosCollisionPatchCatalog.Capture();
        EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
        LosSantosStreamingVerification.Verify();
        LosSantosWalkingVerification.Verify();
        WriteSyncPaths();
        LosSantosGroundAudit.Run();
        LosSantosVisualReview.Capture();
        Debug.Log("LOS_SANTOS_REPAIR_AND_REVIEW_COMPLETE");
    }

    public static void WriteSyncPaths()
    {
        string[] folders = { "Assets/GTASanAndreasModFull/GroundRepairs", "Assets/GTASanAndreasModFull/CollisionRepairs" };
        string[] files = folders.Where(Directory.Exists).SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            .Select(p => p.Replace('\\', '/')).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).ToArray();
        var paths = AssetDatabase.GetDependencies(files, true).Concat(files)
            .Concat(LosSantosAndroidSetup.SectorPaths)
            .Concat(Directory.GetFiles("Assets/Editor", "LosSantos*.cs"))
            .Concat(new[] { "Assets/Editor/GtaSaModTestSetup.cs", "Assets/Scripts/ThirdPersonPlayer.cs",
                "Assets/Scripts/ThirdPersonPlayer.Streaming.cs", "Assets/Scripts/GtaSaSectorStreaming.cs",
                GameUpdateSetup.GameScene, LosSantosAndroidSetup.Scene });
        File.WriteAllLines("../tools/los-santos-repair/android-sync-paths.txt", paths
            .Select(p => p.Replace('\\', '/')).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p))
            .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal));
    }
}
