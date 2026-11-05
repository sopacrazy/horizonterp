using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Collections.Generic;

[InitializeOnLoad]
public static class SACharacterLocomotionSetup
{
    const string Model = "Assets/Resources/Characters/Wuzimu/Wuzimu.fbx";
    const string Folder = "Assets/Resources/Characters/Wuzimu";
    const string Controller = "Assets/Animations/SACharacterTest.controller";
    const string TestScene = "Assets/Scenes/TestePersonagemSA.unity";
    const string Report = "../tools/animation-work/sa-unity-report.txt";
    const float WalkSpeed = 1.3756f;
    static bool busy;
    static SACharacterLocomotionSetup() { EditorApplication.delayCall += AutoPrepare; }

    static void AutoPrepare()
    {
        if ((File.Exists(Report) && File.ReadAllText(Report).Contains("SA_CHARACTER_READY_V9")) || busy) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += AutoPrepare; return; }
        if (new[] { "ParadoSA", "AndarSA", "CorrerSA", "PuloSA" }.Any(name => !File.Exists(Folder + "/Animations/" + name + ".fbx"))) return;
        try { Prepare(); }
        catch (Exception e)
        {
            Directory.CreateDirectory("../tools/animation-work");
            File.WriteAllText("../tools/animation-work/sa-unity-error.txt", e.ToString());
            Debug.LogException(e);
        }
    }

    [MenuItem("Horizonte/Personagem SA/Preparar personagem e animacoes")]
    public static void Prepare()
    {
        if (busy) return;
        busy = true;
        try
        {
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(Model);
            modelImporter.animationType = ModelImporterAnimationType.Human;
            modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            modelImporter.importAnimation = false;
            modelImporter.importCameras = modelImporter.importLights = false;
            modelImporter.isReadable = true;
            modelImporter.bakeAxisConversion = true;
            var standard = (ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx");
            var description = standard.humanDescription;
            description.human = description.human.Where(b => b.boneName != "Raybans").ToArray();
            description.skeleton = Array.Empty<SkeletonBone>();
            modelImporter.humanDescription = description;
            ApplyMaterials(modelImporter);
            modelImporter.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            if (!avatar) throw new InvalidOperationException("Avatar SA Humanoid invalido.");
            var idle = ImportClip("ParadoSA");
            var walk = ImportClip("AndarSA");
            var run = ImportClip("CorrerSA");
            var jump = GroundJumpForCurrentCharacter(ImportClip("PuloSA"));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(Controller);
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var state = controller.layers[0].stateMachine.AddState("Locomocao");
                controller.layers[0].stateMachine.defaultState = state;
                var tree = new BlendTree { name = "SA Parado e Andar", blendType = BlendTreeType.Simple1D,
                    blendParameter = "Speed", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(idle, 0);
                tree.AddChild(walk, WalkSpeed);
                state.motion = tree;
            }
            else
            {
                var tree = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
                var children = tree.children;
                children[0].motion = idle; children[1].motion = walk;
                tree.children = children;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            VerifyLocalPreviewController();
            MakeTestScene(controller, idle, walk);
            File.WriteAllText(Report, "SA_CHARACTER_READY_V9\nModel=" + Model + "\nIdle=" + idle.name + "\nWalk=" + walk.name +
                "\nRun=" + run.name + "\nJump=" + jump.name +
                "\nAvatar=Humanoid valid\nDefaultSkin=unchanged\nLocalTestCommand=/skin sa\nTestScene=" + TestScene + "\n");
            Debug.Log("SA_CHARACTER_READY - ParadoSA / AndarSA / CorrerSA / PuloSA");
        }
        finally { busy = false; }
    }

    static void ApplyMaterials(ModelImporter importer)
    {
        string materialFolder = Folder + "/Materials";
        Directory.CreateDirectory(materialFolder);
        foreach (string name in new[] { "bota01", "calca01", "camisa01", "corpo01", "rosto01", "coldre01" })
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + name + ".png");
            if (!texture) throw new InvalidOperationException("Textura SA ausente: " + name);
            string path = materialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = texture;
            material.color = Color.white;
            material.SetFloat("_Glossiness", .15f);
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
        }
    }

    static AnimationClip ImportClip(string name)
    {
        string path = Folder + "/Animations/" + name + ".fbx";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        // Each exported FBX has its own axis-converted bind hierarchy. Copying
        // the pre-export Avatar treats those frames as identical and produces
        // incorrect Humanoid body/effector translations. Build from this FBX.
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        var description = ((ModelImporter)AssetImporter.GetAtPath(Model)).humanDescription;
        description.skeleton = Array.Empty<SkeletonBone>();
        description.hasTranslationDoF = false;
        importer.humanDescription = description;
        importer.importAnimation = true;
        importer.importCameras = importer.importLights = false;
        importer.bakeAxisConversion = true;
        var take = importer.defaultClipAnimations.First();
        take.name = name;
        take.loopTime = take.loopPose = name != "PuloSA";
        take.lockRootRotation = take.lockRootHeightY = take.lockRootPositionXZ = true;
        // Use Humanoid body forward (+Z), rather than the FBX's original -Z.
        take.keepOriginalOrientation = false;
        take.keepOriginalPositionY = take.keepOriginalPositionXZ = true;
        take.heightFromFeet = false;
        importer.clipAnimations = new[] { take };
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name && c.humanMotion);
        if (!clip) throw new InvalidOperationException("Clipe Humanoid SA ausente: " + name);
        return clip;
    }

    static AnimationClip GroundJumpForCurrentCharacter(AnimationClip source)
    {
        const string path = Folder + "/Animations/PuloSA_Personagem.anim";
        var clip = UnityEngine.Object.Instantiate(source);
        clip.name = "PuloSA";
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject("Calibracao de contato do pulo");
            var visual = GangsterPlayerVisual.CreateRemoteVisual("male", null, root.transform);
            var animator = visual.GetComponent<Animator>();
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            var original = AnimationUtility.GetEditorCurve(source, binding);
            if (original == null) throw new InvalidOperationException("Pulo sem curva Humanoid RootT.y");
            float Lowest(float time)
            {
                var graph = Sample(animator, clip, time);
                try
                {
                    float value = float.MaxValue;
                    foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh = new Mesh();
                        try { skin.BakeMesh(mesh); foreach (var v in mesh.vertices) value = Mathf.Min(value, skin.transform.TransformPoint(v).y); }
                        finally { UnityEngine.Object.DestroyImmediate(mesh); }
                    }
                    return value;
                }
                finally { graph.Destroy(); }
            }
            float baseline = Lowest(0);
            var shifted = new AnimationCurve(original.keys.Select(k => new Keyframe(k.time, k.value + .05f)).ToArray());
            AnimationUtility.SetEditorCurve(clip, binding, shifted);
            float response = (Lowest(0) - baseline) / .05f;
            if (response < .1f) throw new InvalidOperationException("Calibracao de altura Humanoid sem resposta: " + response);
            AnimationUtility.SetEditorCurve(clip, binding, original);
            var keys = new List<Keyframe>();
            int count = Mathf.CeilToInt(source.length * 60);
            for (int i = 0; i <= count; i++)
            {
                float time = source.length * i / count;
                float correction = Mathf.Max(0, -.012f - Lowest(time)) / response;
                keys.Add(new Keyframe(time, original.Evaluate(time) + correction));
            }
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing) { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
            else AssetDatabase.CreateAsset(clip, path);
            clip.name="PuloSA";
            EditorUtility.SetDirty(clip);
            return clip;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
    }

    static void MakeTestScene(RuntimeAnimatorController controller, AnimationClip idle, AnimationClip walk)
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Chao de teste"; floor.transform.localScale = Vector3.one * 3;
            var light = new GameObject("Luz").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var root = new GameObject("Jogador SA - teste");
            var body = root.AddComponent<CharacterController>();
            body.height = 1.8f; body.center = Vector3.up * .9f; body.radius = .28f; body.skinWidth = .03f;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            var animator = visual.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var initialGraph = Sample(animator, idle, 0);
            initialGraph.Destroy();
            CharacterSkinSizing.Match(visual, 1.8f, 0);
            var firstGraph = Sample(animator, walk, .12f);
            var leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var firstRotation = leg.localRotation; firstGraph.Destroy();
            var secondGraph = Sample(animator, walk, .65f);
            float motion = Quaternion.Angle(firstRotation, leg.localRotation); secondGraph.Destroy();
            if (motion < 5) throw new InvalidOperationException("A caminhada SA nao movimentou a perna: " + motion);
            var restoreGraph = Sample(animator, idle, 0); restoreGraph.Destroy();
            Debug.Log("SA_WALK_MOTION_OK legDegrees=" + motion);
            var camera = new GameObject("Camera de teste").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.65f, -3.7f);
            camera.transform.LookAt(Vector3.up * 1.05f);
            camera.gameObject.AddComponent<AudioListener>();
            var movement = root.AddComponent<SACharacterWalkTest>();
            movement.animator = animator; movement.followCamera = camera; movement.walkSpeed = WalkSpeed;
            EditorSceneManager.SaveScene(scene, TestScene);
            ReviewPose(animator, idle, "sa-idle-game.png");
            ReviewPose(animator, walk, "sa-walk-game.png");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
    }

    static void ReviewPose(Animator animator, AnimationClip clip, string filename)
        => ReviewPoseAt(animator, clip, filename, clip.length * .35f);

    static void ReviewPoseAt(Animator animator, AnimationClip clip, string filename, float time)
    {
        var graph = Sample(animator, clip, time);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (Vector3.Dot((head.position - hips.position).normalized, Vector3.up) < .8f)
            throw new InvalidOperationException("Postura SA invalida no clipe " + clip.name);
        var camera = new GameObject("Camera de revisao").AddComponent<Camera>();
        var center = BakedWorldBounds(animator).center;
        camera.transform.position = center + new Vector3(2.7f, 0.6f, 4);
        camera.transform.LookAt(center);
        camera.orthographic = true; camera.orthographicSize = 1.15f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f, .16f, .19f);
        var texture = new RenderTexture(640, 640, 24);
        var pixels = new Texture2D(640, 640, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
        var frozen = new List<GameObject>();
        var meshes = new List<Mesh>();
        try
        {
            foreach (var skin in skins)
            {
                var mesh = new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                var snapshot = new GameObject("Pose congelada");
                snapshot.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                snapshot.transform.localScale = skin.transform.lossyScale;
                snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                frozen.Add(snapshot); skin.enabled = false;
            }
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply();
            File.WriteAllBytes("../tools/animation-work/" + filename, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach (var obj in frozen) UnityEngine.Object.DestroyImmediate(obj);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var skin in skins) if (skin) skin.enabled = true;
            graph.Destroy();
        }
    }

    static PlayableGraph Sample(Animator animator, AnimationClip clip, float time)
    {
        var graph = PlayableGraph.Create("Revisao personagem SA");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        var output = AnimationPlayableOutput.Create(graph, "SA", animator);
        output.SetSourcePlayable(playable);
        graph.Play(); playable.SetTime(time); graph.Evaluate(0);
        return graph;
    }

    static Bounds BakedWorldBounds(Animator animator)
    {
        bool found = false;
        var result = new Bounds();
        foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                var bounds = mesh.bounds;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            var point = skin.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x,y,z)));
                            if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                            else result.Encapsulate(point);
                        }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        if (!found) throw new InvalidOperationException("Personagem sem geometria para revisao.");
        return result;
    }

    static void VerifyLocalPreviewController()
    {
        var original = new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller"));
        AnimatorOverrideController preview = null;
        try
        {
            var method = typeof(GangsterPlayerVisual).GetMethod("CreateSaPreviewController",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            preview = (AnimatorOverrideController)method.Invoke(null, new object[] { original });
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            preview.GetOverrides(clips);
            foreach (var pair in new[] { new[] { "IdleRetarget", "ParadoSA" }, new[] { "Andar_WalkingOriginal", "AndarSA" },
                new[] { "Correr_FastRunEnviado", "CorrerSA" }, new[] { "JumpRetarget", "PuloSA" } })
                if (!clips.Any(c => c.Key && c.Key.name == pair[0] && c.Value && c.Value.name == pair[1]))
                    throw new InvalidOperationException("Troca local de animacao SA invalida: " + pair[0]);
            Debug.Log("SA_LOCAL_PREVIEW_CONTROLLER_OK");
        }
        finally
        {
            if (preview) UnityEngine.Object.DestroyImmediate(preview);
            UnityEngine.Object.DestroyImmediate(original);
        }
    }

    [MenuItem("Horizonte/Personagem SA/Abrir cena de teste")]
    public static void OpenTest()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(TestScene)) Prepare();
        EditorSceneManager.OpenScene(TestScene);
    }
}
