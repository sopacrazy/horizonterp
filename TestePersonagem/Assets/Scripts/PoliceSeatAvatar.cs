using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Linq;
using System.Collections.Generic;

// A full-size visual follows the door/seat path; gameplay capsules stay outside the cabin.
[DefaultExecutionOrder(400)]
public sealed class PoliceSeatAvatar : MonoBehaviour
{
    public string Uid {get;private set;}
    public bool Exiting=>phase==2;
    public bool Transitioning=>phase!=0;
    public bool IsDriver=>driver;
    public bool Approaching=>phase==3;
    public bool Seated=>!string.IsNullOrEmpty(Uid)&&phase==0;
    public float DoorBlend {get;private set;}
    public float Steering {get;set;}
    PoliceCarRig rig;
    Animator animator;
    Animator sourceAnimator;
    VehicleSeatedPose pose;
    Transform avatar;
    bool driver;
    bool rearSeat;
    int phase;
    float elapsed;
    Vector3 from,exitPoint;
    Quaternion originalRotation;
    Quaternion fromRotation;
    const float EnterDuration=2.4f, ExitDuration=2.1f;
    AnimationClip exitingClip,enteringClip;
    PlayableGraph clipGraph;
    AnimationClipPlayable clipPlayer;
    double sampleTime;
    Vector3 clipOffset,clipStartHip,clipEndHip,exitCorrection;
    Quaternion clipFacing;
    float approachDuration;
    float entryStartTime;
    Vector3 entryStartCorrection;
    bool clipFinishing;
    Transform[] visualBones;
    Quaternion[] idleRotations,exitBlendRotations;
    Quaternion[] openingRotations,sampledRotations;
    Vector3 openingHip;
    Vector3 idleHip;
    Quaternion exitFacing;
    readonly Queue<Vector3> boardingPath=new Queue<Vector3>();
    bool rearDoorOpened;
    const float PoseBlendDuration=.25f;
    public bool AuthoredClipActive=>clipGraph.IsValid()&&phase!=0;
    public Animator VisibleAnimator=>animator;
    float EntryLength=>enteringClip?enteringClip.length-entryStartTime+approachDuration+PoseBlendDuration:EnterDuration;
    float ExitLength=>exitingClip?exitingClip.length+PoseBlendDuration:ExitDuration;
    public float ClipProgress {get;private set;}
    public Vector3 ExpectedHip {get;private set;}
    public void Initialize(PoliceCarRig car,Animator source,bool isDriver,bool isRearSeat=false)
    {
        sourceAnimator=source;
        rig=car;driver=isDriver;rearSeat=!isDriver&&isRearSeat;
        var visual=Instantiate(source.gameObject,transform,false);
        visual.name=isDriver?"Motorista visível":"Passageiro visível";
        foreach(var c in visual.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var b in visual.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
        foreach(var a in visual.GetComponentsInChildren<AudioSource>(true))a.enabled=false;
        foreach(var l in visual.GetComponentsInChildren<AudioListener>(true))l.enabled=false;
        // Remote visuals are hidden while their owner occupies the car. The
        // seat clone still needs one active frame to bind the Humanoid pose.
        visual.SetActive(true);
        avatar=visual.transform;originalRotation=source.transform.localRotation;
        avatar.localPosition=Vector3.zero;avatar.localRotation=originalRotation;
        animator=visual.GetComponent<Animator>();animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();animator.Update(0);animator.SetFloat("Speed",0);animator.SetBool("Jumping",false);
        var cuffs=visual.GetComponent<CuffedAvatarPose>();
        if(!isDriver&&!cuffs)cuffs=visual.AddComponent<CuffedAvatarPose>();
        if(cuffs){cuffs.Cuffed=rearSeat;cuffs.UseLatePose=rearSeat;cuffs.enabled=rearSeat;}
        pose=visual.GetComponent<VehicleSeatedPose>();if(!pose)pose=visual.AddComponent<VehicleSeatedPose>();
        pose.PoseFrame=transform;pose.enabled=true;visual.SetActive(false);
        visualBones=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>animator.GetBoneTransform((HumanBodyBones)i)).Where(b=>b).ToArray();
        idleRotations=visualBones.Select(b=>b.localRotation).ToArray();
        sampledRotations=new Quaternion[visualBones.Length];
        idleHip=transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
        if(driver)exitingClip=Resources.LoadAll<AnimationClip>("Vehicles/Police/ExitingCar").FirstOrDefault(c=>c.humanMotion&&!c.name.StartsWith("__preview")&&c.name!="RearMirrored");
        enteringClip=Resources.LoadAll<AnimationClip>("Vehicles/Police/EnteringCar").FirstOrDefault(c=>c.humanMotion&&!c.name.StartsWith("__preview")&&(driver?c.name!="RearMirrored":c.name=="RearMirrored"));
    }
    // The car exists before its occupants are known. Copy the actual player's
    // visual when a seat is assigned so remote female characters keep their
    // appearance while boarding and sitting in the vehicle.
    public void UseVisual(Animator source)
    {
        if(!source||sourceAnimator==source)return;
        Abort();
        if(avatar)Destroy(avatar.gameObject);
        avatar=null;
        Initialize(rig,source,driver,rearSeat);
    }
    public void SetRearSeat(bool value)
    {
        if(driver||rearSeat==value)return;
        Abort();rearSeat=value;
        var cuffs=avatar?avatar.GetComponent<CuffedAvatarPose>():null;
        if(cuffs){cuffs.Cuffed=value;cuffs.UseLatePose=value;cuffs.enabled=value;}
    }
    public void SetOccupant(string uid,Vector3 startOrExit,bool animate=true,float startYaw=float.NaN)
    {
        uid=uid??"";
        if(uid==(Uid??""))return;
        if(!string.IsNullOrEmpty(uid))
        {
            Uid=uid;from=transform.parent.InverseTransformPoint(startOrExit);
            fromRotation=float.IsNaN(startYaw)?transform.localRotation:Quaternion.Inverse(transform.parent.rotation)*Quaternion.Euler(0,startYaw,0);
            transform.localRotation=fromRotation;
            if(Vector3.Distance(from,Door)>6)from=Door;
            phase=animate?1:0;elapsed=0;avatar.gameObject.SetActive(true);
            transform.localPosition=animate?from:SeatRoot;
            if(!animate)transform.localRotation=Quaternion.identity;
            pose.SetBlend(animate?0:1,driver);
            if(animate)
            {
                if(!driver&&enteringClip)PrepareBoardingPath();
                else StartClip();
            }
        }
        else if(avatar.gameObject.activeSelf)
        {
            Uid="";exitPoint=transform.parent.InverseTransformPoint(startOrExit);
            exitFacing=float.IsNaN(startYaw)?Quaternion.Euler(0,driver?-90:90,0):Quaternion.Inverse(transform.parent.rotation)*Quaternion.Euler(0,startYaw,0);
            StopClip();boardingPath.Clear();clipFinishing=false;
            phase=2;elapsed=0;StartClip();
        }
        if(!driver)rig.RearTransitioning=Transitioning;
    }
    Vector3 Door=>driver?rig.DriverDoor:rearSeat?rig.RearDoor:rig.FrontPassengerDoor;
    Vector3 Hip=>driver?rig.DriverSeat:rearSeat?rig.RearSeat:rig.FrontPassengerSeat;
    Vector3 Handle=>driver?rig.DriverHandle:rearSeat?rig.RearHandle:rig.FrontPassengerHandle;
    void SetDoor(float blend)=>rig.SetDoor(driver?0:rearSeat?3:1,blend);
    Vector3 SeatRoot=>Hip-Vector3.up*pose.SeatedHipHeight;
    static float Smooth(float value)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(value));
    void PrepareBoardingPath()
    {
        boardingPath.Clear();rearDoorOpened=false;clipFinishing=false;
        var access=Door+Vector3.right*.4f;
        if(!rearSeat)
        {
            float frontSide=rig.Dimensions.x*.5f+.6f;
            if(from.x<frontSide)boardingPath.Enqueue(new Vector3(frontSide,from.y,from.z));
            boardingPath.Enqueue(access);phase=3;elapsed=0;return;
        }
        // Feet travel outside the car footprint before the authored seat step.
        float side=rig.Dimensions.x*.5f+.6f,rear=-rig.Dimensions.z*.5f-.65f;
        if(from.x<side&&from.z>rear)
        {
            boardingPath.Enqueue(new Vector3(from.x<0?-side:side,from.y,from.z));
            boardingPath.Enqueue(new Vector3(from.x<0?-side:side,from.y,rear));
            boardingPath.Enqueue(new Vector3(side,from.y,rear));
        }
        else if(from.x<side)boardingPath.Enqueue(new Vector3(side,from.y,Mathf.Min(from.z,rear)));
        boardingPath.Enqueue(access);
        phase=3;elapsed=0;
    }
    void ApproachRearDoor()
    {
        pose.SetBlend(0,false);pose.ReachDoor(0,Vector3.zero);
        var destination=boardingPath.Peek();
        var direction=destination-transform.localPosition;direction.y=0;
        transform.localPosition=Vector3.MoveTowards(transform.localPosition,destination,1.65f*Time.deltaTime);
        if(direction.sqrMagnitude>.0025f)transform.localRotation=Quaternion.RotateTowards(transform.localRotation,Quaternion.LookRotation(direction),360*Time.deltaTime);
        animator.SetFloat("Speed",1.65f);
        DoorBlend=Mathf.MoveTowards(DoorBlend,1,Time.deltaTime*2.5f);SetDoor(DoorBlend);
        if(Vector3.Distance(transform.localPosition,destination)>.04f)return;
        boardingPath.Dequeue();if(boardingPath.Count>0)return;
        from=transform.localPosition;fromRotation=transform.localRotation;elapsed=0;phase=1;rearDoorOpened=true;
        animator.SetFloat("Speed",0);StartClip();
    }
    void Update()
    {
        if(!avatar||!rig)return;
        pose.Steering=Steering;
        elapsed+=Time.deltaTime;
        if(!driver)rig.RearTransitioning=Transitioning;
        if(phase==3){ApproachRearDoor();return;}
        if(phase==1&&enteringClip||phase==2&&exitingClip){UpdateAuthoredClip();return;}
        if(phase==1)
        {
            float approach=Smooth(elapsed/.65f),sit=Smooth((elapsed-1f)/.8f);
            Vector3 outside=Vector3.Lerp(from,Door,approach);
            transform.localPosition=Vector3.Lerp(outside,SeatRoot,sit);
            transform.localPosition-=Vector3.up*(Mathf.Sin(sit*Mathf.PI)*.08f);
            var facingDoor=Quaternion.Slerp(fromRotation,Quaternion.Euler(0,driver?90:-90,0),approach);
            transform.localRotation=Quaternion.Slerp(facingDoor,Quaternion.identity,sit);
            pose.SetBlend(sit,driver);
            DoorBlend=Smooth((elapsed-.5f)/.4f)*(1-Smooth((elapsed-1.95f)/.45f));
            pose.ReachDoor(driver?Mathf.Sin(Mathf.Clamp01((elapsed-.4f)/.7f)*Mathf.PI):0,transform.parent.TransformPoint(Handle));
            animator.SetFloat("Speed",elapsed<.6f?1.4f:0);
            if(elapsed>=EnterDuration){phase=0;DoorBlend=0;pose.ReachDoor(0,Vector3.zero);}
        }
        else if(phase==2)
        {
            float leave=Smooth((elapsed-.55f)/.9f);
            transform.localPosition=Vector3.Lerp(SeatRoot,Door,leave);
            if(elapsed>1.45f)transform.localPosition=Vector3.Lerp(Door,exitPoint,Smooth((elapsed-1.45f)/.65f));
            transform.localRotation=Quaternion.Slerp(Quaternion.identity,Quaternion.Euler(0,driver?-90:90,0),leave);
            pose.SetBlend(1-leave,driver);
            DoorBlend=Smooth(elapsed/.45f)*(1-Smooth((elapsed-1.6f)/.5f));
            pose.ReachDoor(driver&&elapsed<.6f?Mathf.Sin(Mathf.Clamp01(elapsed/.6f)*Mathf.PI):0,transform.parent.TransformPoint(Handle));
            animator.SetFloat("Speed",elapsed>1.6f?1f:0);
            if(elapsed>=ExitDuration){phase=0;DoorBlend=0;pose.SetBlend(0,driver);pose.ReachDoor(0,Vector3.zero);avatar.gameObject.SetActive(false);}
        }
        else if(!string.IsNullOrEmpty(Uid))
        {transform.localPosition=SeatRoot;transform.localRotation=Quaternion.identity;pose.SetBlend(1,driver);animator.SetFloat("Speed",0);}
        SetDoor(DoorBlend);
    }
    public void Abort()
    {
        StopClip();
        boardingPath.Clear();clipFinishing=false;
        Uid="";phase=0;DoorBlend=0;
        if(rig)SetDoor(0);
        if(rig&&!driver)rig.RearTransitioning=false;
        if(pose){pose.SetBlend(0,driver);pose.ReachDoor(0,Vector3.zero);}
        if(avatar)avatar.gameObject.SetActive(false);
    }
    void StartClip()
    {
        var clip=phase==1?enteringClip:exitingClip;
        if(!clip)return;
        if(phase==2)
        {
            openingHip=HipInCar();
            openingRotations=visualBones.Select(b=>Quaternion.Inverse(transform.parent.rotation)*b.rotation).ToArray();
        }
        StopClip();pose.SetBlend(0,driver);pose.ReachDoor(0,Vector3.zero);clipFinishing=false;
        clipGraph=PlayableGraph.Create("Viatura · movimento original");clipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        // The clips already contain foot placement. Humanoid Foot IK would
        // ground the seated legs and fight the authored sill-crossing step.
        clipPlayer=AnimationClipPlayable.Create(clipGraph,clip);clipPlayer.SetApplyFootIK(false);clipPlayer.SetApplyPlayableIK(false);clipPlayer.SetSpeed(0);
        var output=AnimationPlayableOutput.Create(clipGraph,"Humanoid",animator);output.SetSourcePlayable(clipPlayer);
        clipGraph.Play();
        transform.localPosition=Vector3.zero;
        // Entering Car is authored walking along +Z and finishes facing -X.
        // Rotating the WHOLE motion +90 puts that final pose along the hood.
        // Exiting Car is already authored seated along +Z, leaving to the left.
        clipFacing=phase==1?Quaternion.Euler(0,driver?90:-90,0):Quaternion.identity;
        transform.localRotation=clipFacing;
        SampleClip(0);clipStartHip=HipInCar();
        SampleClip(clip.length-.001f);clipEndHip=HipInCar();
        clipOffset=Hip-(phase==1?clipEndHip:clipStartHip);
        exitCorrection=exitPoint+exitFacing*idleHip-(clipOffset+clipEndHip);
        var naturalStart=clipOffset+clipStartHip-Vector3.up*pose.StandingHipHeight;
        approachDuration=Mathf.Clamp(Mathf.Max(Vector3.Distance(from,naturalStart)/1.65f,Quaternion.Angle(fromRotation,clipFacing)/240f),.18f,1.2f);
        entryStartTime=phase==1?enteringClip.length*.14f:0;
        if(phase==1)
        {
            SampleClip(entryStartTime);
            entryStartCorrection=from+fromRotation*idleHip-(clipOffset+HipInCar());
            approachDuration=0;
        }
        ClipProgress=phase==1?.14f:0;sampleTime=entryStartTime;SampleClip(sampleTime);
    }
    void StopClip(){if(clipGraph.IsValid())clipGraph.Destroy();if(avatar)avatar.localPosition=Vector3.zero;}
    void UpdateAuthoredClip()
    {
        bool entering=phase==1;
        float duration=(entering?enteringClip:exitingClip).length;
        if(clipFinishing)
        {
            if(elapsed>=(entering?EntryLength:ExitLength))
            {phase=0;if(!entering)avatar.gameObject.SetActive(false);}
            return;
        }
        float t=Mathf.Clamp01((elapsed+(entering?entryStartTime:0)-(entering?approachDuration:0))/duration);
        ClipProgress=t;
        pose.SetBlend(0,driver);pose.ReachDoor(0,Vector3.zero);
        sampleTime=t*Mathf.Max(0,duration-.001f);
        // Opening/closing follows the handle gestures, not a generic lerp of
        // the actor across the doorway. The exit clip closes before walking off.
        DoorBlend=entering?Smooth((t-.17f)/.18f)*(1-Smooth((t-.7f)/.18f)):
            Smooth((t-.1f)/.17f)*(1-Smooth((t-.64f)/.16f));
        if(entering&&!driver)DoorBlend=(rearDoorOpened?1:Smooth(elapsed/.35f))*(1-Smooth((t-.78f)/.16f));
        SetDoor(DoorBlend);
    }
    Vector3 HipInCar()=>transform.parent.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
    void SampleClip(double time)
    {
        avatar.localPosition=Vector3.zero;avatar.localRotation=originalRotation;
        clipPlayer.SetTime(time);clipPlayer.SetDone(false);clipGraph.Evaluate(0);
    }
    void LateUpdate()
    {
        if(clipFinishing)
        {
            if(phase==2)
            {
                float t=Smooth((elapsed-exitingClip.length)/PoseBlendDuration);
                for(int i=0;i<visualBones.Length;i++)visualBones[i].localRotation=Quaternion.Slerp(exitBlendRotations[i],idleRotations[i],t);
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
                avatar.position+=transform.parent.TransformPoint(exitPoint+exitFacing*idleHip)-hips.position;
            }
            return;
        }
        if(!AuthoredClipActive)return;
        // Sample after Unity's Animator update, so locomotion cannot overwrite
        // the manual car clip or reapply its baked translation after alignment.
        transform.localRotation=clipFacing;transform.localPosition=clipOffset;
        if(phase==2)transform.localPosition+=exitCorrection*Smooth((ClipProgress-.82f)/.18f);
        else transform.localPosition+=entryStartCorrection*(1-Smooth((ClipProgress-.14f)/.46f));
        SampleClip(sampleTime);
        if(phase==2&&elapsed<PoseBlendDuration)
        {
            float weight=Smooth(elapsed/PoseBlendDuration);
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var targetHip=hips.position;
            for(int i=0;i<visualBones.Length;i++)sampledRotations[i]=visualBones[i].rotation;
            for(int i=0;i<visualBones.Length;i++)visualBones[i].rotation=Quaternion.Slerp(transform.parent.rotation*openingRotations[i],sampledRotations[i],weight);
            hips.position=Vector3.Lerp(transform.parent.TransformPoint(openingHip),targetHip,weight);
        }
        if(phase==1&&elapsed<approachDuration)
        {
            float t=Smooth(elapsed/approachDuration);
            var destination=clipOffset+clipStartHip;
            var wanted=Vector3.Lerp(from+fromRotation*idleHip,destination,t);
            transform.localRotation=Quaternion.Slerp(fromRotation,clipFacing,t);
            avatar.position+=transform.parent.TransformPoint(wanted)-animator.GetBoneTransform(HumanBodyBones.Hips).position;
        }
        ExpectedHip=HipInCar();
        float duration=phase==1?enteringClip.length-entryStartTime+approachDuration:exitingClip.length;
        if(elapsed<duration)return;
        // Capture the actual last rendered pose before resetting its root.
        clipFinishing=true;DoorBlend=0;SetDoor(0);
        if(phase==1)
        {
            // Snapshot rotations must use the final car frame, so include the
            // clip root's +90 rotation in the hips before capturing the bridge.
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var hipRotation=hips.rotation;
            var hipPosition=hips.position;
            transform.localRotation=Quaternion.identity;
            hips.SetPositionAndRotation(hipPosition,hipRotation);
            pose.BeginSeatTransition(PoseBlendDuration);
            StopClip();transform.localPosition=SeatRoot;pose.SetBlend(1,driver);
        }
        else
        {
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            var hipRotation=hips.rotation;var hipPosition=hips.position;
            StopClip();transform.localPosition=exitPoint;transform.localRotation=exitFacing;
            hips.SetPositionAndRotation(hipPosition,hipRotation);
            exitBlendRotations=visualBones.Select(b=>b.localRotation).ToArray();
            animator.SetFloat("Speed",0);
        }
    }
    void OnDestroy(){StopClip();if(rig&&!driver)rig.RearTransitioning=false;}
}
