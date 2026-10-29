using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SanAndreasIntegration
{
    public static void ApplyAndBuild()
    {
        string backup = "../tools/map-integration/backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { GameUpdateSetup.GameScene, GameUpdateSetup.GameScene + ".meta",
            "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
        File.WriteAllText("../tools/map-integration/latest-backup.txt", Path.GetFullPath(backup));
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GTASanAndreasModFullTeste.unity");
        var preview = UnityEngine.Object.FindAnyObjectByType<GtaSaMapPreview>();
        if (!preview) throw new Exception("Prévia ausente");
        preview.integratedReview = true;
        preview.reviewDirectory = Path.GetFullPath("../tools/map-integration/review");
        var entry = preview.gameObject.AddComponent<IntegratedMapEntry>();
        entry.player = preview.player; entry.streaming = preview.streaming;
        var map = UnityEngine.Object.FindAnyObjectByType<NeighborhoodMap>();
        map.isOfficialCity = true;
        map.displayName = "San Andreas · integração em avaliação";
        EditorSceneManager.SaveScene(scene, GameUpdateSetup.GameScene);
        File.WriteAllText("Assets/Scenes/SanAndreasIntegration.enabled", "Local review only. Android untested; rural and San Fierro visual defects remain.");
        AssetDatabase.Refresh();
        EditorBuildSettings.scenes = GameUpdateSetup.Scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        AssetDatabase.SaveAssets();
        Build();
        Debug.Log("SA_INTEGRATION_BACKUP=" + Path.GetFullPath(backup));
    }
    public static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = GameUpdateSetup.Scenes,
            locationPathName = "Builds/SanAndreasIntegracao/HorizonteIntegracao.exe", target = BuildTarget.StandaloneWindows64 });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Falha na build de avaliação");
        Debug.Log("SA_INTEGRATION_BUILD_OK");
    }
}
