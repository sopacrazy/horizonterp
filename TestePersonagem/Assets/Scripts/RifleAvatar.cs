using UnityEngine;

// Cosmetic weapon pose shared by the controlled character and remote replicas.
// Damage remains owned by the combat server; a visual shot never sets health.
[DefaultExecutionOrder(210)]
public sealed class RifleAvatar : MonoBehaviour
{
    Animator animator;
    Transform owner,weapon,muzzle;
    ShootingGrip grip;
    int carryLayer=-1,armsLayer=-1,fireLayer=-1,lastShot;
    float pitch,speed,weight,lowReady,fireAge=10f,recoilAge=10f;
    const float FireStart=.08f,FireActiveLength=.30f;
    bool gripReady;
    bool equipped,aiming;
    bool receivedState;
    public bool Visible=>weapon&&weapon.gameObject.activeSelf;
    public Transform Muzzle=>muzzle;
    public float GripError=>grip!=null?grip.RifleGripError:float.PositiveInfinity;
    public bool Ready=>animator&&animator.isHuman&&carryLayer>=0&&armsLayer>=0&&weapon&&gripReady;
    public int ShotAnimationStarts {get;private set;}
    public float LowReadyAngle=>35f*Mathf.SmoothStep(0,1,lowReady);

    public void Configure(Animator target,Transform root)
    {
        if(animator==target&&owner==root&&weapon)return;
        Clear();animator=target;owner=root;
        if(!animator||!animator.isHuman||!owner)return;
        carryLayer=animator.GetLayerIndex("Rifle");armsLayer=animator.GetLayerIndex("RifleArms");fireLayer=animator.GetLayerIndex("RifleFire");
        var prefab=Resources.Load<GameObject>("Weapons/Rifle/Rifle");
        if(!prefab)return;
        weapon=Instantiate(prefab,owner,false).transform;weapon.name="Rifle equipado";
        foreach(var collider in weapon.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var item in weapon.GetComponentsInChildren<Transform>(true))item.gameObject.layer=2;
        muzzle=weapon.Find("Muzzle");
        grip=new ShootingGrip();gripReady=grip.ConfigureAuthoredRifle(animator,weapon,owner);
        weapon.gameObject.SetActive(false);
    }

    public void Apply(bool equipped,bool aiming,int shotSequence,float pitch,float speed)
    {
        this.equipped=equipped;this.aiming=aiming;
        this.pitch=Mathf.Clamp(pitch,-60,60);this.speed=Mathf.Max(0,speed);
        if(!receivedState){lastShot=shotSequence;receivedState=true;}
        else if(shotSequence>lastShot)
        {
            if(equipped)PlayShot(shotSequence);else lastShot=shotSequence;
        }
    }

    public void PlayShot(int sequence=-1)
    {
        if(sequence>=0)
        {
            if(receivedState&&sequence<=lastShot)return;
            receivedState=true;lastShot=sequence;
        }
        recoilAge=0;
        // An automatic burst keeps the authored short recoil cycle playing.
        // Local prediction and the confirmed sequence cannot restart it twice.
        if(fireAge<FireActiveLength)return;
        fireAge=0;
        if(animator&&fireLayer>=0)
        {
            // Advance the current shot even while its layer is hidden by the
            // moving gait. Stopping cannot reveal a shot left from an old burst.
            animator.Play("RifleFire.Shot",fireLayer,FireStart);
            animator.SetLayerWeight(fireLayer,speed<.2f?1:0);ShotAnimationStarts++;
        }
    }

    void Update()=>PrepareAnimationFrame(Time.deltaTime);
    public void PrepareAnimationFrame(float delta)
    {
        if(!animator||!owner||!weapon)return;
        delta=Mathf.Clamp(delta,0,.1f);fireAge+=delta;recoilAge+=delta;
        bool busy=animator.TryGetComponent<CombatAvatarAnimation>(out var combat)&&combat.Busy;
        bool active=equipped&&!busy&&gripReady;
        weapon.gameObject.SetActive(active);
        weight=Mathf.MoveTowards(weight,active?1:0,delta*9f);
        bool relaxed=active&&!aiming&&recoilAge>.30f&&speed<.2f&&!animator.GetBool("Jumping");
        lowReady=Mathf.MoveTowards(lowReady,relaxed?1:0,delta*6f);
        if(carryLayer>=0)
        {
            animator.SetLayerWeight(carryLayer,animator.GetBool("Jumping")?0:weight);
            animator.SetFloat("RifleSpeed",speed,.09f,Mathf.Max(.001f,delta));
        }
        if(armsLayer>=0)
        {
            // Both trees use the same filtered speed and synchronized phase.
            // The normal gait owns torso, pelvis and legs; only the arms hold
            // the rifle, so an armed clip cannot add lateral hops to the stride.
            animator.SetLayerWeight(armsLayer,animator.GetBool("Jumping")?0:weight);
        }
        // A shooting pose must never replace the running hips/stride.
        if(fireLayer>=0)animator.SetLayerWeight(fireLayer,active&&speed<.2f&&!animator.GetBool("Jumping")?
            Mathf.Clamp01((FireActiveLength-fireAge)/.07f):0);
    }
    void LateUpdate()=>ApplyAttachmentPose();
    public void ApplyAttachmentPose()
    {
        if(!Ready||!weapon.gameObject.activeSelf)return;
        float recoil=recoilAge<.045f?Mathf.Clamp01(recoilAge/.045f):1-Mathf.Clamp01((recoilAge-.045f)/.13f);
        if(aiming||recoil>0)
        {
            var chest=animator.GetBoneTransform(HumanBodyBones.UpperChest)??animator.GetBoneTransform(HumanBodyBones.Chest);
            // Stabilize aim at the torso, retaining the supplied local arm and
            // wrist tracks. The walking torso lean must not aim into the floor.
            float angle=0;
            if(aiming)
            {
                var target=Quaternion.Euler(Mathf.Clamp(pitch,-25,25),owner.eulerAngles.y,0)*Vector3.forward;
                var barrel=Vector3.ProjectOnPlane(grip.AuthoredRifleDirection,owner.right);
                angle=Mathf.Clamp(Vector3.SignedAngle(barrel,target,owner.right),-35,35);
            }
            angle-=1.4f*recoil;
            if(chest)chest.rotation=Quaternion.AngleAxis(angle,owner.right)*chest.rotation;
        }
        // Aim correction uses the original pose above, so it cannot cancel the
        // relaxed carry offset or bend the whole torso forward to lower the gun.
        grip.LowerAuthoredCarry(LowReadyAngle);
        var direction=Quaternion.Euler(pitch,owner.eulerAngles.y,0)*Vector3.forward;
        grip.ApplyAuthoredRifle(direction,aiming,recoil,speed);
    }

    void Clear()
    {
        if(weapon){if(Application.isPlaying)Destroy(weapon.gameObject);else DestroyImmediate(weapon.gameObject);}
        if(animator){if(carryLayer>=0)animator.SetLayerWeight(carryLayer,0);if(armsLayer>=0)animator.SetLayerWeight(armsLayer,0);if(fireLayer>=0)animator.SetLayerWeight(fireLayer,0);}
        weapon=muzzle=null;grip=null;weight=lowReady=0;gripReady=false;
    }
    void OnDestroy()=>Clear();
}
