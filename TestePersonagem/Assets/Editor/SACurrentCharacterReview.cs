using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SACurrentCharacterReview
{
    const string Report = "../tools/animation-work/sa-current-character-report.txt";
    static SACurrentCharacterReview() { EditorApplication.delayCall += AutoReview; }
    static void AutoReview()
    {
        if (File.Exists(Report) && File.ReadAllText(Report).Contains("SA_CURRENT_CHARACTER_OK_V6")) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            !File.Exists("../tools/animation-work/sa-unity-report.txt") || !File.ReadAllText("../tools/animation-work/sa-unity-report.txt").Contains("SA_CHARACTER_READY_V9"))
        { EditorApplication.delayCall += AutoReview; return; }
        try { Review(); }
        catch (Exception e)
        {
            File.WriteAllText("../tools/animation-work/sa-current-character-error.txt", e.ToString());
            Debug.LogException(e);
        }
    }

    [MenuItem("Horizonte/Personagem SA/Conferir animacoes no personagem atual")]
    public static void Review()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var visualType = typeof(GangsterPlayerVisual);
        string modelPath = (string)visualType.GetField("MalePath", flags).GetRawConstantValue();
        if (modelPath != "GangsterMusculo/GangsterMusculo") throw new InvalidOperationException("Modelo padrao foi alterado.");
        // The imported glasses are a head accessory, not a humanoid eye.
        // Mapping their transform as LeftEye lets retargeting displace them.
        var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx");
        var description = importer.humanDescription;
        if (description.human.Any(b => b.boneName == "Raybans"))
        {
            description.human = description.human.Where(b => b.boneName != "Raybans").ToArray();
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }
        var baseController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
        var controller = (AnimatorOverrideController)visualType.GetMethod("CreateSaPreviewController", flags).Invoke(null, new object[] { baseController });
        var idle = Resources.LoadAll<AnimationClip>("Characters/Wuzimu/Animations/ParadoSA").First(c => c.name == "ParadoSA" && c.humanMotion);
        var walk = Resources.LoadAll<AnimationClip>("Characters/Wuzimu/Animations/AndarSA").First(c => c.name == "AndarSA" && c.humanMotion);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var light = new GameObject("Luz de revisao").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Referencia do chao y=0";
            var root = new GameObject("Personagem atual - mesmas malhas e texturas");
            var model = GangsterPlayerVisual.CreateRemoteVisual("male", controller, root.transform);
            var animator = model.GetComponent<Animator>();
            animator.Rebind();
            var setupType = typeof(SACharacterLocomotionSetup);
            var sample = setupType.GetMethod("Sample", flags);
            PlayableGraph Sample(AnimationClip clip, float time) => (PlayableGraph)sample.Invoke(null, new object[] { animator, clip, time });
            var graph = Sample(idle, .3f);
            var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float armsDown = Vector3.Dot((hand.position - shoulder.position).normalized, Vector3.down);
            graph.Destroy();
            if (armsDown < .85f) throw new InvalidOperationException("Parado com bracos elevados: " + armsDown);
            var first = Sample(walk, .1f);
            var thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rotation = thigh.localRotation; first.Destroy();
            var second = Sample(walk, .65f);
            float delta = Quaternion.Angle(rotation, thigh.localRotation); second.Destroy();
            if (delta < 5) throw new InvalidOperationException("Caminhada sem movimento: " + delta);
            var render = setupType.GetMethod("ReviewPose", flags);
            render.Invoke(null, new object[] { animator, idle, "sa-current-idle.png" });
            render.Invoke(null, new object[] { animator, walk, "sa-current-walk.png" });
            foreach (var name in new[] { "CorrerSA", "PuloSA" })
            {
                var clip = Resources.LoadAll<AnimationClip>("Characters/Wuzimu/Animations/" + (name == "PuloSA" ? "PuloSA_Personagem" : name)).First(c => c.name == name && c.humanMotion);
                render.Invoke(null, new object[] { animator, clip, "sa-current-" + name + ".png" });
            }
            File.WriteAllText(Report, "SA_CURRENT_CHARACTER_OK_V6\nModel=" + modelPath +
                "\nIdle=" + idle.name + "\nWalk=" + walk.name + "\nIdleArmsDown=" + armsDown +
                "\nWalkLegMotionDegrees=" + delta + "\nSkinChanged=false\n");
            Debug.Log("SA_CURRENT_CHARACTER_OK - mesmo personagem, novos clipes de parado e caminhada");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            UnityEngine.Object.DestroyImmediate(controller);
        }
    }
}
