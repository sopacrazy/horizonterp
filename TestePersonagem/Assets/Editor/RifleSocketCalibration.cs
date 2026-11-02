using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RifleSocketCalibration
{
    [Serializable] sealed class FireSample
    {
        public float time,handSpeed,armAngularSpeed;
        public Vector3 rightHand,leftHand,barrel;
    }
    [Serializable] sealed class FireReport
    {
        public string gender;
        public float duration;
        public List<FireSample> frames=new List<FireSample>();
    }
    [MenuItem("Horizonte/Armas/Calibrar encaixes do rifle nos personagens")]
    public static void Prepare()
    {
        const string output="Assets/Resources/Weapons/Rifle/SocketCalibration.json";
        var controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
        var riflePrefab=Resources.Load<GameObject>("Weapons/Rifle/Rifle");
        if(!controller||!riflePrefab)throw new Exception("Prepare o rifle/controlador antes dos encaixes");
        var scene=EditorSceneManager.NewPreviewScene();var roots=new List<GameObject>();var entries=new List<RifleSocketPose>();
        try
        {
            foreach(string gender in new[]{"male","female"})
            {
                var root=new GameObject("Calibrar rifle "+gender);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);roots.Add(root);
                var model=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,root.transform);
                var animator=model.GetComponent<Animator>();animator.Rebind();animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                int layer=animator.GetLayerIndex("Rifle");
                int arms=animator.GetLayerIndex("RifleArms");
                if(layer<0||arms<0)throw new Exception("Camadas Rifle/RifleArms ausentes");
                for(int i=1;i<animator.layerCount;i++)animator.SetLayerWeight(i,0);
                // The arms-only Humanoid pose retargets relative to the gait
                // torso. Capture that composed pose, exactly as used at runtime.
                animator.SetLayerWeight(layer,1);animator.SetLayerWeight(arms,1);animator.SetFloat("RifleSpeed",0);
                animator.Play("Rifle.Locomotion",layer,.2f/3.1f);
                animator.Update(.0001f);animator.Update(0);
                var weapon=UnityEngine.Object.Instantiate(riflePrefab,root.transform,false).transform;
                var grip=new ShootingGrip();
                if(!grip.Configure(animator,weapon,weapon.Find("Muzzle"),root.transform))throw new Exception("Esqueleto inválido: "+gender);
                // The rifle is authored with +Z barrel and +Y up. Its trigger
                // socket sits at the calibrated palm; the reference faces +Z.
                var entry=grip.CaptureAuthoredRifle(animator,Quaternion.LookRotation(root.transform.forward,root.transform.up));
                if(entries.Exists(item=>item.avatarKey==entry.avatarKey))throw new Exception("Chave de avatar duplicada: "+entry.avatarKey);
                entries.Add(entry);
                Debug.Log("RIFLE_SOCKET_CALIBRATED gender="+gender+" key="+entry.avatarKey+" supportGap="+entry.referenceSupportGap);
                SampleFire(animator,entry,gender);
            }
            File.WriteAllText(output,JsonUtility.ToJson(new RifleSocketCalibrationData{entries=entries.ToArray()},true));
            AssetDatabase.ImportAsset(output,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        }
        finally{foreach(var root in roots)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }

    static void SampleFire(Animator animator,RifleSocketPose socket,string gender)
    {
        var clip=Array.Find(Resources.LoadAll<AnimationClip>("RifleAnimations/Shoot"),item=>item&&item.humanMotion&&!item.name.StartsWith("__preview"));
        int layer=animator.GetLayerIndex("RifleFire");
        if(!clip||layer<0)return;
        var report=new FireReport{gender=gender,duration=clip.length};
        var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
        var other=animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Vector3 previous=Vector3.zero;Quaternion previousUpper=Quaternion.identity,previousLower=Quaternion.identity;
        int count=Mathf.CeilToInt(clip.length*30);
        animator.SetLayerWeight(layer,1);
        for(int i=0;i<=count;i++)
        {
            float time=Mathf.Min(clip.length-.001f,i/30f);
            animator.Play("RifleFire.Shot",layer,time/clip.length);animator.Update(.00001f);
            report.frames.Add(new FireSample{time=time,rightHand=hand.position,leftHand=other.position,
                barrel=hand.rotation*socket.weaponInRightHand*Vector3.forward,
                handSpeed=i==0?0:Vector3.Distance(previous,hand.position)*30,
                armAngularSpeed=i==0?0:(Quaternion.Angle(previousUpper,upper.rotation)+Quaternion.Angle(previousLower,lower.rotation))*30});
            previous=hand.position;previousUpper=upper.rotation;previousLower=lower.rotation;
        }
        string folder=Path.GetFullPath("../tools/rifle-verification");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,gender+"-fire-motion.json"),JsonUtility.ToJson(report,true));
    }
}
