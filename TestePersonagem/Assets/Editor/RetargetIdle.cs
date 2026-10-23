using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

public static class RetargetIdle
{
    const string SourcePath = "Assets/Animations/Source/Idle.fbx";
    const string OutputPath = "Assets/Animations/IdleRetarget.anim";
    // Each correspondence also identifies a child to align the differing rest poses.
    static string[][] pairs = {
        new[]{" Pelvis", "Hips", "", ""},
        new[]{" Spine", "Spine", " Spine1", "Spine2"},
        new[]{" Spine1", "Spine2", " Neck", "Neck"},
        new[]{" Neck", "Neck", " Head", "Head"},
        new[]{" Head", "Head", "", ""},
        new[]{"Bip01 L Clavicle", "LeftShoulder", " L UpperArm", "LeftArm"},
        new[]{" L UpperArm", "LeftArm", " L ForeArm", "LeftForeArm"},
        new[]{" L ForeArm", "LeftForeArm", " L Hand", "LeftHand"},
        new[]{" L Hand", "LeftHand", " L Finger", "LeftHandMiddle1"},
        new[]{"Bip01 R Clavicle", "RightShoulder", " R UpperArm", "RightArm"},
        new[]{" R UpperArm", "RightArm", " R ForeArm", "RightForeArm"},
        new[]{" R ForeArm", "RightForeArm", " R Hand", "RightHand"},
        new[]{" R Hand", "RightHand", " R Finger", "RightHandMiddle1"},
        new[]{" L Thigh", "LeftUpLeg", " L Calf", "LeftLeg"},
        new[]{" L Calf", "LeftLeg", " L Foot", "LeftFoot"},
        new[]{" L Foot", "LeftFoot", " L Toe0", "LeftToeBase"},
        new[]{" L Toe0", "LeftToeBase", "", ""},
        new[]{" R Thigh", "RightUpLeg", " R Calf", "RightLeg"},
        new[]{" R Calf", "RightLeg", " R Foot", "RightFoot"},
        new[]{" R Foot", "RightFoot", " R Toe0", "RightToeBase"},
        new[]{" R Toe0", "RightToeBase", "", ""}
    };

    [MenuItem("Teste/Aplicar Idle enviado")]
    public static void Apply()
    {
        Transfer(SourcePath,OutputPath,0,"idle");
    }

    [MenuItem("Teste/Aplicar Fast Run enviado")]
    public static void ApplyRun()
    {
        Transfer("Assets/Animations/Source/Fast Run.fbx","Assets/Animations/FastRunRetarget.anim",2,"fast-run");
    }

    [MenuItem("Teste/Aplicar Walking Left Turn enviado")]
    public static void ApplyWalk()
    {
        Transfer("Assets/Animations/Source/Walking Left Turn.fbx","Assets/Animations/WalkingRetarget.anim",1,"walking");
    }

    [MenuItem("Teste/Aplicar Jump enviado")]
    public static void ApplyJump()
    {
        Transfer("Assets/Animations/Source/Jump.fbx","Assets/Animations/JumpRetarget.anim",3,"jump");
    }

