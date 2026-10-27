using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit, isolated batch preview. Does not save or alter gameplay scenes/assets.
public static class ShootingGripReview
{
    const string ModelPath = "Assets/Resources/GangsterMusculo/GangsterMusculo.fbx";
    const string ControllerPath = "Assets/Animations/Locomocao.controller";
    const int ReviewLayer = 30;
    static readonly BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Serializable] sealed class Joint
    {
        public string bone;
        public Vector3 position, localPosition, localEuler, forward, up, right;
        public Quaternion rotation, localRotation;
    }
    [Serializable] sealed class Palm
    {
        public string side;
        public Vector3 wrist, knuckles, fingerDirection, thumbDirection, normal;
    }
    [Serializable] sealed class Sample
    {
        public string name;
        public float pitch, recoil, rightElbowAngle, leftElbowAngle, muzzleAimErrorDegrees;
        public float rightContactError, leftContactError, rightReachRatio, leftReachRatio;
        public float rightWristBend, leftWristBend, rightWristTwist, leftWristTwist;
        public float rightThumbBend, leftThumbBend, rightThumbSlope, leftThumbSlope;
        public float rightElbowLateral;
        public float gripHeightAboveShoulders, boreHeightAboveShoulders, boreHeightBelowHead;
        public float rightForearmElevation, leftForearmElevation;
        public Vector3 shoulderCenter, headPosition, rightElbowPosition, leftElbowPosition;
        public bool geometryValid;
        public Vector3 aimDirection, pistolPosition, muzzlePosition, muzzleForward;
        public Quaternion pistolRotation;
        public Joint[] joints;
        public Palm[] palms;
    }
    [Serializable] sealed class Report
    {
        public string model, controller, correction;
        public Sample[] samples;
    }
    sealed class Rig
    {
        public GameObject root, model;
        public Animator animator;
        public Transform pistol, muzzle;
        public object correction;
        public MethodInfo apply;
        public Quaternion rightNeutralWrist, leftNeutralWrist;
        public Vector3 rightWristAxis, leftWristAxis;
    }

    [MenuItem("Horizonte/Revisar empunhadura de tiro")]
    public static void Run()
    {
        string folder = Path.GetFullPath("../tools/shooting-grip-review");
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var assets = new List<UnityEngine.Object>();
        var samples = new List<Sample>();
        var oldAmbient = RenderSettings.ambientLight;
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldFog = RenderSettings.fog;
        try
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.57f, .61f, .67f);
            RenderSettings.fog = false;
            var cameraObject = ObjectInScene("Review camera", scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << ReviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .15f, .19f);
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 20f;
            camera.scene = scene;
            AddLight(scene, new Vector3(25, -35, 0), 1.05f);
            AddLight(scene, new Vector3(20, 150, 0), .7f);

            var before = CreateRig(scene, assets);
            RestoreBindPose(before);
            CalibrateWrists(before);
            AlignBaseline(before, Vector3.forward);
            samples.Add(Record(before, "bind", Vector3.forward, 0, 0));
            RenderViews(before, camera, folder, "bind");
            SampleAnimation(before);
            var direction = Quaternion.Euler(3, 0, 0) * Vector3.forward;
            AlignBaseline(before, direction);
            samples.Add(Record(before, "before", direction, 3, 0));
            RenderViews(before, camera, folder, "before");
            SaveReport(folder, samples, "Baseline captured before correction");
            before.root.SetActive(false);

            var after = CreateRig(scene, assets);
            SampleAnimation(after);
            var correctionType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("ShootingGrip", false)).FirstOrDefault(type => type != null);
            if (correctionType == null)
            {
                SaveReport(folder, samples, "Not compiled; baseline only");
                Debug.Log("SHOOTING_GRIP_BASELINE_OK folder=" + folder);
                return;
            }
            after.correction = Activator.CreateInstance(correctionType);
            var configure = correctionType.GetMethod("Configure", Methods, null,
                new[] { typeof(Animator), typeof(Transform), typeof(Transform), typeof(Transform) }, null);
            after.apply = correctionType.GetMethod("ApplyPose", Methods, null,
                new[] { typeof(Vector3), typeof(float) }, null);
            if (configure == null || after.apply == null)
                throw new Exception("Expected ShootingGrip.Configure(Animator,Transform,Transform,Transform) and ApplyPose(Vector3,float).");
            RestoreBindPose(after);
            CalibrateWrists(after);
            if (configure.ReturnType == typeof(bool) && !(bool)configure.Invoke(after.correction,
                    new object[] { after.animator, after.pistol, after.muzzle, after.root.transform }))
                throw new Exception("ShootingGrip.Configure returned false.");
            if (configure.ReturnType != typeof(bool)) configure.Invoke(after.correction,
                new object[] { after.animator, after.pistol, after.muzzle, after.root.transform });
            foreach (float pitch in new[] { 3f, -5f, 20f })
            {
                SampleAnimation(after);
                direction = Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                after.apply.Invoke(after.correction, new object[] { direction, 0f });
                string name = pitch == 3 ? "after" : pitch < 0 ? "after-up" : "after-down";
                samples.Add(Record(after, name, direction, pitch, 0));
                RenderViews(after, camera, folder, name);
                RenderRightOnly(after, camera, folder, name);
            }
            SampleAnimation(after);
            direction = Quaternion.Euler(3, 0, 0) * Vector3.forward;
            after.apply.Invoke(after.correction, new object[] { direction, 1f });
            samples.Add(Record(after, "after-recoil", direction, 3, 1));
            RenderViews(after, camera, folder, "after-recoil");
            RenderRightOnly(after, camera, folder, "after-recoil");
            foreach (float yaw in new[] { 90f, -45f, 45f })
            {
                after.root.transform.rotation = yaw == 90 ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
                SampleAnimation(after);
                direction = Quaternion.Euler(3, yaw, 0) * Vector3.forward;
                after.apply.Invoke(after.correction, new object[] { direction, 0f });
                string name = yaw == 90 ? "after-root-yaw90" : yaw < 0 ? "after-yaw-left" : "after-yaw-right";
                samples.Add(Record(after, name, direction, 3, 0));
                RenderViews(after, camera, folder, name);
                RenderRightOnly(after, camera, folder, name);
            }
            SaveReport(folder, samples, correctionType.FullName);
            Debug.Log("SHOOTING_GRIP_REVIEW_OK folder=" + folder + " samples=" + samples.Count);
            foreach (var sample in samples)
                Debug.Log("SHOOTING_GRIP_SAMPLE " + sample.name + " elbows=" + sample.rightElbowAngle + "/" +
                    sample.leftElbowAngle + " muzzleAimError=" + sample.muzzleAimErrorDegrees +
                    " palmContactError=" + sample.rightContactError + "/" + sample.leftContactError +
                    " reach=" + sample.rightReachRatio + "/" + sample.leftReachRatio +
                    " wristBend=" + sample.rightWristBend + "/" + sample.leftWristBend +
                    " wristTwist=" + sample.rightWristTwist + "/" + sample.leftWristTwist +
                    " thumbBend=" + sample.rightThumbBend + "/" + sample.leftThumbBend +
                    " thumbSlope=" + sample.rightThumbSlope + "/" + sample.leftThumbSlope +
                    " rightElbowLateral=" + sample.rightElbowLateral +
                    " gripAboveShoulders=" + sample.gripHeightAboveShoulders +
                    " boreAboveShoulders=" + sample.boreHeightAboveShoulders +
                    " boreBelowHead=" + sample.boreHeightBelowHead +
                    " forearmElevation=" + sample.rightForearmElevation + "/" + sample.leftForearmElevation);
            if (samples.Any(sample => sample.name.StartsWith("after") && !sample.geometryValid))
                throw new Exception("Shooting grip geometric checks failed. See joints.json and rendered review views.");
        }
        finally
        {
            RenderSettings.ambientLight = oldAmbient;
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.fog = oldFog;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) if (asset) UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    // Build the current saved project exactly as configured; no scene/setup tools run.
    public static void RunAndBuildWindows()
    {
        Run();
        BuildWindows();
    }

    public static void BuildWindows()
    {
        BuildWindowsAt("Builds/Windows/TestePersonagem.exe");
    }

    public static void RunAndBuildTestWindows()
    {
        Run();
        BuildWindowsAt("Builds/ShootingRangeTest/HorizonteShootingTest.exe");
    }

    public static void RunAndBuildBothWindows()
    {
        Run();
        BuildWindowsAt("Builds/Windows/TestePersonagem.exe");
        BuildWindowsAt("Builds/ShootingRangeTest/HorizonteShootingTest.exe");
    }

    static void BuildWindowsAt(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = GameUpdateSetup.Scenes,
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("Windows build failed: " + report.summary.result);
        Debug.Log("SHOOTING_GRIP_WINDOWS_BUILD_OK path=" + Path.GetFullPath(path));
    }

    static void SaveReport(string folder, List<Sample> samples, string correction)
    {
        File.WriteAllText(Path.Combine(folder, "joints.json"), JsonUtility.ToJson(new Report
        {
            model = ModelPath, controller = ControllerPath, correction = correction, samples = samples.ToArray()
        }, true));
    }

    static void RestoreBindPose(Rig rig)
    {
        var bones = rig.model.GetComponentsInChildren<Transform>(true).GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var bind in rig.animator.avatar.humanDescription.skeleton)
        {
            if (!bones.TryGetValue(bind.name, out var bone) || bone == rig.model.transform) continue;
            bone.localPosition = bind.position;
            bone.localRotation = bind.rotation;
            bone.localScale = bind.scale;
        }
    }

    static Rig CreateRig(Scene scene, List<UnityEngine.Object> assets)
    {
        var rig = new Rig { root = ObjectInScene("Review player", scene) };
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new Exception("Gangster model is absent.");
        rig.model = UnityEngine.Object.Instantiate(source, rig.root.transform);
        rig.model.name = "Gangster Musculo";
        rig.animator = rig.model.GetComponentInChildren<Animator>();
        if (!rig.animator || !rig.animator.avatar || !rig.animator.avatar.isHuman)
            throw new Exception("Gangster Humanoid avatar is absent.");
        foreach (var renderer in rig.model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i]) continue;
                string surface = materials[i].name.Contains("Body") ? "Body" : materials[i].name.Contains("Head") ? "Face" :
                    materials[i].name.Contains("Cloth") ? "Pants" : null;
                if (surface == null) continue;
                var material = new Material(Shader.Find("Standard")) { name = materials[i].name };
                string stem = "Assets/Resources/GangsterMusculo/textures/Dealerguy_El_Musculo_Male_" + surface;
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + "_B.png");
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(stem + "_N.png"));
                material.SetFloat("_BumpScale", .6f);
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Glossiness", .16f);
                assets.Add(material);
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
        var renderers = rig.model.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        rig.model.transform.localScale *= 1.8f / bounds.size.y;
        rig.model.transform.localPosition = Vector3.zero;
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var overrides = new AnimatorOverrideController(controller);
        assets.Add(overrides);
        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrides.GetOverrides(clips);
        var replacements = new Dictionary<string, string>
        {
            { "IdleRetarget", ModelPath },
            { "Andar_WalkingOriginal", "Assets/Resources/GangsterMusculo/Animations/Walking.fbx" },
            { "Correr_FastRunEnviado", "Assets/Resources/GangsterMusculo/Animations/Running.fbx" },
            { "JumpRetarget", "Assets/Resources/GangsterMusculo/Animations/Jump.fbx" }
        };
        for (int i = 0; i < clips.Count; i++)
            if (replacements.TryGetValue(clips[i].Key.name, out var path))
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clips[i].Key,
                    AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(clip =>
                        clip.humanMotion && clip.length > 0 && !clip.name.StartsWith("__preview")));
        overrides.ApplyOverrides(clips);
        rig.animator.runtimeAnimatorController = overrides;
        rig.animator.applyRootMotion = false;
        rig.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        rig.animator.Rebind();
        rig.animator.Update(0);
        SampleAnimation(rig);
        CreatePistol(rig);
        foreach (var child in rig.root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ReviewLayer;
        return rig;
    }

    static void SampleAnimation(Rig rig)
    {
        // Force evaluation after manually restoring the diagnostic bind pose.
        // A zero-delta update of the same frozen state can reuse its cached pose.
        rig.animator.Rebind();
        rig.animator.SetFloat("Speed", 0);
        rig.animator.SetFloat("ShootingSpeed", 0);
        int layer = rig.animator.GetLayerIndex("SocoParteSuperior");
        rig.animator.Play("Shooting", layer, .08f);
        rig.animator.SetLayerWeight(layer, 1);
        rig.animator.Update(1f / 120f);
    }

    static void CreatePistol(Rig rig)
    {
        rig.pistol = new GameObject("Pistola de treino").transform;
        rig.pistol.SetParent(rig.root.transform, false);
        var hand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand);
        rig.pistol.position = hand.position;
        var prefab = Resources.Load<GameObject>("Weapons/ColtPocket/ColtPocketHammer");
        if (!prefab) throw new Exception("Colt Pocket Hammer is absent.");
        var model = UnityEngine.Object.Instantiate(prefab, rig.pistol, false);
        model.name = "Colt Pocket Hammer";
        foreach (var camera in model.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
        foreach (var light in model.GetComponentsInChildren<Light>(true)) light.gameObject.SetActive(false);
        var gripBone = model.transform.Find("Armature/body/mag");
        var slideBone = model.transform.Find("Armature/body/slide");
        var muzzleBone = model.transform.Find("Armature/body/slide/slide_end");
        if (slideBone && muzzleBone)
        {
            var axis = model.transform.InverseTransformDirection(muzzleBone.position - slideBone.position);
            if (axis.sqrMagnitude > .000001f)
                model.transform.localRotation = Quaternion.FromToRotation(axis.normalized, Vector3.forward);
        }
        var renderers = model.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        model.transform.localScale = Vector3.one * (.27f / size);
        if (gripBone) model.transform.localPosition -= rig.pistol.InverseTransformPoint(gripBone.position);
        else throw new Exception("Weapon grip bone is absent; update review normalization with runtime code.");
        rig.muzzle = new GameObject("Boca do cano").transform;
        rig.muzzle.SetParent(rig.pistol, false);
        rig.muzzle.localPosition = muzzleBone ? rig.pistol.InverseTransformPoint(muzzleBone.position) + Vector3.forward * .018f :
            new Vector3(0, .08f, .31f);
        LogPistolGeometry(rig, model.transform, renderers, gripBone, slideBone, muzzleBone);
    }

    static void LogPistolGeometry(Rig rig, Transform model, Renderer[] renderers, Transform grip, Transform slide, Transform barrelEnd)
    {
        Debug.Log("SHOOTING_WEAPON_MODEL localPosition=" + model.localPosition.ToString("F6") +
            " localEuler=" + model.localEulerAngles.ToString("F6") + " localScale=" + model.localScale.ToString("F6"));
        foreach (var marker in new[] { grip, slide, barrelEnd, rig.muzzle })
            if (marker) Debug.Log("SHOOTING_WEAPON_BONE " + AnimationUtility.CalculateTransformPath(marker, rig.pistol) +
                " pistolLocalPosition=" + rig.pistol.InverseTransformPoint(marker.position).ToString("F6") +
                " pistolLocalEuler=" + (Quaternion.Inverse(rig.pistol.rotation) * marker.rotation).eulerAngles.ToString("F6"));
        var combined = new Bounds();
        bool initialized = false;
        foreach (var renderer in renderers)
        {
            Mesh temporary = null;
            try
            {
                Bounds sourceBounds;
                var skin = renderer as SkinnedMeshRenderer;
                if (skin)
                {
                    temporary = SkinWorld(skin);
                    sourceBounds = temporary.bounds;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    sourceBounds = filter && filter.sharedMesh ? filter.sharedMesh.bounds : renderer.localBounds;
                }
                var local = new Bounds();
                bool hasPoint = false;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    var vertex = new Vector3(x == 0 ? sourceBounds.min.x : sourceBounds.max.x,
                        y == 0 ? sourceBounds.min.y : sourceBounds.max.y, z == 0 ? sourceBounds.min.z : sourceBounds.max.z);
                    var point = rig.pistol.InverseTransformPoint(skin ? vertex : renderer.transform.TransformPoint(vertex));
                    if (!hasPoint) { local = new Bounds(point, Vector3.zero); hasPoint = true; }
                    else local.Encapsulate(point);
                }
                if (!initialized) { combined = local; initialized = true; } else combined.Encapsulate(local);
                Debug.Log("SHOOTING_WEAPON_RENDERER " + AnimationUtility.CalculateTransformPath(renderer.transform, rig.pistol) +
                    " type=" + renderer.GetType().Name + " parent=" + renderer.transform.parent.name +
                    " pistolLocalBoundsCenter=" + local.center.ToString("F6") + " size=" + local.size.ToString("F6") +
                    " min=" + local.min.ToString("F6") + " max=" + local.max.ToString("F6"));
            }
            finally { if (temporary) UnityEngine.Object.DestroyImmediate(temporary); }
        }
        Debug.Log("SHOOTING_WEAPON_COMBINED_BOUNDS center=" + combined.center.ToString("F6") +
            " size=" + combined.size.ToString("F6") + " min=" + combined.min.ToString("F6") + " max=" + combined.max.ToString("F6"));
    }

    static void AlignBaseline(Rig rig, Vector3 direction)
    {
        var hand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand);
        rig.pistol.SetPositionAndRotation(hand.position + direction * .015f + Vector3.up * .012f,
            Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0, -8, 0));
    }

    static Sample Record(Rig rig, string name, Vector3 direction, float pitch, float recoil)
    {
        var joints = new List<Joint>();
        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            string label = bone.ToString();
            if (!(label.Contains("Arm") || label.Contains("Hand") || label.Contains("Shoulder") || label.Contains("Thumb") ||
                label.Contains("Index") || label.Contains("Middle") || label.Contains("Ring") || label.Contains("Little") ||
                label == "Spine" || label == "Chest" || label == "UpperChest" || label == "Head" || label == "Neck")) continue;
            var joint = rig.animator.GetBoneTransform(bone);
            if (!joint) continue;
            joints.Add(new Joint { bone = label, position = joint.position, localPosition = joint.localPosition,
                localEuler = joint.localEulerAngles, rotation = joint.rotation, localRotation = joint.localRotation,
                forward = joint.forward, up = joint.up, right = joint.right });
        }
        foreach (var joint in rig.model.GetComponentsInChildren<Transform>(true).Where(bone =>
            bone.name.Contains("UpperarmTwist") || bone.name.Contains("ForearmTwist") || bone.name.Contains("ElbowShare")))
            joints.Add(new Joint { bone = joint.name, position = joint.position, localPosition = joint.localPosition,
                localEuler = joint.localEulerAngles, rotation = joint.rotation, localRotation = joint.localRotation,
                forward = joint.forward, up = joint.up, right = joint.right });
        Vector3 rightCenter = Vector3.Lerp(rig.animator.GetBoneTransform(HumanBodyBones.RightHand).position,
            rig.animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position, .65f);
        Vector3 leftCenter = Vector3.Lerp(rig.animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
            rig.animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal).position, .65f);
        float rightContact = Vector3.Distance(rightCenter, rig.pistol.position + rig.pistol.right * .023f);
        float leftContact = Vector3.Distance(leftCenter, rig.pistol.position - rig.pistol.right * .028f -
            rig.pistol.up * .012f + rig.pistol.forward * .025f);
        float rightReach = Reach(rig.animator, false), leftReach = Reach(rig.animator, true);
        float aimError = Vector3.Angle(rig.muzzle.forward, direction);
        float rightBend = WristBend(rig.animator, false), leftBend = WristBend(rig.animator, true);
        float rightTwist = WristTwist(rig, false), leftTwist = WristTwist(rig, true);
        float rightThumbBend = ThumbBend(rig.animator, false), leftThumbBend = ThumbBend(rig.animator, true);
        Vector3 shoulders = (rig.animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position +
            rig.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position) * .5f;
        Vector3 head = rig.animator.GetBoneTransform(HumanBodyBones.Head).position;
        return new Sample
        {
            name = name, pitch = pitch, recoil = recoil, aimDirection = direction,
            rightElbowAngle = Elbow(rig.animator, false), leftElbowAngle = Elbow(rig.animator, true),
            pistolPosition = rig.pistol.position, pistolRotation = rig.pistol.rotation,
            muzzlePosition = rig.muzzle.position, muzzleForward = rig.muzzle.forward,
            muzzleAimErrorDegrees = aimError,
            rightContactError = rightContact, leftContactError = leftContact,
            rightReachRatio = rightReach, leftReachRatio = leftReach,
            rightWristBend = rightBend, leftWristBend = leftBend,
            rightWristTwist = rightTwist, leftWristTwist = leftTwist,
            rightThumbBend = rightThumbBend, leftThumbBend = leftThumbBend,
            rightThumbSlope = ThumbSlope(rig, false), leftThumbSlope = ThumbSlope(rig, true),
            rightElbowLateral = Vector3.Dot(rig.animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position -
                rig.animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position, rig.pistol.right),
            shoulderCenter = shoulders, headPosition = head,
            rightElbowPosition = rig.animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position,
            leftElbowPosition = rig.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position,
            gripHeightAboveShoulders = Vector3.Dot(rig.pistol.position - shoulders, rig.root.transform.up),
            boreHeightAboveShoulders = Vector3.Dot(rig.muzzle.position - shoulders, rig.root.transform.up),
            boreHeightBelowHead = Vector3.Dot(head - rig.muzzle.position, rig.root.transform.up),
            rightForearmElevation = ForearmElevation(rig, false), leftForearmElevation = ForearmElevation(rig, true),
            geometryValid = rightContact < .003f && leftContact < .015f &&
                rightReach < .985f && leftReach < .985f && aimError < (recoil > 0 ? 4.1f : .1f) &&
                rightBend <= 30f && leftBend <= 30f && Mathf.Abs(rightTwist) <= 15f && Mathf.Abs(leftTwist) <= 15f &&
                (!name.StartsWith("after", StringComparison.Ordinal) || (rightThumbBend >= 20f && leftThumbBend >= 20f)),
            joints = joints.ToArray(), palms = new[] { PalmFrame(rig.animator, false), PalmFrame(rig.animator, true) }
        };
    }

    static float Reach(Animator animator, bool left)
    {
        var shoulder = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
        var elbow = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
        return Vector3.Distance(shoulder, hand) / (Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, hand));
    }

    static float ForearmElevation(Rig rig, bool left)
    {
        var forearm = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        return Mathf.Asin(Mathf.Clamp(Vector3.Dot((hand.position - forearm.position).normalized,
            rig.root.transform.up), -1f, 1f)) * Mathf.Rad2Deg;
    }

    // Calibrate in the same native bind pose used by ShootingGrip. Quaternion
    // swing/twist is measured about the forearm's local longitudinal axis.
    static void CalibrateWrists(Rig rig)
    {
        var rightForearm = rig.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var leftForearm = rig.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var rightHand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand);
        var leftHand = rig.animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rig.rightNeutralWrist = Quaternion.Inverse(rightForearm.rotation) * rightHand.rotation;
        rig.leftNeutralWrist = Quaternion.Inverse(leftForearm.rotation) * leftHand.rotation;
        rig.rightWristAxis = rightForearm.InverseTransformDirection(rightHand.position - rightForearm.position).normalized;
        rig.leftWristAxis = leftForearm.InverseTransformDirection(leftHand.position - leftForearm.position).normalized;
    }

    static float WristBend(Animator animator, bool left)
    {
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        return Vector3.Angle(hand.position - lower.position, middle.position - hand.position);
    }

    static float ThumbBend(Animator animator, bool left)
    {
        var proximal = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        var intermediate = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
        var distal = animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
        return Vector3.Angle(intermediate.position - proximal.position, distal.position - intermediate.position);
    }

    static float ThumbSlope(Rig rig, bool left)
    {
        var intermediate = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
        var distal = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
        Vector3 direction = (distal.position - intermediate.position).normalized;
        return Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, rig.pistol.up), -1f, 1f)) * Mathf.Rad2Deg;
    }

    static float WristTwist(Rig rig, bool left)
    {
        var lower = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var relative = Quaternion.Inverse(lower.rotation) * hand.rotation;
        var neutral = left ? rig.leftNeutralWrist : rig.rightNeutralWrist;
        var axis = left ? rig.leftWristAxis : rig.rightWristAxis;
        var delta = relative * Quaternion.Inverse(neutral);
        float projected = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis);
        return Mathf.DeltaAngle(0, 2f * Mathf.Atan2(projected, delta.w) * Mathf.Rad2Deg);
    }

    static float Elbow(Animator animator, bool left)
    {
        var shoulder = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var elbow = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        return Vector3.Angle(shoulder.position - elbow.position, wrist.position - elbow.position);
    }

    static Palm PalmFrame(Animator animator, bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        var finger = (middle.position - hand.position).normalized;
        var thumb = (index.position - little.position).normalized;
        return new Palm { side = left ? "left" : "right", wrist = hand.position, knuckles = middle.position,
            fingerDirection = finger, thumbDirection = thumb, normal = Vector3.Cross(finger, thumb).normalized };
    }

    static void RenderViews(Rig rig, Camera camera, string folder, string prefix, bool closeOnly = false)
    {
        // Skin directly into world space so skeleton scale is applied once.
        // BakeMesh has different scale handling from these imported hierarchies.
        var skins = rig.root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(skin => skin.enabled).ToArray();
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        try
        {
            foreach (var skin in skins)
            {
                var mesh = SkinWorld(skin);
                meshes.Add(mesh);
                var snapshot = ObjectInScene("World review skin " + skin.name, rig.root.scene);
                snapshot.layer = ReviewLayer;
                snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                snapshots.Add(snapshot);
                skin.enabled = false;
            }
            var shoulder = (rig.animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position +
                rig.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position) * .5f;
            var left = rig.animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            var hand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            var focus = Vector3.Lerp(shoulder, (left + hand) * .5f, .35f) - Vector3.up * .08f;
            if (!closeOnly)
            {
                Render(camera, folder, prefix + "-rear-oblique", focus, rig.root.transform.TransformDirection(new Vector3(1.8f, .65f, -2.6f)), .69f);
                Render(camera, folder, prefix + "-front", focus, rig.root.transform.TransformDirection(new Vector3(-.7f, .35f, 2.8f)), .69f);
                Render(camera, folder, prefix + "-right", focus, rig.root.transform.TransformDirection(new Vector3(3, .3f, .05f)), .69f);
                Render(camera, folder, prefix + "-reference-shoulder",
                    shoulder + rig.root.transform.up * .03f + rig.root.transform.forward * .12f,
                    rig.root.transform.TransformDirection(new Vector3(.70f, .28f, -1.35f)), .69f, true, 655, 720);
            }
            Render(camera, folder, prefix + "-hands", closeOnly ? (hand + rig.muzzle.position) * .5f : (hand + left + rig.muzzle.position) / 3,
                rig.root.transform.TransformDirection(new Vector3(1.3f, .6f, 1.6f)), closeOnly ? .21f : .25f);
            var wristFocus = Vector3.Lerp(hand, rig.pistol.position, .55f);
            Render(camera, folder, prefix + "-wrist-right", wristFocus,
                rig.root.transform.TransformDirection(new Vector3(3f, .1f, .1f)), .19f);
            Render(camera, folder, prefix + "-wrist-back", wristFocus,
                rig.root.transform.TransformDirection(new Vector3(1.6f, .35f, -2.8f)), .21f);
            Render(camera, folder, prefix + "-wrist-top", wristFocus,
                rig.root.transform.TransformDirection(new Vector3(.1f, 3f, -.3f)), .24f);
        }
        finally
        {
            foreach (var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var skin in skins) skin.enabled = true;
        }
    }

    static void RenderRightOnly(Rig rig, Camera camera, string folder, string prefix)
    {
        // Temporarily move the support arm to its native rest pose so it cannot
        // conceal a bad primary grip or wrist in the close-up views.
        var upper = rig.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var saved = upper.GetComponentsInChildren<Transform>(true).ToDictionary(bone => bone, bone => bone.localRotation);
        var native = rig.animator.avatar.humanDescription.skeleton.ToDictionary(bone => bone.name, bone => bone.rotation);
        try
        {
            foreach (var bone in saved.Keys)
                if (native.TryGetValue(bone.name, out var rotation)) bone.localRotation = rotation;
            RenderViews(rig, camera, folder, prefix + "-right-only", true);
        }
        finally
        {
            foreach (var bone in saved) bone.Key.localRotation = bone.Value;
        }
    }

    static Mesh SkinWorld(SkinnedMeshRenderer skin)
    {
        var source = skin.sharedMesh;
        if (!source) throw new Exception("Review skin has no mesh: " + skin.name);
        Vector3[] vertices, normals;
        Vector4[] tangents;
        BoneWeight[] weights;
        Matrix4x4[] bindPoses;
        try
        {
            // Unity Editor permits access to imported mesh data even when the
            // player's Read/Write copy is disabled; no importer changes needed.
            vertices = source.vertices;
            normals = source.normals;
            tangents = source.tangents;
            weights = source.boneWeights;
            bindPoses = source.bindposes;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("SHOOTING_REVIEW_BAKE_FALLBACK " + skin.name + ": " + exception.Message);
            var baked = new Mesh();
            // Include scale in the bake, then apply only rotation/translation.
            skin.BakeMesh(baked, true);
            var world = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            var points = baked.vertices;
            var surfaceNormals = baked.normals;
            for (int i = 0; i < points.Length; i++) points[i] = world.MultiplyPoint3x4(points[i]);
            for (int i = 0; i < surfaceNormals.Length; i++) surfaceNormals[i] = world.MultiplyVector(surfaceNormals[i]).normalized;
            baked.vertices = points;
            baked.normals = surfaceNormals;
            baked.RecalculateBounds();
            return baked;
        }

        var bones = skin.bones;
        if (vertices.Length == 0 || weights.Length != vertices.Length || bindPoses.Length > bones.Length)
            throw new Exception("Review skin data is incomplete: " + skin.name);
        var matrices = new Matrix4x4[bindPoses.Length];
        var normalMatrices = new Matrix4x4[bindPoses.Length];
        for (int i = 0; i < matrices.Length; i++)
        {
            if (!bones[i]) throw new Exception("Missing review bone " + i + " in " + skin.name);
            matrices[i] = bones[i].localToWorldMatrix * bindPoses[i];
            normalMatrices[i] = matrices[i].inverse.transpose;
        }
        var worldVertices = new Vector3[vertices.Length];
        var worldNormals = normals.Length == vertices.Length ? new Vector3[vertices.Length] : null;
        var worldTangents = tangents.Length == vertices.Length ? new Vector4[vertices.Length] : null;
        for (int i = 0; i < vertices.Length; i++)
        {
            var weight = weights[i];
            worldVertices[i] = WeightedPoint(vertices[i], weight, matrices);
            if (worldNormals != null) worldNormals[i] = WeightedDirection(normals[i], weight, normalMatrices).normalized;
            if (worldTangents != null)
            {
                var tangent = WeightedDirection(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z), weight, matrices).normalized;
                worldTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
            }
        }
        var result = UnityEngine.Object.Instantiate(source);
        result.name = source.name + " world review";
        result.vertices = worldVertices;
        if (worldNormals != null) result.normals = worldNormals; else result.RecalculateNormals();
        if (worldTangents != null) result.tangents = worldTangents;
        result.RecalculateBounds();
        return result;
    }

    static Vector3 WeightedPoint(Vector3 vertex, BoneWeight weight, Matrix4x4[] matrices)
    {
        Vector3 result = Vector3.zero;
        if (weight.weight0 > 0) result += matrices[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
        if (weight.weight1 > 0) result += matrices[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
        if (weight.weight2 > 0) result += matrices[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
        if (weight.weight3 > 0) result += matrices[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
        return result;
    }

    static Vector3 WeightedDirection(Vector3 direction, BoneWeight weight, Matrix4x4[] matrices)
    {
        Vector3 result = Vector3.zero;
        if (weight.weight0 > 0) result += matrices[weight.boneIndex0].MultiplyVector(direction) * weight.weight0;
        if (weight.weight1 > 0) result += matrices[weight.boneIndex1].MultiplyVector(direction) * weight.weight1;
        if (weight.weight2 > 0) result += matrices[weight.boneIndex2].MultiplyVector(direction) * weight.weight2;
        if (weight.weight3 > 0) result += matrices[weight.boneIndex3].MultiplyVector(direction) * weight.weight3;
        return result;
    }

    static void Render(Camera camera, string folder, string name, Vector3 focus, Vector3 offset, float size,
        bool perspective = false, int width = 1200, int height = 1000)
    {
        camera.orthographic = !perspective;
        camera.fieldOfView = 40f;
        camera.transform.position = focus + offset;
        camera.transform.LookAt(focus);
        camera.orthographicSize = size;
        var previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 24);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static GameObject ObjectInScene(string name, Scene scene)
    {
        var instance = new GameObject(name);
        SceneManager.MoveGameObjectToScene(instance, scene);
        return instance;
    }

    static void AddLight(Scene scene, Vector3 angles, float intensity)
    {
        var instance = ObjectInScene("Review light", scene);
        instance.transform.rotation = Quaternion.Euler(angles);
        var light = instance.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.cullingMask = 1 << ReviewLayer;
    }
}
