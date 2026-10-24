using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PrototypeSetup
{
    const string ModelPath = "Assets/Personagem/ByBeyby.fbx";
    const string ScenePath = "Assets/Scenes/TestePersonagem.unity";
    static string[] textureNames = { "beyby_maoo", "beyby_cass", "beyby_nfcc", "beyby_negaa", "beyby_calc", "beyby_trnsd" };

    [MenuItem("Teste/Preparar cena e animacoes")]
    public static void Create()
    {
        foreach (string dir in new[] { "Scenes", "Animations", "Materials", "Prefabs" })
            Directory.CreateDirectory("Assets/" + dir);
        AssetDatabase.Refresh();
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new Exception("O modelo FBX nao foi importado.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.62f, .67f, .74f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(.62f, .73f, .81f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 25;
        RenderSettings.fogEndDistance = 70;
        Camera camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RenderSettings.fogColor;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 120;
        camera.fieldOfView = 55;
        var sun = new GameObject("Sol", typeof(Light)).GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);
        Material ground = Material("Piso", new Color(.33f, .38f, .40f));
        Material line = Material("Marcacao", new Color(.72f, .76f, .70f));
        Material wall = Material("Blocos", new Color(.45f, .53f, .57f));
        Material edge = Material("Limites", new Color(.22f, .3f, .34f));
        Cube("Chao", new Vector3(0, -.25f, 0), new Vector3(40, .5f, 40), ground);
        for (int i = -18; i <= 18; i += 3)
        {
            Cube("Linha X " + i, new Vector3(i, .003f, 0), new Vector3(.025f, .006f, 39), line, false);
            Cube("Linha Z " + i, new Vector3(0, .004f, i), new Vector3(39, .006f, .025f), line, false);
        }
        Cube("Limite frente", new Vector3(0, 1, 20), new Vector3(41, 2, 1), edge);
        Cube("Limite tras", new Vector3(0, 1, -20), new Vector3(41, 2, 1), edge);
        Cube("Limite esquerdo", new Vector3(-20, 1, 0), new Vector3(1, 2, 40), edge);
        Cube("Limite direito", new Vector3(20, 1, 0), new Vector3(1, 2, 40), edge);
        Cube("Bloco A", new Vector3(-6, 1, 6), new Vector3(3, 2, 3), wall);
        Cube("Bloco B", new Vector3(7, 1.5f, 8), new Vector3(3, 3, 4), wall);
        Cube("Bloco C", new Vector3(-8, .5f, -7), new Vector3(4, 1, 2), wall);
        var player = new GameObject("Jogador");
        player.layer = 2;
        player.transform.position = new Vector3(0, .05f, 0);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = .26f; cc.center = new Vector3(0, .91f, 0);
        cc.stepOffset = .25f; cc.slopeLimit = 45; cc.skinWidth = .035f;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name = "Personagem";
        model.transform.SetParent(player.transform, false);
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
        // The imported skin faces +Z; the player and camera use the same forward direction.
        model.transform.localRotation = Quaternion.identity;
        var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (!renderer || !renderer.sharedMesh || renderer.bones.Length < 20)
            throw new Exception("Malha ou esqueleto incompleto no Unity.");
        renderer.updateWhenOffscreen = true;
        var materials = new Material[textureNames.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Personagem/Textures/" + textureNames[i] + ".png");
            if (!tex) throw new Exception("Textura ausente: " + textureNames[i]);
            materials[i] = Material(textureNames[i], Color.white);
            materials[i].mainTexture = tex;
            EditorUtility.SetDirty(materials[i]);
        }
        renderer.sharedMaterials = materials;
        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
        foreach (string required in new[] { " Pelvis", " Spine", " L UpperArm", " R UpperArm", " L Thigh", " R Thigh" })
            if (!bones.ContainsKey(required)) throw new Exception("Osso ausente: [" + required + "]");
        foreach (string side in new[] { " L", " R" })
        {
            Transform upper = bones[side + " UpperArm"];
            Transform elbow = bones[side + " ForeArm"];
            Vector3 current = (elbow.position - upper.position).normalized;
            Vector3 desired = new Vector3(Mathf.Sign(current.x) * .12f, -.98f, .06f).normalized;
            upper.rotation = Quaternion.FromToRotation(current, desired) * upper.rotation;
        }
        AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim");
        if (!idle) idle = MakeClip(model.transform, bones, "Parado", 2.4f, 0);
        AnimationClip walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/WalkingRetarget.anim");
        if (!walk) walk = MakeClip(model.transform, bones, "Andar", 1.05f, 1);
        AnimationClip run = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/FastRunRetarget.anim");
        if (!run) run = MakeClip(model.transform, bones, "Correr", .68f, 2);
        const string controllerPath = "Assets/Animations/Locomocao.controller";
        var previousController=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var previousTree=previousController ? previousController.layers[0].stateMachine.defaultState?.motion as BlendTree : null;
        var cadence=previousTree ? previousTree.children.Where(c=>c.motion).ToDictionary(c=>c.motion.name,c=>c.timeScale) : new Dictionary<string,float>();
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("Locomocao");
        var tree = new BlendTree { name = "Parado - Andar - Correr", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, 0); tree.AddChild(walk, 1.65f); tree.AddChild(run, 4.5f);
        var childMotions=tree.children;
        for(int i=0;i<childMotions.Length;i++)
            if(cadence.TryGetValue(childMotions[i].motion.name,out float rate))childMotions[i].timeScale=rate;
        tree.children=childMotions;
        state.motion = tree; machine.defaultState = state;
        animator.runtimeAnimatorController = controller;
        var follow = camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target = player.transform;
        camera.transform.SetPositionAndRotation(new Vector3(0, 2.27f, -4.08f), Quaternion.Euler(14, 0, 0));
        var movement = player.AddComponent<ThirdPersonPlayer>();
        movement.cameraTransform = camera.transform; movement.animator = animator;
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Jogador.prefab");
        PlayerSettings.companyName = "Adriano";
        PlayerSettings.productName = "Horizonte";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadowDistance = 30;
        QualitySettings.antiAliasing = 2;
        // This prototype deliberately uses Unity's built-in keyboard/mouse input.
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input = settings.FindProperty("activeInputHandler");
        if (input != null) { input.intValue = 0; settings.ApplyModifiedPropertiesWithoutUndo(); }
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../tools");
        File.WriteAllText("../tools/unity-setup-result.txt", "SETUP_OK\nBones=" + renderer.bones.Length + "\nVertices=" + renderer.sharedMesh.vertexCount + "\nAvatarValid=" + (animator.avatar && animator.avatar.isValid) + "\nRig=Generic\nAnimations=Parado,Andar,Correr\nScene=" + ScenePath);
        Debug.Log("PROTOTYPE_SETUP_OK");
        if (AssetDatabase.IsValidFolder("Assets/DowntownCity")) CityStreetSetup.Create();
    }

    static Material Material(string name, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color; mat.SetFloat("_Glossiness", .08f);
        return mat;
    }

    static void Cube(string name, Vector3 position, Vector3 scale, Material material, bool collider = true)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name; obj.transform.position = position; obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
    }

    // Original prototype cycles, generated on the supplied Generic rig. No external animation assets.
    static AnimationClip MakeClip(Transform root, Dictionary<string, Transform> bones, string name, float length, int gait)
    {
        var clip = new AnimationClip { name = name, frameRate = 60 };
        var animated = new[] { " Pelvis", " Spine", " Spine1", " Neck", " Head", " L UpperArm", " R UpperArm", " L ForeArm", " R ForeArm", " L Thigh", " R Thigh", " L Calf", " R Calf", " L Foot", " R Foot" };
        int samples = Mathf.CeilToInt(length * 60);
        foreach (string boneName in animated)
        {
            Transform bone = bones[boneName];
            Quaternion rest = bone.localRotation;
            Quaternion baseRotation = rest;
            Vector3 axis = bone.parent.InverseTransformDirection(root.right).normalized;
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var yCurve = new AnimationCurve();
            for (int i = 0; i <= samples; i++)
            {
                float t = length * i / samples;
                float phase = i == samples ? 0 : 2 * Mathf.PI * i / samples;
                float swing = Mathf.Sin(phase);
                float sign = boneName.StartsWith(" R") ? -1 : 1;
                float angle = 0;
                if (boneName.EndsWith("Thigh")) angle = -sign * swing * (gait == 2 ? 39 : gait == 1 ? 24 : 0);
                if (boneName.EndsWith("Calf")) angle = Mathf.Max(0, sign * swing) * (gait == 2 ? 78 : gait == 1 ? 42 : 0);
                if (boneName.EndsWith("Foot")) angle = sign * swing * (gait == 2 ? 12 : gait == 1 ? 7 : 0);
                if (boneName.EndsWith("UpperArm")) angle = sign * swing * (gait == 2 ? 34 : gait == 1 ? 17 : 1.3f);
                if (boneName.EndsWith("ForeArm")) angle = -(gait == 2 ? 70 : gait == 1 ? 18 : 8) - (gait > 0 ? sign * swing * 8 : 0);
                if (boneName == " Spine") angle = gait == 2 ? 9 : gait == 1 ? 3 : Mathf.Sin(phase) * .7f;
                Quaternion q = Quaternion.AngleAxis(angle, axis) * baseRotation;
                curves[0].AddKey(t, q.x); curves[1].AddKey(t, q.y); curves[2].AddKey(t, q.z); curves[3].AddKey(t, q.w);
                float bob = gait == 0 ? Mathf.Sin(phase) * .003f : -Mathf.Cos(2 * phase) * (gait == 2 ? .035f : .012f);
                yCurve.AddKey(t, bone.localPosition.y + bob);
            }
            string path = AnimationUtility.CalculateTransformPath(bone, root);
            for (int component = 0; component < 4; component++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[component]), curves[component]);
            if (boneName == " Pelvis") AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"), yCurve);
        }
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true; settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        string assetPath = "Assets/Animations/" + name + ".anim";
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    [MenuItem("Teste/Gerar executavel Windows")]
    public static void BuildWindows()
    {
        PlayerSettings.bundleVersion=GameVersion.Current.version;
        Directory.CreateDirectory("Builds/Windows");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = GameUpdateSetup.Scenes, locationPathName = "Builds/Windows/TestePersonagem.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("Build falhou: " + report.summary.result);
        Debug.Log("PROTOTYPE_BUILD_OK");
    }

    public static void VerifyAndRender()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var movement = UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>();
        var renderer = movement.GetComponentInChildren<SkinnedMeshRenderer>();
        var animator = movement.animator;
        if (renderer.bones.Length != 32 || !animator.runtimeAnimatorController)
            throw new Exception("Rig ou controller invalido.");
        var root = animator.transform;
        var thigh = root.GetComponentsInChildren<Transform>().First(t => t.name == " L Thigh");
        var results = new List<string>();
        foreach (string name in new[] { "Parado", "Andar", "Correr" })
        {
            int clipIndex=name=="Parado"?0:name=="Andar"?1:2;
            var clip = ((BlendTree)((AnimatorController)animator.runtimeAnimatorController).layers[0].stateMachine.defaultState.motion).children[clipIndex].motion as AnimationClip;
            if (!clip || clip.length <= 0 || !clip.isLooping) throw new Exception("Clip invalido: " + name);
            clip.SampleAnimation(root.gameObject, 0); Quaternion start = thigh.localRotation;
            clip.SampleAnimation(root.gameObject, clip.length * .25f);
            float delta = Quaternion.Angle(start, thigh.localRotation);
            if (name != "Parado" && delta < 10) throw new Exception("Perna nao anima: " + name);
            results.Add(name + ": duracao=" + clip.length + ", rotacao_perna=" + delta);
            var camera = Camera.main;
            camera.transform.SetPositionAndRotation(new Vector3(2.8f, 1.9f, 4.8f), Quaternion.LookRotation(new Vector3(0, 1, 0) - new Vector3(2.8f, 1.9f, 4.8f)));
            var rt = new RenderTexture(1000, 750, 24);
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1000, 750, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1000, 750), 0, 0); texture.Apply();
            File.WriteAllBytes("../tools/unity-" + name + ".png", texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
        File.WriteAllLines("../tools/unity-validation.txt", results);
        Debug.Log("PROTOTYPE_VALIDATION_OK");
    }
}
