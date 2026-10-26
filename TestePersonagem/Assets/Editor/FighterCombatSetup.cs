using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class FighterCombatSetup
{
    const string ClipPath = "Assets/Resources/GangsterMusculo/Animations/Punching.fbx";
    const string ControllerPath = "Assets/Animations/Locomocao.controller";
    const string MaskPath = "Assets/Animations/SocoParteSuperior.mask";
    const string LayerName = "SocoParteSuperior";

    [MenuItem("Horizonte/Validar soco")]
    public static void Validate()
    {
        AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport);
        var clip = LoadClip();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(GangsterMusculoSetup.ModelPath);
        if (!source) throw new Exception("Modelo Gangster Musculo ausente");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (!animator || !animator.avatar || !animator.avatar.isHuman)
                throw new Exception("Avatar Humanoid do personagem inválido");
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!rightHand || !leftHand) throw new Exception("Mãos ausentes no avatar");
            clip.SampleAnimation(model, 0f);
            var rightStart = rightHand.position;
            var leftStart = leftHand.position;
            float travel = 0f;
            for (int sample = 1; sample < 10; sample++)
            {
                clip.SampleAnimation(model, clip.length * sample / 10f);
                travel = Mathf.Max(travel, Vector3.Distance(rightStart, rightHand.position),
                    Vector3.Distance(leftStart, leftHand.position));
            }
            if (travel < .1f) throw new Exception("Punching não move a mão após retargeting");
            Debug.Log("PUNCHING_READY length=" + clip.length + " handTravel=" + travel);
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }

    [MenuItem("Horizonte/Configurar soco")]
    public static void Apply()
    {
        Validate();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) throw new Exception("Controlador de locomoção ausente");
        var baseMachine = controller.layers[0].stateMachine;
        foreach (var oldName in new[] { "Soco", "SocoCurto", "SocoReverso", "ChuteBaixo", "ChuteTraseiro" })
        {
            var oldState = Array.Find(baseMachine.states, child => child.state.name == oldName).state;
            if (oldState) baseMachine.RemoveState(oldState);
        }
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (!mask)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
        foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                     AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                     AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
            mask.SetHumanoidBodyPartActive(part, true);
        EditorUtility.SetDirty(mask);
        var layerIndex = Array.FindIndex(controller.layers, candidate => candidate.name == LayerName);
        if (layerIndex < 0)
        {
            controller.AddLayer(LayerName);
            layerIndex = Array.FindIndex(controller.layers, candidate => candidate.name == LayerName);
        }
        var layers = controller.layers;
        var layer = layers[layerIndex];
        if (!layer.stateMachine)
        {
            var newMachine = new AnimatorStateMachine { name = LayerName };
            AssetDatabase.AddObjectToAsset(newMachine, controller);
            layer.stateMachine = newMachine;
        }
        layer.avatarMask = mask;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f;
        layers[layerIndex] = layer;
        controller.layers = layers;
        var machine = layer.stateMachine;
        var state = Array.Find(machine.states, child => child.state.name == "Soco").state;
        if (!state) state = machine.AddState("Soco");
        state.motion = LoadClip();
        state.mirror = false;
        state.speed = 1f;
        state.writeDefaultValues = true;
        machine.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("PUNCHING_CONTROLLER_READY");
    }

    static AnimationClip LoadClip()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
            .FirstOrDefault(candidate => candidate.length > .15f && candidate.humanMotion &&
                !candidate.name.StartsWith("__preview", StringComparison.Ordinal));
        if (!clip) throw new Exception("Punching Humanoid ausente: " + ClipPath);
        return clip;
    }
}
