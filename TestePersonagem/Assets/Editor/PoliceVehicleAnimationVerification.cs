using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PoliceVehicleAnimationVerification
{
    static double nextRequestCheck;
    static PoliceVehicleAnimationVerification()
    {EditorApplication.update+=CheckRequestedVerification;}
    static void CheckRequestedVerification()
    {
        if(EditorApplication.timeSinceStartup<nextRequestCheck)return;
        nextRequestCheck=EditorApplication.timeSinceStartup+1;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        string request=Path.GetFullPath("../tools/police-car-animation/run-verification.request");
        if(!File.Exists(request))return;
        File.Delete(request);
        EditorApplication.delayCall+=()=>{try{Run();}catch(Exception error){Debug.LogException(error);}};
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public List<string> checks=new List<string>();
        public string error;
        public float carLength,carWidth,carHeight,standingHipHeight,seatedHipHeight,headTop;
        public float roofMinimumY,roofMaximumY;
        public bool roofMeshFound;
    }
    static readonly MethodInfo EvaluatePose=typeof(VehicleSeatedPose).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
    static void Check(Report report,bool pass,string text)
    {if(!pass)throw new Exception(text);report.checks.Add(text);}

    [MenuItem("Horizonte/Testes locais/Validar animação da viatura")]
    public static void Run()
    {
        string folder=Path.GetFullPath("../tools/police-car-animation");Directory.CreateDirectory(folder);
        var report=new Report();PreviewRenderUtility preview=null;GameObject root=null;
        string[] previousBuildScenes=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray();
        var generated=new List<UnityEngine.Object>();
        try
        {
            Check(report,!EditorApplication.isPlayingOrWillChangePlaymode,"Verification runs outside Play Mode");
            var source=Resources.Load<GameObject>("Vehicles/Police/PoliceCar");
            Check(report,source,"PoliceCar resource exists");
            preview=new PreviewRenderUtility();
            root=new GameObject("Prévia isolada da viatura"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);
            var car=new GameObject("Viatura de verificação");car.transform.SetParent(root.transform,false);
            var rig=PoliceCarRig.Create(car.transform,source);
            report.carLength=rig.Dimensions.z;report.carWidth=rig.Dimensions.x;report.carHeight=rig.Dimensions.y;
            Check(report,Mathf.Abs(rig.Dimensions.z-4.55f)<.04f,"Police model normalized to 4.55 metres with +Z toward the hood");
            Check(report,rig.Dimensions.x>1.5f&&rig.Dimensions.x<2.4f,"Police model retains plausible full-size width");
            Check(report,rig.HasDoors,"Driver and rear passenger doors have reversible runtime hinges");
            Check(report,rig.DriverDoor.x<rig.DriverSeat.x&&rig.RearDoor.x>rig.RearSeat.x,"Door access points remain outside the correct side of each seat");
            var hinge=car.transform.Find("Dobradiça door_lf");
            Check(report,hinge,"Front-left hinge exists");
            Quaternion closed=hinge.localRotation;
            rig.SetDoor(true,1);
            Check(report,Quaternion.Angle(closed,hinge.localRotation)>60&&rig.DoorOpenAmount(true)==1,"Driver door opens smoothly through 68 degrees");
            rig.SetDoor(true,0);
            Check(report,Quaternion.Angle(closed,hinge.localRotation)<.01f,"Driver door closes back to its original position");
            var wheel=car.GetComponentsInChildren<Transform>().FirstOrDefault(x=>x.name.StartsWith("Rolagem ",StringComparison.Ordinal));
            Check(report,wheel,"Wheel rolling pivots exist");
            Quaternion parkedWheel=wheel.localRotation;rig.RotateWheels(.4f,.25f);
            Check(report,Quaternion.Angle(parkedWheel,wheel.localRotation)>1,"Driving rotates the visible tyres");

            var model=CreateAvatar(root.transform,out var animator,generated);
            var pose=model.AddComponent<VehicleSeatedPose>();pose.SetBlend(0,true);
            Check(report,pose.Ready,"Current humanoid avatar supports the seated pose");
            report.standingHipHeight=pose.StandingHipHeight;report.seatedHipHeight=pose.SeatedHipHeight;
            Check(report,pose.StandingHipHeight>.5f&&pose.StandingHipHeight<1.3f,"The current Gangster idle clip produces an anatomically valid standing pelvis height");
            Check(report,pose.StandingHipHeight>pose.SeatedHipHeight+.1f,"Sitting lowers the pelvis without shrinking the character");
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            var foot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Vector3 standingHips=hips.localPosition;Quaternion standingThigh=upper.localRotation;
            pose.SetBlend(1,true);EvaluatePose.Invoke(pose,null);
            Check(report,Vector3.Dot((lower.position-upper.position).normalized,model.transform.forward)>.8f,"Seated thighs extend naturally toward the pedals");
            Check(report,Vector3.Dot((foot.position-lower.position).normalized,-model.transform.up)>.8f,"Seated knees bend downward rather than standing through the floor");
            Vector3 firstHips=hips.localPosition;Quaternion firstThigh=upper.localRotation;
            for(int frame=0;frame<60;frame++)EvaluatePose.Invoke(pose,null);
            Check(report,(hips.localPosition-firstHips).sqrMagnitude<.000001f&&Quaternion.Angle(firstThigh,upper.localRotation)<.05f,"Sixty pose evaluations do not accumulate bone rotations or pelvis drift");
            pose.SetBlend(0,true);
            Check(report,(hips.localPosition-standingHips).sqrMagnitude<.000001f&&Quaternion.Angle(standingThigh,upper.localRotation)<.05f,"Leaving restores the original walking pose");
            pose.ReachDoor(1,hips.position+model.transform.forward*.35f-model.transform.right*.45f);
            EvaluatePose.Invoke(pose,null);
            Check(report,Finite(animator.GetBoneTransform(HumanBodyBones.LeftHand).position),"Door-handle reach keeps a valid humanoid pose");
            pose.ReachDoor(0,Vector3.zero);

            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Piso da prévia";ground.transform.SetParent(root.transform,false);
            ground.transform.localPosition=new Vector3(0,-.1f,0);ground.transform.localScale=new Vector3(13,.2f,13);
            var groundMaterial=new Material(Shader.Find("Standard")){color=new Color(.13f,.16f,.19f)};generated.Add(groundMaterial);
            ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
            model.transform.position=car.transform.TransformPoint(rig.DriverSeat)-Vector3.up*pose.SeatedHipHeight;
            pose.SetBlend(1,true);EvaluatePose.Invoke(pose,null);
            report.headTop=HeadTop(animator);
            var roof=car.GetComponentsInChildren<Renderer>().Where(x=>x.name.IndexOf("roof",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            report.roofMeshFound=roof.Length>0;
            if(roof.Length>0)
            {
                report.roofMinimumY=roof.Min(x=>x.bounds.min.y);report.roofMaximumY=roof.Max(x=>x.bounds.max.y);
            }
            rig.SetDoor(true,1);
            Render(preview,car.transform,new Vector3(-4.8f,2.1f,4.5f),"door-preview.png",folder);
            rig.SetDoor(true,0);
            Render(preview,car.transform,new Vector3(-3.8f,1.7f,3.5f),"seated-preview.png",folder);
            if(report.roofMeshFound)Check(report,report.headTop<report.roofMaximumY+.02f,"Driver head stays below the cabin roof");
            Check(report,previousBuildScenes.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Verification leaves build scenes unchanged");
            report.passed=true;
            Debug.Log("POLICE_VEHICLE_ANIMATION_VERIFIED checks="+report.checks.Count);
        }
        catch(Exception error){report.error=error.ToString();Debug.LogError("POLICE_VEHICLE_ANIMATION_FAILED "+error.Message);throw;}
        finally
        {
            if(preview!=null)preview.Cleanup();else if(root)UnityEngine.Object.DestroyImmediate(root);
            foreach(var item in generated)if(item)UnityEngine.Object.DestroyImmediate(item);
            File.WriteAllText(Path.Combine(folder,"unity-validation.json"),JsonUtility.ToJson(report,true));
        }
    }

    static GameObject CreateAvatar(Transform parent,out Animator animator,List<UnityEngine.Object> generated)
    {
        var source=Resources.Load<GameObject>("GangsterMusculo/GangsterMusculo");
        if(!source)throw new Exception("Current Gangster humanoid resource is absent");
        var model=UnityEngine.Object.Instantiate(source,parent,false);model.name="Motorista em escala real";
        var imported=model.GetComponentInChildren<Animator>();
        var avatar=imported?imported.avatar:Resources.LoadAll<Avatar>("GangsterMusculo/GangsterMusculo").FirstOrDefault(x=>x&&x.isHuman);
        animator=model.GetComponent<Animator>()??model.AddComponent<Animator>();
        if(imported&&imported!=animator){imported.enabled=false;UnityEngine.Object.DestroyImmediate(imported);}
        animator.avatar=avatar;
        var controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
        var overrides=new AnimatorOverrideController(controller);generated.Add(overrides);
        var clips=new List<KeyValuePair<AnimationClip,AnimationClip>>();overrides.GetOverrides(clips);
        var paths=new Dictionary<string,string>
        {
            {"IdleRetarget","GangsterMusculo/GangsterMusculo"},
            {"Andar_WalkingOriginal","GangsterMusculo/Animations/Walking"},
            {"Correr_FastRunEnviado","GangsterMusculo/Animations/Running"},
            {"JumpRetarget","GangsterMusculo/Animations/Jump"}
        };
        bool idleReplaced=false;
        for(int index=0;index<clips.Count;index++)
        {
            var original=clips[index].Key;
            if(!original||!paths.TryGetValue(original.name,out var path))continue;
            var replacement=Resources.LoadAll<AnimationClip>(path).FirstOrDefault(x=>x&&x.length>0&&x.humanMotion&&!x.name.StartsWith("__preview"));
            if(!replacement)throw new Exception("Current Gangster animation resource is absent: "+path);
            clips[index]=new KeyValuePair<AnimationClip,AnimationClip>(original,replacement);
            if(original.name=="IdleRetarget")idleReplaced=true;
        }
        if(!idleReplaced)throw new Exception("Current Gangster idle override was not applied");
        overrides.ApplyOverrides(clips);animator.runtimeAnimatorController=overrides;
        animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.updateWhenOffscreen=true;
        var renderers=model.GetComponentsInChildren<Renderer>();
        var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
        if(bounds.size.y>.01f)model.transform.localScale*=1.8f/bounds.size.y;
        var surfaces=new Dictionary<string,string[]>
        {
            {"Gangsta2_HIK_Dealer_Musculo_Cloth",new[]{"Dealerguy_El_Musculo_Male_Pants_B","Dealerguy_El_Musculo_Male_Pants_N"}},
            {"Gangsta2_HIK_Dealer_Musculo_Body",new[]{"Dealerguy_El_Musculo_Male_Body_B","Dealerguy_El_Musculo_Male_Body_N"}},
            {"Gangsta2_HIK_Dealer_Musculo_Head",new[]{"Dealerguy_El_Musculo_Male_Face_B","Dealerguy_El_Musculo_Male_Face_N"}}
        };
        foreach(var renderer in renderers)
        {
            var materials=renderer.sharedMaterials;
            for(int index=0;index<materials.Length;index++)
            {
                var old=materials[index];if(!old||!surfaces.TryGetValue(old.name,out var names))continue;
                var material=new Material(Shader.Find("Standard")){name=old.name,mainTexture=Resources.Load<Texture2D>("GangsterMusculo/textures/"+names[0]),color=Color.white};
                material.SetTexture("_BumpMap",Resources.Load<Texture2D>("GangsterMusculo/textures/"+names[1]));material.SetFloat("_BumpScale",.6f);material.SetFloat("_Glossiness",.16f);material.EnableKeyword("_NORMALMAP");
                generated.Add(material);materials[index]=material;
            }
            renderer.sharedMaterials=materials;
        }
        animator.Rebind();animator.SetFloat("Speed",0);animator.Play("Locomocao",0,0);animator.Update(1f/60f);
        return model;
    }
    static float HeadTop(Animator animator)
    {
        var head=animator.GetBoneTransform(HumanBodyBones.Head);
        var neck=animator.GetBoneTransform(HumanBodyBones.Neck);
        return head.position.y+Vector3.Distance(head.position,neck.position)*.8f;
    }
    static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&!float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
    static void Render(PreviewRenderUtility preview,Transform car,Vector3 cameraOffset,string name,string folder)
    {
        preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.035f,.055f,.085f);
        preview.camera.fieldOfView=38;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=40;
        preview.camera.transform.position=car.TransformPoint(cameraOffset);
        preview.camera.transform.LookAt(car.TransformPoint(new Vector3(0,.85f,0)));
        preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
        preview.lights[1].intensity=.8f;preview.lights[1].transform.rotation=Quaternion.Euler(25,145,0);
        preview.BeginPreview(new Rect(0,0,1280,720),GUIStyle.none);preview.Render(true);
        var image=preview.EndPreview() as RenderTexture;
        if(!image)throw new Exception("Preview render texture is absent");
        var previous=RenderTexture.active;RenderTexture.active=image;
        var texture=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,image.width,image.height),0,0);texture.Apply();
        File.WriteAllBytes(Path.Combine(folder,name),texture.EncodeToPNG());
        RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);
    }

    [MenuItem("Horizonte/Testes locais/Prévia da viatura animada")]
    public static void OpenPreview()
    {OpenPreviewScene(true);}
    public static void OpenPreviewScene(bool promptToSave)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
        {Debug.LogWarning("Pare o Play antes de abrir a prévia da viatura.");return;}
        if(promptToSave&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab");
        if(!prefab)throw new Exception("Prefab do jogador ausente");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Piso da pista local";
        floor.transform.position=new Vector3(0,-.15f,0);floor.transform.localScale=new Vector3(40,.3f,40);
        floor.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.22f,.25f,.28f)};
        var sun=new GameObject("Luz da prévia").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
        var playerObject=PrefabUtility.InstantiatePrefab(prefab) as GameObject;playerObject.name="Jogador da prévia local";
        playerObject.transform.SetPositionAndRotation(new Vector3(-1.95f,.04f,.35f),Quaternion.Euler(0,90,0));
        var player=playerObject.GetComponent<ThirdPersonPlayer>();player.spawnPosition=playerObject.transform.position;player.spawnYaw=90;
        var cameraObject=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener),typeof(ThirdPersonCamera));cameraObject.tag="MainCamera";
        var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.21f,.3f);camera.farClipPlane=100;
        var orbit=cameraObject.GetComponent<ThirdPersonCamera>();orbit.target=playerObject.transform;orbit.yaw=135;orbit.pitch=18;orbit.distance=5;
        player.cameraTransform=cameraObject.transform;
        var setup=new GameObject("Prévia offline da viatura").AddComponent<PoliceVehiclePreview>();setup.player=player;setup.carPosition=Vector3.zero;
        Selection.activeGameObject=setup.gameObject;
        SceneView.lastActiveSceneView?.Frame(new Bounds(Vector3.zero,new Vector3(9,4,9)),false);
        Debug.Log("POLICE_VEHICLE_PREVIEW_READY · Cena de teste não salva; pressione Play. E entrar, WASD dirigir, F sair. Nenhum login ou build necessário.");
    }
}
