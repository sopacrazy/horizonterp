using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// Explicit, read-only asset diagnostic. No automatic import, pose correction or hip pinning.
public static class PoliceCarClipCalibration
{
    const string PlayerPrefabPath = "Assets/Prefabs/Jogador.prefab";
    const int SampleIntervals = 40; // Inclusive samples: 0, .025, ... 1.
    static readonly HumanBodyBones[] MeasuredBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
        HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand
    };

    [Serializable] sealed class Report
    {
        public int schemaVersion = 1;
        public bool completed;
        public string generatedUtc, unityVersion, prefabPath, avatarName, avatarSource;
        public string animatorController, error;
        public string coordinates = "Metres in Unity world (+Y up, +Z forward, +X right). Root-relative points use the identity Jogador root; avatar-relative points include inverse avatar scale.";
        public string sampling = "AnimationClipPlayable, manual graph, forward raw clip time, root motion/Foot IK/Playable IK off; no procedural pose or transform correction between samples.";
        public string orientation = "Geometric right = right-side bone minus left-side bone; up points toward torso/head; forward = cross(right, up). Yaw = atan2(forward.x, forward.z), degrees.";
        public float normalizedStep = 1f / SampleIntervals;
        public float humanScale;
        public bool applyRootMotion, proceduralBehavioursDisabled;
        public TransformData playerRootAtSetup, avatarRootAtSetup;
        public Frame standingIdle;
        public List<ClipData> clips = new List<ClipData>();
    }

    [Serializable] sealed class TransformData
    {
        public Vector3 worldPosition, localPosition, localScale, lossyScale;
        public Quaternion worldRotation, localRotation;
    }

    [Serializable] sealed class BoneData
    {
        public string bone;
        public bool found;
        public Vector3 worldPosition, playerRelativePosition, avatarRelativePosition;
        public Quaternion worldRotation, playerRelativeRotation;
    }

    [Serializable] sealed class OrientationData
    {
        public bool valid;
        public Vector3 origin, right, up, forward;
        public float yawDegrees;
    }

    [Serializable] sealed class Frame
    {
        public float normalizedTime;
        public double requestedTimeSeconds, playableTimeSeconds;
        public TransformData playerRoot, avatarRoot;
        public Vector3 animatorRootPosition, bodyPosition, bodyForward, bodyRight, deltaPosition;
        public Quaternion animatorRootRotation, bodyRotation, deltaRotation;
        public OrientationData hipsGeometry, chestGeometry;
        public float minimumFootOrToeBoneY;
        public Vector3 hipsDisplacementFromClipStart;
        public float hipsYawFromClipStart;
        public List<BoneData> bones = new List<BoneData>();
    }

    [Serializable] sealed class ImportData
    {
        public string animationType, avatarSetup;
        public bool foundClipSettings, lockRootRotation, lockRootHeightY, lockRootPositionXZ;
        public bool keepOriginalOrientation, keepOriginalPositionY, keepOriginalPositionXZ, loopTime;
        public float rotationOffset, heightOffset;
    }

    [Serializable] sealed class ClipData
    {
        public string resourcePath, assetPath, clipName;
        public float lengthSeconds, frameRate;
        public bool humanMotion, isLooping, hasRootCurves, hasMotionCurves;
        public ImportData import;
        public List<Frame> frames = new List<Frame>();
    }

    [MenuItem("Horizonte/Testes locais/Calibrar trajetória natural dos clipes da viatura")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run PoliceCarClipCalibration outside Play Mode.");

        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/police-car-animation"));
        Directory.CreateDirectory(folder);
        var report = new Report
        {
            generatedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            prefabPath = PlayerPrefabPath, avatarSource = "GangsterMusculo/GangsterMusculo via PoliceVehicleAnimationVerification.CreateAvatar"
        };
        var generated = new List<UnityEngine.Object>();
        Scene previewScene = default(Scene);
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (!prefab) throw new InvalidOperationException("Current Jogador prefab is absent.");
            previewScene = EditorSceneManager.NewPreviewScene();
            var playerObject = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
            if (!playerObject) throw new InvalidOperationException("Jogador prefab could not be instantiated.");
            playerObject.name = "Jogador — raw car clip calibration";
            playerObject.hideFlags = HideFlags.HideAndDontSave;
            playerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // Keep the current prefab root scale and the Gangster helper's runtime avatar scale.
            DisableProceduralBehaviours(playerObject);
            foreach (Transform child in playerObject.transform) child.gameObject.SetActive(false);

            var model = CreateCurrentAvatar(playerObject.transform, generated, out var animator);
            model.transform.localPosition = Vector3.zero; // Same origin used by GangsterPlayerVisual.Awake.
            DisableProceduralBehaviours(model);
            var player = playerObject.GetComponent<ThirdPersonPlayer>();
            if (!player) throw new InvalidOperationException("Jogador prefab has no ThirdPersonPlayer.");
            player.animator = animator;
            if (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The current Gangster avatar is not a valid Humanoid.");
            if (!(animator.runtimeAnimatorController is AnimatorOverrideController))
                throw new InvalidOperationException("Current Gangster animator overrides were not installed.");
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
            report.avatarName = animator.avatar.name;
            report.animatorController = animator.runtimeAnimatorController.name;
            report.humanScale = animator.humanScale;
            report.applyRootMotion = animator.applyRootMotion;
            report.proceduralBehavioursDisabled = !playerObject.GetComponentsInChildren<MonoBehaviour>(true).Any(x => x && x.enabled);
            report.playerRootAtSetup = CaptureTransform(playerObject.transform);
            report.avatarRootAtSetup = CaptureTransform(model.transform);
            report.standingIdle = CaptureFrame(animator, playerObject.transform, model.transform, 0, 0, 0);

            foreach (string resourcePath in new[] { "Vehicles/Police/EnteringCar", "Vehicles/Police/ExitingCar" })
                SampleClip(report, resourcePath, animator, playerObject.transform, model.transform);
            report.completed = true;
            Debug.Log("POLICE_CAR_CLIP_CALIBRATION_COMPLETE: " + Path.Combine(folder, "clip-calibration.json"));
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
            Debug.LogError("POLICE_CAR_CLIP_CALIBRATION_FAILED: " + exception.Message);
            throw;
        }
        finally
        {
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            foreach (var item in generated) if (item) UnityEngine.Object.DestroyImmediate(item);
            File.WriteAllText(Path.Combine(folder, "clip-calibration.json"), JsonUtility.ToJson(report, true));
        }
    }

    static GameObject CreateCurrentAvatar(Transform parent, List<UnityEngine.Object> generated, out Animator animator)
    {
        // Reuse the current verifier's exact resource, humanoid/controller overrides and size setup.
        var verifier = typeof(PoliceCarClipCalibration).Assembly.GetType("PoliceVehicleAnimationVerification");
        var create = verifier?.GetMethod("CreateAvatar", BindingFlags.Static | BindingFlags.NonPublic);
        if (create == null) throw new MissingMethodException("PoliceVehicleAnimationVerification.CreateAvatar was not found.");
        object[] arguments = { parent, null, generated };
        GameObject model;
        try { model = (GameObject)create.Invoke(null, arguments); }
        catch (TargetInvocationException exception)
        { throw new InvalidOperationException("Current Gangster avatar setup failed.", exception.InnerException ?? exception); }
        animator = arguments[1] as Animator;
        if (!model || !animator) throw new InvalidOperationException("Current Gangster avatar setup returned no animator.");
        return model;
    }

    static void DisableProceduralBehaviours(GameObject root)
    {
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour) behaviour.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
    }

    static void SampleClip(Report report, string resourcePath, Animator animator, Transform playerRoot, Transform avatarRoot)
    {
        var clip = Resources.LoadAll<AnimationClip>(resourcePath)
            .FirstOrDefault(x => x && x.humanMotion && x.length > 0 && !x.name.StartsWith("__preview", StringComparison.Ordinal));
        if (!clip) throw new InvalidOperationException("Raw Humanoid clip is absent: " + resourcePath);
        var data = new ClipData
        {
            resourcePath = resourcePath, assetPath = AssetDatabase.GetAssetPath(clip), clipName = clip.name,
            lengthSeconds = clip.length, frameRate = clip.frameRate, humanMotion = clip.humanMotion,
            isLooping = clip.isLooping, hasRootCurves = clip.hasRootCurves, hasMotionCurves = clip.hasMotionCurves
        };
        data.import = CaptureImport(data.assetPath, clip.name);
        report.clips.Add(data);

        // Restore only between clips. No per-frame root/hips positioning or procedural pose evaluation.
        playerRoot.SetPositionAndRotation(report.playerRootAtSetup.worldPosition, report.playerRootAtSetup.worldRotation);
        avatarRoot.localPosition = report.avatarRootAtSetup.localPosition;
        avatarRoot.localRotation = report.avatarRootAtSetup.localRotation;
        avatarRoot.localScale = report.avatarRootAtSetup.localScale;
        animator.Rebind();
        animator.SetFloat("Speed", 0);
        animator.Play("Locomocao", 0, 0);
        animator.Update(0);
        animator.applyRootMotion = false;

        var graph = PlayableGraph.Create("Raw police car clip calibration");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0);
            playable.SetDuration(double.PositiveInfinity);
            var output = AnimationPlayableOutput.Create(graph, "Raw Humanoid", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            for (int index = 0; index <= SampleIntervals; index++)
            {
                float normalizedTime = index / (float)SampleIntervals;
                double seconds = normalizedTime * (double)clip.length;
                playable.SetTime(seconds);
                playable.SetDone(false);
                graph.Evaluate(0);
                var frame = CaptureFrame(animator, playerRoot, avatarRoot, normalizedTime, seconds, playable.GetTime());
                if (data.frames.Count > 0)
                {
                    frame.hipsDisplacementFromClipStart = frame.hipsGeometry.origin - data.frames[0].hipsGeometry.origin;
                    frame.hipsYawFromClipStart = Mathf.DeltaAngle(data.frames[0].hipsGeometry.yawDegrees, frame.hipsGeometry.yawDegrees);
                }
                data.frames.Add(frame);
            }
        }
        finally { if (graph.IsValid()) graph.Destroy(); }
    }

    static ImportData CaptureImport(string path, string clipName)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!importer) return null;
        var result = new ImportData { animationType = importer.animationType.ToString(), avatarSetup = importer.avatarSetup.ToString() };
        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        var settings = clips.FirstOrDefault(x => x.name == clipName) ?? (clips.Length == 1 ? clips[0] : null);
        if (settings == null) return result;
        result.foundClipSettings = true;
        result.lockRootRotation = settings.lockRootRotation;
        result.lockRootHeightY = settings.lockRootHeightY;
        result.lockRootPositionXZ = settings.lockRootPositionXZ;
        result.keepOriginalOrientation = settings.keepOriginalOrientation;
        result.keepOriginalPositionY = settings.keepOriginalPositionY;
        result.keepOriginalPositionXZ = settings.keepOriginalPositionXZ;
        result.loopTime = settings.loopTime;
        result.rotationOffset = settings.rotationOffset;
        result.heightOffset = settings.heightOffset;
        return result;
    }

    static Frame CaptureFrame(Animator animator, Transform playerRoot, Transform avatarRoot, float normalizedTime, double requestedTime, double playableTime)
    {
        var frame = new Frame
        {
            normalizedTime = normalizedTime, requestedTimeSeconds = requestedTime, playableTimeSeconds = playableTime,
            playerRoot = CaptureTransform(playerRoot), avatarRoot = CaptureTransform(avatarRoot),
            animatorRootPosition = animator.rootPosition, animatorRootRotation = animator.rootRotation,
            bodyPosition = animator.bodyPosition, bodyRotation = animator.bodyRotation,
            bodyForward = animator.bodyRotation * Vector3.forward, bodyRight = animator.bodyRotation * Vector3.right,
            deltaPosition = animator.deltaPosition, deltaRotation = animator.deltaRotation
        };
        foreach (var bone in MeasuredBones)
        {
            var transform = animator.GetBoneTransform(bone);
            var measurement = new BoneData { bone = bone.ToString(), found = transform };
            if (transform)
            {
                measurement.worldPosition = transform.position;
                measurement.playerRelativePosition = playerRoot.InverseTransformPoint(transform.position);
                measurement.avatarRelativePosition = avatarRoot.InverseTransformPoint(transform.position);
                measurement.worldRotation = transform.rotation;
                measurement.playerRelativeRotation = Quaternion.Inverse(playerRoot.rotation) * transform.rotation;
            }
            frame.bones.Add(measurement);
        }
        var feet = frame.bones.Where(x => x.found && (x.bone.EndsWith("Foot", StringComparison.Ordinal) || x.bone.EndsWith("Toes", StringComparison.Ordinal))).ToArray();
        frame.minimumFootOrToeBoneY = feet.Length > 0 ? feet.Min(x => x.worldPosition.y) : 0;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        var head = animator.GetBoneTransform(HumanBodyBones.Neck) ?? animator.GetBoneTransform(HumanBodyBones.Head);
        frame.hipsGeometry = Geometry(hips, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), chest);
        frame.chestGeometry = Geometry(chest,
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder) ?? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
            animator.GetBoneTransform(HumanBodyBones.RightShoulder) ?? animator.GetBoneTransform(HumanBodyBones.RightUpperArm), head);
        return frame;
    }

    static TransformData CaptureTransform(Transform transform)
    {
        return new TransformData
        {
            worldPosition = transform.position, worldRotation = transform.rotation,
            localPosition = transform.localPosition, localRotation = transform.localRotation,
            localScale = transform.localScale, lossyScale = transform.lossyScale
        };
    }

    static OrientationData Geometry(Transform origin, Transform left, Transform right, Transform upper)
    {
        var data = new OrientationData { origin = origin ? origin.position : Vector3.zero };
        if (!origin || !left || !right || !upper) return data;
        Vector3 across = right.position - left.position;
        Vector3 upward = upper.position - origin.position;
        if (across.sqrMagnitude < .000001f || upward.sqrMagnitude < .000001f) return data;
        data.right = across.normalized;
        Vector3 forward = Vector3.Cross(data.right, upward);
        if (forward.sqrMagnitude < .000001f) return data;
        data.forward = forward.normalized;
        data.up = Vector3.Cross(data.forward, data.right).normalized;
        data.yawDegrees = Mathf.Atan2(data.forward.x, data.forward.z) * Mathf.Rad2Deg;
        data.valid = true;
        return data;
    }
}
