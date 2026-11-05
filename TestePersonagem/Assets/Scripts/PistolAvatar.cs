using UnityEngine;

// Imported GTA SA motion owns the arms and gait. The weapon follows the hand.
public sealed class PistolAvatar : MonoBehaviour
{
    public const string ReloadLayerName="SAPistolReload";
    public const string ReloadTimeParameter="SAPistolReloadProgress";
    public const string ReloadResource="SAExpanded/PistolReload";
    Animator animator;
    Transform owner,weapon,muzzle;
    ShootingGrip grip;
    int layer=-1,reloadLayer=-1;
    float weight,pitch,speed,reloadProgress=-1f;
    public float ReloadDuration {get;private set;}=1.5f;
    bool equipped,aiming,gripReady;
    public bool Ready=>animator&&layer>=0&&weapon&&gripReady;
    public bool Visible=>weapon&&weapon.gameObject.activeSelf;
    public Transform Muzzle=>muzzle;
    public float GripSurfaceGap=>grip!=null?grip.PistolGripSurfaceGap:float.PositiveInfinity;
    public void Configure(Animator target,Transform root)
    {
        if(animator==target&&owner==root&&weapon)return;
        animator=target;owner=root;layer=animator?animator.GetLayerIndex("SAPistol"):-1;
        if(!animator||!owner||layer<0)return;
        reloadLayer=animator.GetLayerIndex(ReloadLayerName);
        var reloadClip=reloadLayer>=0?System.Array.Find(Resources.LoadAll<AnimationClip>(ReloadResource),c=>c.name=="PistolReload"&&c.humanMotion):null;
        ReloadDuration=reloadClip?reloadClip.length:1.5f;
        weapon=CreateWeapon(owner);muzzle=weapon.Find("Muzzle");
        grip=new ShootingGrip();gripReady=grip.ConfigureAuthoredPistol(animator,weapon,owner);
        weapon.gameObject.SetActive(false);
    }
    public void Apply(bool value,bool aim,float aimPitch,float movement,float reload=-1f)
    {equipped=value;reloadProgress=value?reload:-1f;aiming=aim&&reloadProgress<0;pitch=Mathf.Clamp(aimPitch,-35,35);speed=movement;}
    public void PrepareFrame(float delta)
    {
        if(!Ready)return;
        bool busy=animator.TryGetComponent<CombatAvatarAnimation>(out var combat)&&combat.Busy;
        bool active=equipped&&!busy&&!animator.GetBool("Jumping");
        weight=busy||animator.GetBool("Jumping")?0:Mathf.MoveTowards(weight,active?1:0,Mathf.Clamp(delta,0,.1f)*10);
        weapon.gameObject.SetActive(active);
        animator.SetLayerWeight(layer,weight);animator.SetBool("SAPistolAim",aiming);
        animator.SetFloat("SAPistolSpeed",speed,.09f,Mathf.Max(.001f,delta));
        if(reloadLayer>=0)
        {
            bool reloading=active&&reloadProgress>=0;
            if(reloading)animator.SetFloat(ReloadTimeParameter,reloadProgress);
            // Use the same clock as the ammo refill, including when game time slows.
            float edge=Mathf.Min(reloadProgress,1f-reloadProgress)*ReloadDuration;
            animator.SetLayerWeight(reloadLayer,reloading?weight*Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/.10f)):0);
        }
    }
    public void Attach()
    {
        if(!Visible||!Ready)return;
        if(aiming)
        {
            var chest=animator.GetBoneTransform(HumanBodyBones.UpperChest)??animator.GetBoneTransform(HumanBodyBones.Chest);
            var target=Quaternion.Euler(pitch,owner.eulerAngles.y,0)*Vector3.forward;
            var barrel=Vector3.ProjectOnPlane(grip.AuthoredRifleDirection,owner.right);
            float correction=Mathf.Clamp(Vector3.SignedAngle(barrel,target,owner.right),-45,45);
            if(chest)chest.rotation=Quaternion.AngleAxis(correction,owner.right)*chest.rotation;
        }
        grip.ApplyAuthoredPistol(aiming,pitch,0);
    }
    public static Transform CreateWeapon(Transform parent)
    {
        var root=new GameObject("Pistola equipada").transform;root.SetParent(parent,false);
        var prefab=Resources.Load<GameObject>("Weapons/ColtPocket/ColtPocketHammer");
        if(!prefab)throw new System.InvalidOperationException("Modelo Colt Pocket ausente");
        var model=Instantiate(prefab,root,false).transform;model.name="Colt Pocket Hammer";
        foreach(var camera in model.GetComponentsInChildren<Camera>(true))camera.gameObject.SetActive(false);
        foreach(var light in model.GetComponentsInChildren<Light>(true))light.gameObject.SetActive(false);
        foreach(var collider in model.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        var slide=model.Find("Armature/body/slide");var end=model.Find("Armature/body/slide/slide_end");
        if(slide&&end)model.localRotation=Quaternion.FromToRotation(model.InverseTransformDirection(end.position-slide.position).normalized,Vector3.forward);
        var bounds=GeometryBounds(root);
        model.localScale*=.23f/Mathf.Max(.001f,bounds.size.z);
        var mag=model.Find("Armature/body/mag");
        // The magazine marker is near its top, not the palm's contact patch.
        // This socket sits in the physical middle of the backstrap/grip.
        if(mag)model.localPosition-=root.InverseTransformPoint(mag.position)+new Vector3(0,-.035f,-.030f);
        new GameObject("HandleSocket").transform.SetParent(root,false);
        bounds=GeometryBounds(root);
        var muzzle=new GameObject("Muzzle").transform;muzzle.SetParent(root,false);
        muzzle.localPosition=new Vector3(bounds.center.x,end?root.InverseTransformPoint(end.position).y:bounds.max.y-.02f,bounds.max.z+.002f);
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=2;
        return root;
    }
    static Bounds GeometryBounds(Transform root)
    {
        bool found=false;var bounds=new Bounds();
        foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                foreach(var vertex in mesh.vertices)
                {
                    var point=root.InverseTransformPoint(BakedWeaponPoint(skin,vertex));
                    if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
                }
            }
            finally{if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        }
        if(!found)throw new System.InvalidOperationException("Pistola sem geometria para calibrar o cabo");
        return bounds;
    }
    // The Colt's baked vertices already include the imported armature scale.
    // TransformPoint multiplies that scale again (a 5 m source became 74 m
    // during the measurement), causing the normalization to shrink it away.
    public static Vector3 BakedWeaponPoint(SkinnedMeshRenderer skin,Vector3 vertex)
        =>skin.transform.position+skin.transform.rotation*vertex;
    void OnDestroy()
    {
        if(animator&&layer>=0)animator.SetLayerWeight(layer,0);
        if(animator&&reloadLayer>=0)animator.SetLayerWeight(reloadLayer,0);
        if(weapon){if(Application.isPlaying)Destroy(weapon.gameObject);else DestroyImmediate(weapon.gameObject);}
    }
}