    static void Transfer(string SourcePath,string OutputPath,int childIndex,string reportPrefix)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        AssetDatabase.Refresh();
        var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath));
        source.name = "IdleSourceTemporary";
        var target = UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>().animator.gameObject;
        // Use the original skin rest pose rather than the lowered-arm prefab pose.
        var restModel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        restModel.name = "RetargetTemporary";
        RestoreBindPose(source);
        RestoreBindPose(restModel);
        var src = source.GetComponentsInChildren<Transform>().ToDictionary(t => t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name, t => t);
        var dst = restModel.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
        // Some supplied animations already carry the GTA skin skeleton rather than Mixamo names.
        if (!src.ContainsKey("Hips") && src.ContainsKey(" Pelvis"))
        {
            foreach (var p in pairs)
            {
                src[p[1]]=src[p[0]];
                if(p[2]!="") src[p[3]]=src[p[2]];
            }
        }
        var srcRest = pairs.ToDictionary(p => p[0], p => src[p[1]].rotation);
        var dstRest = pairs.ToDictionary(p => p[0], p => dst[p[0]].rotation);
        var alignment = new Dictionary<string, Quaternion>();
        foreach (var p in pairs)
        {
            alignment[p[0]] = p[2] == "" ? Quaternion.identity : Quaternion.FromToRotation(
                dst[p[2]].position - dst[p[0]].position, src[p[3]].position - src[p[1]].position);
        }
        float sourceLeg = Vector3.Distance(src["LeftUpLeg"].position,src["LeftLeg"].position)+Vector3.Distance(src["LeftLeg"].position,src["LeftFoot"].position);
        float targetLeg = Vector3.Distance(dst[" L Thigh"].position,dst[" L Calf"].position)+Vector3.Distance(dst[" L Calf"].position,dst[" L Foot"].position);
        float scale = targetLeg / sourceLeg;
        File.WriteAllText("../tools/"+reportPrefix+"-bind-diagnostic.txt","SourceLeg="+sourceLeg+"\nTargetLeg="+targetLeg+"\nScale="+scale+"\nSourceHip="+src["Hips"].position+"\nSourceFoot="+src["LeftFoot"].position+"\nTargetHip="+dst[" Pelvis"].position+"\nTargetFoot="+dst[" L Foot"].position);
        Vector3 pelvisRest = dst[" Pelvis"].localPosition;
        Vector3 sourceBindPelvisPosition=src["Hips"].position;
        clip.SampleAnimation(source, clip.length);
        Vector3 srcEnd = src["Hips"].position;
        Quaternion srcEndRotation = src["Hips"].rotation;
        clip.SampleAnimation(source, 0);
        Vector3 srcStart = src["Hips"].position;
        Quaternion rotationDelta=srcEndRotation*Quaternion.Inverse(src["Hips"].rotation);
        float headingTurn=childIndex==1?Vector3.SignedAngle(Vector3.forward,Vector3.ProjectOnPlane(rotationDelta*Vector3.forward,Vector3.up),Vector3.up):0;
        Vector3 travel = childIndex > 0 ? Vector3.ProjectOnPlane(srcEnd-srcStart,Vector3.up) : Vector3.zero;
        bool jump=childIndex==3;
        float sampleStart=jump?.65f:0;
        float duration=jump?Mathf.Min(1.85f,clip.length)-sampleStart:clip.length;
        float seamDuration = childIndex >= 2 ? 0 : childIndex == 1 ? .12f : .2f;
        int frames = Mathf.CeilToInt(duration * 60);
        var rotations = pairs.ToDictionary(p => p[0], p => new List<Quaternion>());
        var positions = new List<Vector3>();
        for (int frame = 0; frame <= frames; frame++)
        {
            float time = duration * frame / frames;
            float sourceTime=sampleStart+time;
            clip.SampleAnimation(source, sourceTime);
            Quaternion headingCorrection=Quaternion.identity;
            Quaternion upperBodyCorrection=Quaternion.identity;
            if(childIndex==1)
            {
                Quaternion hipsDelta=src["Hips"].rotation*Quaternion.Inverse(srcRest[" Pelvis"]);
                // Turning clips steer and bank the whole body. Keep only a small forward
                // inclination for straight walking, rather than retaining that bank/twist.
                headingCorrection=StraightWalkingRotation(hipsDelta,2f)*Quaternion.Inverse(hipsDelta);
                Quaternion chestDelta=headingCorrection*src["Spine2"].rotation*Quaternion.Inverse(srcRest[" Spine1"]);
                upperBodyCorrection=StraightWalkingRotation(chestDelta,4f)*Quaternion.Inverse(chestDelta)*headingCorrection;
            }
            foreach (var p in pairs)
            {
                bool upperBody=p[0]!=" Pelvis"&&!p[0].EndsWith("Thigh")&&!p[0].EndsWith("Calf")&&!p[0].EndsWith("Foot")&&!p[0].EndsWith("Toe0");
                Quaternion correction=childIndex==1&&upperBody?upperBodyCorrection:headingCorrection;
                dst[p[0]].rotation = correction * src[p[1]].rotation * Quaternion.Inverse(srcRest[p[0]]) * alignment[p[0]] * dstRest[p[0]];
                if(childIndex==1&&(p[0]==" Neck"||p[0]==" Head"))
                {
                    Quaternion delta=dst[p[0]].rotation*Quaternion.Inverse(dstRest[p[0]]);
                    dst[p[0]].rotation=StraightWalkingRotation(delta,4f)*dstRest[p[0]];
                }
                Quaternion q = dst[p[0]].localRotation;
                if (rotations[p[0]].Count > 0 && Quaternion.Dot(q, rotations[p[0]][frame - 1]) < 0)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rotations[p[0]].Add(q);
            }
            Vector3 displacement=src["Hips"].position-srcStart-travel*(time/clip.length);
            if(childIndex>0)
            {
                // In-place locomotion: retain vertical body motion at its authored height,
                // without residual forward/backward drift on top of CharacterController movement.
                displacement=Vector3.up*(src["Hips"].position.y-sourceBindPelvisPosition.y);
                if(jump)
                {
                    // The CharacterController supplies the flight arc; retain the authored crouch,
                    // knee tuck and landing without adding a second rise from the animation.
                    float flight=Mathf.Clamp01((sourceTime-.76f)/.55f);
                    displacement-=Vector3.up*(.30f*4f*flight*(1f-flight));
                }
            }
            positions.Add(pelvisRest + dst[" Pelvis"].parent.InverseTransformVector(displacement * scale));
        }
        var output = new AnimationClip { name = jump?"Pular_JumpEnviado":childIndex == 2 ? "Correr_FastRunEnviado" : childIndex == 1 ? "Andar_WalkingLeftTurnEnviado" : "Parado_IdleEnviado", frameRate = 60 };
        foreach (var p in pairs)
        {
            var curves = new[]{new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve()};
            for (int frame = 0; frame <= frames; frame++)
            {
                float time = duration * frame / frames;
                float blend = seamDuration>0?Mathf.SmoothStep(0, 1, Mathf.InverseLerp(duration - seamDuration, duration, time)):0;
                Quaternion q = Quaternion.Slerp(rotations[p[0]][frame], rotations[p[0]][0], blend);
                curves[0].AddKey(time,q.x); curves[1].AddKey(time,q.y); curves[2].AddKey(time,q.z); curves[3].AddKey(time,q.w);
            }
            // Target uses the same hierarchy as the exported rest model.
            string path = AnimationUtility.CalculateTransformPath(dst[p[0]], restModel.transform);
            for (int i = 0; i < 4; i++)
            {
                if(jump)SmoothNonLooping(curves[i]);else SmoothPeriodic(curves[i]);
                AnimationUtility.SetEditorCurve(output, EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i]),curves[i]);
            }
        }
        for (int axis = 0; axis < 3; axis++)
        {
            var curve = new AnimationCurve();
            for (int frame = 0; frame <= frames; frame++)
            {
                float time = duration * frame / frames;
                float blend = seamDuration>0?Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration - seamDuration,duration,time)):0;
                curve.AddKey(time,Vector3.Lerp(positions[frame],positions[0],blend)[axis]);
            }
            if(jump)SmoothNonLooping(curve);else SmoothPeriodic(curve);
            AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(dst[" Pelvis"],restModel.transform),typeof(Transform),"m_LocalPosition."+"xyz"[axis]),curve);
        }
        output.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(output);settings.loopTime = !jump; settings.loopBlend = !jump;
        AnimationUtility.SetAnimationClipSettings(output,settings);
        // Keep the existing asset identity when updating this conversion.
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath);
        if (existing) { EditorUtility.CopySerialized(output,existing); UnityEngine.Object.DestroyImmediate(output); output=existing; }
        else AssetDatabase.CreateAsset(output,OutputPath);
        var controller = (AnimatorController)target.GetComponent<Animator>().runtimeAnimatorController;
        var tree = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
        var children = tree.children;
        if(!jump)children[childIndex].motion = output;
        if(childIndex>0&&!jump)
        {
            var movement=target.GetComponentInParent<ThirdPersonPlayer>();
            float authoredSpeed=travel.magnitude*scale/clip.length;
            float desiredSpeed=childIndex==2?movement.runSpeed:movement.walkSpeed;
            children[childIndex].timeScale=authoredSpeed>.1f?Mathf.Clamp(desiredSpeed/authoredSpeed,.4f,2f):1;
        }
        tree.children = children;
        if(jump)
        {
            if(!controller.parameters.Any(p=>p.name=="Jumping"))controller.AddParameter("Jumping",AnimatorControllerParameterType.Bool);
            var machine=controller.layers[0].stateMachine;
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Jump")??machine.AddState("Jump");
            state.motion=output;state.speed=1.05f;state.writeDefaultValues=true;
            var locomotion=machine.defaultState;
            foreach(var transition in locomotion.transitions.Where(t=>t.destinationState==state).ToArray())locomotion.RemoveTransition(transition);
            foreach(var transition in state.transitions.ToArray())state.RemoveTransition(transition);
            var enter=locomotion.AddTransition(state);enter.hasExitTime=false;enter.duration=.075f;
            enter.AddCondition(AnimatorConditionMode.If,0,"Jumping");
            var exit=state.AddTransition(locomotion);exit.hasExitTime=false;exit.duration=.14f;
            exit.AddCondition(AnimatorConditionMode.IfNot,0,"Jumping");
            EditorUtility.SetDirty(state);EditorUtility.SetDirty(machine);
        }
        EditorUtility.SetDirty(tree);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(output);
        UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(restModel);
        AssetDatabase.SaveAssets();
        File.WriteAllText("../tools/"+reportPrefix+"-retarget-result.txt", "RETARGET_OK\nSource="+SourcePath+"\nDuration="+output.length+"\nMappedBones="+pairs.Length+"\nCurveCount="+AnimationUtility.GetCurveBindings(output).Length+"\nOutput="+OutputPath+"\nRemovedHorizontalTravel="+travel+"\nRemovedHeadingTurn="+headingTurn);
        var targetTransforms=target.GetComponentsInChildren<Transform>();
        var savedRotations=targetTransforms.Select(t=>t.localRotation).ToArray();
        var savedPositions=targetTransforms.Select(t=>t.localPosition).ToArray();
        output.SampleAnimation(target,output.length*.25f);
        var skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh();skin.BakeMesh(baked);
        var view = new GameObject("PreviewBaked",typeof(MeshFilter),typeof(MeshRenderer));
        view.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);view.transform.localScale=skin.transform.lossyScale;
        view.GetComponent<MeshFilter>().sharedMesh=baked;view.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
        var camera=Camera.main;var savedCameraPosition=camera.transform.position;var savedCameraRotation=camera.transform.rotation;
        camera.transform.SetPositionAndRotation(new Vector3(2.3f,1.5f,3.6f),Quaternion.LookRotation(new Vector3(0,.95f,0)-new Vector3(2.3f,1.5f,3.6f)));
        var rt=new RenderTexture(1000,750,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(1000,750,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,750),0,0);image.Apply();File.WriteAllBytes("../tools/"+reportPrefix+"-retarget-preview.png",image.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;rt.Release();
        skin.enabled=true;UnityEngine.Object.DestroyImmediate(view);UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);
        camera.transform.SetPositionAndRotation(savedCameraPosition,savedCameraRotation);
        for(int i=0;i<targetTransforms.Length;i++){targetTransforms[i].localRotation=savedRotations[i];targetTransforms[i].localPosition=savedPositions[i];}
        Debug.Log("IDLE_RETARGET_OK");
    }

    public static void Verify()
    {
        VerifyClip(OutputPath,0,"idle");
    }

    public static void VerifyRun()
    {
        VerifyClip("Assets/Animations/FastRunRetarget.anim",2,"fast-run");
    }

    public static void VerifyAll()
    {
        VerifyClip(SourcePath.Replace("Source/Idle.fbx","IdleRetarget.anim"),0,"idle");
        VerifyClip("Assets/Animations/WalkingRetarget.anim",1,"walking");
        VerifyClip("Assets/Animations/FastRunRetarget.anim",2,"fast-run");
    }

    public static void VerifyAndPlay()
    {
        VerifyAll();
        PrototypePlayVerification.Start();
    }

    static void SmoothPeriodic(AnimationCurve curve)
    {
        var keys=curve.keys;
        int last=keys.Length-1;
        // A periodic central derivative preserves velocity through keys and the loop boundary.
        for(int i=0;i<=last;i++)
        {
            int previous=i==0||i==last?last-1:i-1;
            int next=i==0||i==last?1:i+1;
            float interval=i==0||i==last?(keys[last].time-keys[previous].time)+(keys[next].time-keys[0].time):keys[next].time-keys[previous].time;
            float tangent=(keys[next].value-keys[previous].value)/interval;
            keys[i].inTangent=tangent;keys[i].outTangent=tangent;keys[i].weightedMode=WeightedMode.None;
        }
        curve.keys=keys;
    }

    static void SmoothNonLooping(AnimationCurve curve)
    {
        for(int i=0;i<curve.length;i++)curve.SmoothTangents(i,0);
    }

    static Quaternion StraightWalkingRotation(Quaternion delta,float maxPitch)
    {
        Vector3 forward=delta*Vector3.forward;
        float pitch=-Mathf.Atan2(forward.y,new Vector2(forward.x,forward.z).magnitude)*Mathf.Rad2Deg;
        return Quaternion.AngleAxis(Mathf.Clamp(pitch,-maxPitch,maxPitch),Vector3.right);
    }

    public static void FixWalkingPosture()
    {
        ApplyWalk();VerifyAll();VerifyQuality();
        PrototypePlayVerification.Start();
    }

    public static void BuildVerifiedPosture()
    {
        VerifyQuality();
        PrototypeSetup.BuildWindows();
    }

    static void RestoreBindPose(GameObject model)
    {
        var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();
        if(!skin||!skin.sharedMesh)throw new Exception("Modelo sem bind poses: "+model.name);
        var poses=skin.sharedMesh.bindposes;
        var bones=skin.bones;
        var world=new Dictionary<Transform,Matrix4x4>();
        for(int i=0;i<poses.Length;i++)world[bones[i]]=skin.transform.localToWorldMatrix*poses[i].inverse;
        // FBX animation models can instantiate in their first animation pose, not their skin bind pose.
        foreach(var bone in bones.OrderBy(b=>AnimationUtility.CalculateTransformPath(b,model.transform).Count(c=>c=='/')))
        {
            Matrix4x4 matrix=world[bone];
            bone.SetPositionAndRotation(matrix.GetColumn(3),matrix.rotation);
        }
    }

    public static void FixLocomotion()
    {
        ApplyRun();
        ApplyWalk();
        VerifyAll();
        VerifyQuality();
        PrototypePlayVerification.Start();
    }

    public static void VerifyQuality()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        var root=UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>().animator.gameObject;
        var bones=root.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx");
        var rest=(GameObject)PrefabUtility.InstantiatePrefab(model);
        var restBones=rest.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        Quaternion restHip=restBones[" Pelvis"].rotation;
        var lines=new List<string>();
        foreach(var path in new[]{"Assets/Animations/WalkingRetarget.anim","Assets/Animations/FastRunRetarget.anim"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            float maxYaw=0,maxDrift=0,maxTangentMismatch=0,maxTorsoYaw=0,maxTorsoRoll=0;
            Vector3? first=null;
            for(int frame=0;frame<=240;frame++)
            {
                clip.SampleAnimation(root,clip.length*frame/240f);
                Quaternion delta=bones[" Pelvis"].rotation*Quaternion.Inverse(restHip);
                float yaw=Mathf.Abs(Vector3.SignedAngle(Vector3.forward,Vector3.ProjectOnPlane(delta*Vector3.forward,Vector3.up),Vector3.up));
                maxYaw=Mathf.Max(maxYaw,yaw);
                if(path.Contains("Walking"))
                {
                    foreach(string name in new[]{" Pelvis"," Spine1"," Neck"," Head"})
                    {
                        Quaternion bodyDelta=bones[name].rotation*Quaternion.Inverse(restBones[name].rotation);
                        Vector3 forward=bodyDelta*Vector3.forward;
                        float bodyYaw=Mathf.Abs(Vector3.SignedAngle(Vector3.forward,Vector3.ProjectOnPlane(forward,Vector3.up),Vector3.up));
                        Vector3 right=bodyDelta*Vector3.right;
                        float bodyRoll=Mathf.Abs(Mathf.Asin(Mathf.Clamp(right.y,-1,1))*Mathf.Rad2Deg);
                        maxTorsoYaw=Mathf.Max(maxTorsoYaw,bodyYaw);maxTorsoRoll=Mathf.Max(maxTorsoRoll,bodyRoll);
                    }
                }
                Vector3 p=bones[" Pelvis"].position;
                if(!first.HasValue)first=p;
                maxDrift=Mathf.Max(maxDrift,Vector3.ProjectOnPlane(p-first.Value,Vector3.up).magnitude);
            }
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,binding);var keys=curve.keys;
                maxTangentMismatch=Mathf.Max(maxTangentMismatch,Mathf.Abs(keys[0].outTangent-keys[keys.Length-1].inTangent));
            }
            if(path.Contains("Walking")&&maxYaw>1f)throw new Exception("Caminhada ainda tem desvio: "+maxYaw);
            if(path.Contains("Walking")&&(maxTorsoYaw>1f||maxTorsoRoll>1f))throw new Exception("Tronco da caminhada ainda torto: yaw="+maxTorsoYaw+", roll="+maxTorsoRoll);
            if(maxDrift>.001f||maxTangentMismatch>.01f)throw new Exception("Deriva ou descontinuidade nas curvas: "+path);
            lines.Add(path+" | maxYaw="+maxYaw+" | horizontalDrift="+maxDrift+" | loopTangentMismatch="+maxTangentMismatch+" | maxTorsoYaw="+maxTorsoYaw+" | maxTorsoRoll="+maxTorsoRoll);
        }
        UnityEngine.Object.DestroyImmediate(rest);
        File.WriteAllLines("../tools/locomotion-quality.txt",lines);
    }

    public static void RenderGaitReview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>();
        var target=player.animator.gameObject;
        var skin=target.GetComponentInChildren<SkinnedMeshRenderer>();
        var camera=Camera.main;
        camera.transform.SetPositionAndRotation(new Vector3(2.8f,1.5f,3.8f),Quaternion.LookRotation(new Vector3(0,.9f,0)-new Vector3(2.8f,1.5f,3.8f)));
        var tree=(BlendTree)((AnimatorController)player.animator.runtimeAnimatorController).layers[0].stateMachine.defaultState.motion;
        var rt=new RenderTexture(640,480,24);camera.targetTexture=rt;
        var texture=new Texture2D(640,480,TextureFormat.RGB24,false);
        var mesh=new Mesh();
        var review=new GameObject("BakedReview",typeof(MeshFilter),typeof(MeshRenderer));
        review.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);review.transform.localScale=skin.transform.lossyScale;
        review.GetComponent<MeshFilter>().sharedMesh=mesh;review.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
        skin.enabled=false;
        foreach(int index in new[]{1,2})
        {
            string folder="../tools/review-"+(index==1?"walk":"run");Directory.CreateDirectory(folder);
            var child=tree.children[index];var clip=(AnimationClip)child.motion;
            for(int frame=0;frame<60;frame++)
            {
                clip.SampleAnimation(target,(frame/30f*child.timeScale)%clip.length);
                skin.BakeMesh(mesh);
                camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,640,480),0,0);texture.Apply();
                File.WriteAllBytes(folder+"/"+frame.ToString("D3")+".png",texture.EncodeToPNG());
            }
        }
        camera.targetTexture=null;RenderTexture.active=null;rt.Release();
    }

    public static void FixAndReview()
    {
        FixLocomotionReviewOnly();
        PrototypePlayVerification.Start();
    }

    static void FixLocomotionReviewOnly()
    {
        ApplyRun();ApplyWalk();VerifyAll();VerifyQuality();RenderGaitReview();
    }

    static void VerifyClip(string OutputPath,int childIndex,string reportPrefix)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>();
        var controller=(AnimatorController)player.animator.runtimeAnimatorController;
        var tree=(BlendTree)controller.layers[0].stateMachine.defaultState.motion;
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath);
        if(tree.children[childIndex].motion!=clip || !clip.isLooping || clip.length<.2f)throw new Exception("Clip enviado nao esta configurado.");
        var skin=player.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh=new Mesh();var root=player.animator.gameObject;
        clip.SampleAnimation(root,0);skin.BakeMesh(mesh);var first=mesh.vertices;
        clip.SampleAnimation(root,clip.length*.25f);skin.BakeMesh(mesh);var middle=mesh.vertices;
        float motion=first.Zip(middle,(a,b)=>Vector3.Distance(a,b)).Max();
        clip.SampleAnimation(root,clip.length);skin.BakeMesh(mesh);var end=mesh.vertices;
        float loopError=first.Zip(end,(a,b)=>Vector3.Distance(a,b)).Max();
        if(motion<.001f || loopError>.001f)throw new Exception("Movimento ou fechamento do loop invalido.");
        File.WriteAllText("../tools/"+reportPrefix+"-validation.txt","ANIMATION_VALIDATION_OK\nController references supplied clip=true\nLoop=true\nDuration="+clip.length+"\nMeshMotion="+motion+"\nLoopError="+loopError);
        UnityEngine.Object.DestroyImmediate(mesh);
        Debug.Log("IDLE_VALIDATION_OK");
    }
}
