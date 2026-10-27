using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ShootingRangeSetup
{
    const string ClipPath = "Assets/Resources/GangsterMusculo/Animations/Shooting.fbx";
    const string ControllerPath = "Assets/Animations/Locomocao.controller";
    const string LayerName = "SocoParteSuperior";
    const string SpeedParameter = "ShootingSpeed";

    [MenuItem("Horizonte/Configurar treino de tiro")]
    public static void Apply()
    {
        AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
            .FirstOrDefault(candidate => candidate && candidate.length > .15f && candidate.humanMotion &&
                !candidate.name.StartsWith("__preview", StringComparison.Ordinal));
        if (!clip) throw new Exception("Animação Shooting Humanoid ausente: " + ClipPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) throw new Exception("Controlador de locomoção ausente");
        int layerIndex = Array.FindIndex(controller.layers, layer => layer.name == LayerName);
        if (layerIndex < 0) throw new Exception("Camada superior ausente. Execute Horizonte/Configurar soco.");
        var machine = controller.layers[layerIndex].stateMachine;
        var state = Array.Find(machine.states, child => child.state.name == "Shooting").state;
        if (!state) state = machine.AddState("Shooting");
        if (!controller.parameters.Any(parameter => parameter.name == SpeedParameter))
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
        state.motion = clip;
        state.speed = 1f;
        state.speedParameterActive = true;
        state.speedParameter = SpeedParameter;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("SHOOTING_RANGE_ANIMATION_READY length=" + clip.length);
    }

    public static void BuildWindowsTest()
    {
        Apply();
        Directory.CreateDirectory("Builds/ShootingRangeTest");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes=GameUpdateSetup.Scenes,
            locationPathName="Builds/ShootingRangeTest/HorizonteShootingTest.exe",
            target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.None
        });
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("Build de teste falhou: "+report.summary.result);
        Debug.Log("SHOOTING_RANGE_WINDOWS_BUILD_OK");
    }
}
