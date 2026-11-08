using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class RifleSocketPose
{
    public string avatarKey;
    public Quaternion weaponInRightHand;
    public Quaternion leftHandInWeapon;
    public float referenceSupportGap;
}

[System.Serializable]
public sealed class RifleSocketCalibrationData
{
    public RifleSocketPose[] entries;
}

[System.Serializable]
public sealed class PistolGripProfile
{
    public RifleSocketPose carry;
    public Vector3 rightPalmInWeapon;
    public Vector3 supportInWeapon;
}

// Retarget the two-handed grip to this avatar's proportions after animation.
// The imported shooting clip has no finger tracks and uses a different skeleton.
public sealed class ShootingGrip
{
    sealed class Finger
    {
        public Transform bone;
        public Quaternion rest;
        public Vector3 curlAxis;
        public float curl;
    }

    sealed class Arm
    {
        public Transform upper, lower, hand;
        public Quaternion upperFrame, lowerFrame, palmFrame, handInLower, upperRest;
        public Vector3 palmCenter, lowerAxis, upperAxis, elbowPoleInRoot;
        public readonly List<Finger> fingers = new List<Finger>();
        public readonly List<Twist> twists = new List<Twist>();
        public readonly List<Twist> upperTwists = new List<Twist>();
    }

    sealed class Twist
    {
        public Transform bone;
        public Quaternion restInLimb;
        public float amount;
    }

    Transform root, weapon;
    Arm right, left;
    RifleSocketPose authoredSocket;
    PistolGripProfile twoHandedPistol;
    Transform authoredSupport;
    Vector3 smoothedPistolGrip,pistolGripVelocity;
    bool pistolGripReady;
    public bool TwoHandedPistol => twoHandedPistol != null;
    public Vector3 AuthoredRifleDirection=>authoredSocket!=null&&right!=null?
        right.hand.rotation*authoredSocket.weaponInRightHand*Vector3.forward:Vector3.forward;
    public float RifleGripError
    {
        get
        {
            if(right==null||left==null||!weapon)return float.PositiveInfinity;
            var support=weapon.Find("SupportGrip");
            float rightError=Vector3.Distance(right.hand.TransformPoint(right.palmCenter),weapon.position);
            float leftError=support?Vector3.Distance(left.hand.TransformPoint(left.palmCenter),support.position):0;
            return Mathf.Max(rightError,leftError);
        }
    }
    public float PistolGripSurfaceGap
    {
        get
        {
            if(right==null||!weapon)return float.PositiveInfinity;
            float gap=float.MaxValue;var palm=right.hand.TransformPoint(right.palmCenter);
            foreach(var skin in weapon.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh();
                try{skin.BakeMesh(mesh);foreach(var v in mesh.vertices)gap=Mathf.Min(gap,Vector3.Distance(palm,PistolAvatar.BakedWeaponPoint(skin,v)));}
                finally{if(Application.isPlaying)Object.Destroy(mesh);else Object.DestroyImmediate(mesh);}
            }
            return gap;
        }
    }

    public bool Configure(Animator animator, Transform pistol, Transform muzzle, Transform playerRoot, bool applyLegacyPistolSeat=true)
    {
        if (!animator || !animator.isHuman || !pistol || !playerRoot) return false;
        root = playerRoot;
        weapon = pistol;
        var model = pistol.Find("Colt Pocket Hammer");
        if (model && applyLegacyPistolSeat)
        {
            // The magazine marker is ahead of the actual backstrap. Seat the
            // physical handle in the palm, below the slide and behind the trigger.
            Vector3 seat = new Vector3(0, .030f, .050f);
            model.localPosition += seat;
            if (muzzle && muzzle.parent == pistol) muzzle.localPosition += seat;
        }

        // Calibrate from the avatar's native rest pose, regardless of the frame
        // on which the player entered the range. Restore the sampled pose after.
        var rest = new Dictionary<string, Quaternion>();
        foreach (var bone in animator.avatar.humanDescription.skeleton) rest[bone.name] = bone.rotation;
        var saved = new Dictionary<Transform, Quaternion>();
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
        {
            if (bone == animator.transform || !rest.TryGetValue(bone.name, out var rotation)) continue;
            saved[bone] = bone.localRotation;
            bone.localRotation = rotation;
        }
        try
        {
            right = CreateArm(animator, true);
            left = CreateArm(animator, false);
        }
        finally
        {
            foreach (var bone in saved) bone.Key.localRotation = bone.Value;
        }
        return right != null && left != null;
    }

    public string AuthoredAvatarKey(Animator animator)
    {
        return animator.avatar.name+"|"+right.upper.name+"|"+right.lower.name+"|"+right.hand.name;
    }

    public RifleSocketPose CaptureAuthoredPistol(Animator animator,Quaternion rotation)
        =>new RifleSocketPose{avatarKey=AuthoredAvatarKey(animator),weaponInRightHand=Quaternion.Inverse(right.hand.rotation)*rotation,
            leftHandInWeapon=Quaternion.Inverse(rotation)*left.hand.rotation};

    public bool ConfigureAuthoredPistol(Animator animator,Transform pistol,Transform playerRoot)
    {
        twoHandedPistol=null;
        if(!Configure(animator,pistol,pistol?pistol.Find("Muzzle"):null,playerRoot,false))return false;
        string key=AuthoredAvatarKey(animator);
        var json=Resources.Load<TextAsset>("SAExpanded/PistolSockets");
        var data=json?JsonUtility.FromJson<RifleSocketCalibrationData>(json.text):null;
        authoredSocket=data?.entries==null?null:System.Array.Find(data.entries,p=>p.avatarKey==key);
        if(authoredSocket==null)
        {
            var profileAsset=Resources.Load<TextAsset>("Characters/Fla/FlaPistolGrip");
            var profile=profileAsset?JsonUtility.FromJson<PistolGripProfile>(profileAsset.text):null;
            if(profile?.carry?.avatarKey==key)
            {
                twoHandedPistol=profile;
                authoredSocket=profile.carry;
            }
        }
        return authoredSocket!=null;
    }
    public void ApplyAuthoredPistol()
    {
        if(authoredSocket==null||right==null||!weapon)return;
        var rotation=right.hand.rotation*authoredSocket.weaponInRightHand;
        weapon.SetPositionAndRotation(right.hand.TransformPoint(right.palmCenter),rotation);
        foreach(var finger in right.fingers)PoseFinger(finger);
    }

    public void ApplyTwoHandedPistol(float aimWeight,Vector3 aimPoint,Vector3 muzzleInWeapon)
    {
        var profile=twoHandedPistol;
        Quaternion carry=right.hand.rotation*profile.carry.weaponInRightHand;
        Vector3 carryPosition=right.hand.TransformPoint(right.palmCenter)-carry*profile.rightPalmInWeapon;
        if(aimWeight<=0f)
        {
            pistolGripReady=false;pistolGripVelocity=Vector3.zero;
            weapon.SetPositionAndRotation(carryPosition,carry);
            foreach(var finger in right.fingers)PoseFinger(finger,.82f);
            return;
        }

        Vector3 shoulders=(right.upper.position+left.upper.position)*.5f;
        float reach=Vector3.Distance(right.upper.position,right.lower.position)+Vector3.Distance(right.lower.position,right.hand.position);
        Vector3 forward=Vector3.RotateTowards(root.forward,(aimPoint-shoulders).normalized,50f*Mathf.Deg2Rad,0f);
        Quaternion aim=Quaternion.LookRotation(forward,root.up);
        Vector3 position=shoulders+forward*(reach*.76f)+root.up*.075f+root.right*.025f;
        // Use the muzzle, rather than the shoulder, as the origin of the sight line.
        forward=Vector3.RotateTowards(root.forward,(aimPoint-position-aim*muzzleInWeapon).normalized,50f*Mathf.Deg2Rad,0f);
        aim=Quaternion.LookRotation(forward,root.up);
        // The rig's palm frames keep the thumb up and the fingers toward the
        // grip even when the source avatar's wrist axes differ from the Fla.
        var rightPalm=Quaternion.LookRotation(new Vector3(0f,.34f,.94f),Vector3.up);
        var leftPalm=Quaternion.LookRotation(new Vector3(.40f,.25f,.88f),Vector3.up);
        var rightRotation=aim*rightPalm*Quaternion.Inverse(right.palmFrame);
        var leftRotation=aim*leftPalm*Quaternion.Inverse(left.palmFrame);
        var rightOffset=aim*profile.rightPalmInWeapon-rightRotation*Vector3.Scale(right.palmCenter,right.hand.lossyScale);
        var leftOffset=aim*profile.supportInWeapon-leftRotation*Vector3.Scale(left.palmCenter,left.hand.lossyScale);
        for(int i=0;i<4;i++)
        {
            ConstrainReach(ref position,right,rightOffset);
            ConstrainReach(ref position,left,leftOffset);
        }
        // Idle breathing and retarget rounding move the shoulders by a few
        // millimetres every frame. Filter the shared grip once so both hands
        // and the weapon move together instead of visibly vibrating.
        if(!pistolGripReady){smoothedPistolGrip=position;pistolGripReady=true;}
        else smoothedPistolGrip=Vector3.SmoothDamp(smoothedPistolGrip,position,ref pistolGripVelocity,.06f,
            Mathf.Infinity,Mathf.Max(.0001f,Time.deltaTime));
        position=smoothedPistolGrip;
        SolveArm(right,Vector3.Lerp(right.hand.position,position+rightOffset,aimWeight),
            Quaternion.Slerp(right.hand.rotation,rightRotation,aimWeight),StableElbowHint(right),0,true);
        var rotation=Quaternion.Slerp(carry,aim,aimWeight);
        position=right.hand.TransformPoint(right.palmCenter)-rotation*profile.rightPalmInWeapon;
        weapon.SetPositionAndRotation(position,rotation);
        leftRotation=Quaternion.Slerp(left.hand.rotation,rotation*leftPalm*Quaternion.Inverse(left.palmFrame),aimWeight);
        var support=weapon.TransformPoint(profile.supportInWeapon);
        var target=support-leftRotation*Vector3.Scale(left.palmCenter,left.hand.lossyScale);
        SolveArm(left,Vector3.Lerp(left.hand.position,target,aimWeight),leftRotation,StableElbowHint(left),0,true);
        // Close the firing hand around the physical handle. The index already
        // has a smaller authored curl, so it remains resting on the trigger.
        foreach(var finger in right.fingers)PoseFinger(finger,.96f);
        foreach(var finger in left.fingers)
        {
            var authored=finger.bone.localRotation;
            PoseFinger(finger,.82f);
            finger.bone.localRotation=Quaternion.Slerp(authored,finger.bone.localRotation,aimWeight);
        }
    }

    // Called on an isolated avatar sampled in the authored idle pose. This is
    // a fixed socket calibration, never a replacement for the right arm pose.
    public RifleSocketPose CaptureAuthoredRifle(Animator animator,Quaternion weaponWorldRotation)
    {
        if(right==null||left==null||!weapon)throw new System.InvalidOperationException("Rifle grip was not configured");
        var support=weapon.Find("SupportGrip");
        Vector3 trigger=right.hand.TransformPoint(right.palmCenter);
        Vector3 expectedSupport=trigger+weaponWorldRotation*(support?support.localPosition:Vector3.zero);
        return new RifleSocketPose
        {
            avatarKey=AuthoredAvatarKey(animator),
            weaponInRightHand=Quaternion.Inverse(right.hand.rotation)*weaponWorldRotation,
            leftHandInWeapon=Quaternion.Inverse(weaponWorldRotation)*left.hand.rotation,
            referenceSupportGap=Vector3.Distance(left.hand.TransformPoint(left.palmCenter),expectedSupport)
        };
    }

    public bool ConfigureAuthoredRifle(Animator animator,Transform rifle,Transform playerRoot)
    {
        authoredSocket=null;authoredSupport=null;
        if(!Configure(animator,rifle,rifle?rifle.Find("Muzzle"):null,playerRoot))return false;
        var json=Resources.Load<TextAsset>("SAExpanded/RifleSockets")??Resources.Load<TextAsset>("Weapons/Rifle/SocketCalibration");
        if(!json)return false;
        var data=JsonUtility.FromJson<RifleSocketCalibrationData>(json.text);
        string key=AuthoredAvatarKey(animator);
        if(data?.entries!=null)
            authoredSocket=System.Array.Find(data.entries,entry=>entry!=null&&entry.avatarKey==key);
        authoredSupport=rifle.Find("SupportGrip");
        return authoredSocket!=null&&authoredSupport;
    }

    public void LowerAuthoredCarry(float degrees)
    {
        if(right==null||left==null||!root||degrees<=0)return;
        // Lower the complete authored arm chains, preserving their elbow bend
        // and wrist grip. Rotating only the weapon would detach it from the hands.
        var lower=Quaternion.AngleAxis(degrees,root.right);
        right.upper.rotation=lower*right.upper.rotation;
        left.upper.rotation=lower*left.upper.rotation;
    }

    public void ApplyAuthoredRifle(Vector3 direction,bool aiming,float recoil,float speed)
    {
        if(authoredSocket==null||!weapon||!root||right==null||left==null||!authoredSupport)return;
        // The authored right hand drives the rifle throughout idle, run and
        // firing. No camera quaternion, forced wrist twist or right-arm IK.
        Vector3 trigger=right.hand.TransformPoint(right.palmCenter);
        Quaternion rotation=right.hand.rotation*authoredSocket.weaponInRightHand;
        weapon.SetPositionAndRotation(trigger,rotation);
        Vector3 leftHint=left.lower.position;
        Quaternion leftRotation=rotation*authoredSocket.leftHandInWeapon;
        Vector3 wrist=authoredSupport.position-leftRotation*Vector3.Scale(left.palmCenter,left.hand.lossyScale);
        SolveArm(left,wrist,leftRotation,leftHint,0,true);
        // These avatars' source clips have no mapped finger tracks. Close the
        // digits around the handle/handguard without changing either wrist.
        foreach(var finger in right.fingers)PoseFinger(finger);
        foreach(var finger in left.fingers)PoseFinger(finger);
    }

    Arm CreateArm(Animator animator, bool isRight)
    {
        var arm = new Arm
        {
            upper = animator.GetBoneTransform(isRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm),
            lower = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm),
            hand = animator.GetBoneTransform(isRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand)
        };
        var index = animator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var middle = animator.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var little = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        bool groupedFingers=false;
        if(!middle&&arm.hand)
        {
            string groupedName=isRight?"RightFinger":"LeftFinger";
            foreach(var candidate in arm.hand.GetComponentsInChildren<Transform>(true))
                if(candidate.name==groupedName){middle=candidate;groupedFingers=true;break;}
        }
        if (!arm.upper || !arm.lower || !arm.hand) return null;
        // Some valid Humanoid avatars omit finger mappings. The wrist and arm
        // still provide a stable palm frame for the shared weapon sockets.
        Vector3 along = middle?(middle.position-arm.hand.position).normalized:(arm.hand.position-arm.lower.position).normalized;
        Vector3 thumb = index&&little?Vector3.ProjectOnPlane(index.position-little.position,along).normalized:
            Vector3.ProjectOnPlane(root.up,along).normalized;
        Vector3 inward = Vector3.Cross(along, thumb).normalized * (isRight ? 1f : -1f);
        arm.palmFrame = Quaternion.Inverse(arm.hand.rotation) * Quaternion.LookRotation(along, thumb);
        arm.palmCenter = arm.hand.InverseTransformPoint(middle?Vector3.Lerp(arm.hand.position,middle.position,.65f):arm.hand.position+along*.055f);
        Vector3 nativeBend = Vector3.ProjectOnPlane(arm.lower.position - arm.upper.position,
            arm.hand.position - arm.upper.position).normalized;
        if(nativeBend.sqrMagnitude < .001f)nativeBend = -root.forward;
        arm.upperFrame = Quaternion.Inverse(arm.upper.rotation) *
            Quaternion.LookRotation(arm.lower.position - arm.upper.position, nativeBend);
        arm.lowerFrame = Quaternion.Inverse(arm.lower.rotation) *
            Quaternion.LookRotation(arm.hand.position - arm.lower.position, nativeBend);
        arm.lowerAxis = arm.lower.InverseTransformDirection(arm.hand.position - arm.lower.position).normalized;
        arm.handInLower = Quaternion.Inverse(arm.lower.rotation) * arm.hand.rotation;
        arm.upperAxis = arm.upper.InverseTransformDirection(arm.lower.position - arm.upper.position).normalized;
        arm.upperRest = arm.upper.localRotation;
        // Store an anatomical pole in player space once. Reading lower.position
        // again after IK made the previous frame feed the next one and allowed
        // the elbow to creep upward while the camera moved.
        // A two-handed pistol stance keeps both elbows below the wrists and
        // slightly open. A straight bind/T-pose has no reliable bend plane,
        // so use an explicit player-space pole instead of deriving it from it.
        arm.elbowPoleInRoot=new Vector3(isRight?.42f:-.42f,-.88f,-.20f).normalized;
        foreach (var bone in arm.upper.GetComponentsInChildren<Transform>())
        {
            float amount;
            if (bone.name.Contains("UpperarmTwist01")) amount = 0f;
            else if (bone.name.Contains("UpperarmTwist02")) amount = .5f;
            else continue;
            arm.upperTwists.Add(new Twist { bone = bone, amount = amount,
                restInLimb = Quaternion.Inverse(arm.upper.rotation) * bone.rotation });
        }
        foreach (var bone in arm.lower.GetComponentsInChildren<Transform>())
        {
            float amount;
            if (bone.name.Contains("ForearmTwist01")) amount = .33f;
            else if (bone.name.Contains("ForearmTwist02")) amount = .67f;
            else if (bone.name.Contains("ElbowShareBone")) amount = 0f;
            else continue;
            arm.twists.Add(new Twist { bone = bone, amount = amount,
                restInLimb = Quaternion.Inverse(arm.lower.rotation) * bone.rotation });
        }

        var first = isRight ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal;
        // HumanBodyBones stores the five fingers consecutively, three joints each.
        for (int finger = 0; finger < 5; finger++)
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = animator.GetBoneTransform((HumanBodyBones)((int)first + finger * 3 + joint));
            if (!bone) continue;
            Vector3 direction = bone.childCount > 0 ? bone.GetChild(0).position - bone.position : along;
            Vector3 axis = Vector3.Cross(direction.normalized, finger == 0 ? along : inward).normalized;
            float curl = joint == 0 ? 48f : joint == 1 ? 72f : 48f;
            if (finger == 0) curl = joint == 0 ? 30f : joint == 1 ? 35f : 25f;
            // The firing index rests on the trigger, the others wrap the grip.
            if (isRight && finger == 1) curl = joint == 0 ? 20f : joint == 1 ? 42f : 30f;
            arm.fingers.Add(new Finger { bone = bone, rest = bone.localRotation,
                curlAxis = bone.InverseTransformDirection(axis), curl = curl });
        }
        if(groupedFingers)
        {
            foreach(var bone in middle.GetComponentsInChildren<Transform>(true))
            {
                Vector3 direction=bone.childCount>0?bone.GetChild(0).position-bone.position:along;
                Vector3 axis=Vector3.Cross(direction.normalized,inward).normalized;
                arm.fingers.Add(new Finger{bone=bone,rest=bone.localRotation,curlAxis=bone.InverseTransformDirection(axis),curl=55f});
            }
        }
        return arm;
    }

    public void ApplyPose(Vector3 aimDirection, float recoil)
    {
        if (right == null || left == null || !root || !weapon) return;
        if (aimDirection.sqrMagnitude < .001f) aimDirection = root.forward;
        Quaternion aim = Quaternion.LookRotation(aimDirection.normalized, root.up);
        Quaternion grip = aim * Quaternion.Euler(-4f * Mathf.Clamp01(recoil), 0, 0);
        Vector3 forward = grip * Vector3.forward, up = grip * Vector3.up, side = grip * Vector3.right;

        Vector3 shoulders = (right.upper.position + left.upper.position) * .5f;
        float reach = Vector3.Distance(right.upper.position, right.lower.position) +
            Vector3.Distance(right.lower.position, right.hand.position);
        // Raise the two-hand grip to the face, with bent elbows in front of the
        // chest, matching the reference aiming stance rather than a low ready.
        Vector3 gripPosition = shoulders + forward * (reach * .82f) + side * .035f + up * .07f;
        gripPosition -= forward * (.022f * Mathf.Clamp01(recoil));

        Quaternion rightPalm = Quaternion.LookRotation(forward * .766f + up * .643f, -forward * .643f + up * .766f);
        Quaternion leftPalm = Quaternion.LookRotation(forward * .866f + up * .50f, -forward * .50f + up * .866f);
        Quaternion rightRotation = rightPalm * Quaternion.Inverse(right.palmFrame);
        Quaternion leftRotation = leftPalm * Quaternion.Inverse(left.palmFrame);
        Vector3 rightForearm = rightRotation * Quaternion.Inverse(right.handInLower) * right.lowerAxis;
        Vector3 rightOffset = side * .023f - rightRotation * Vector3.Scale(right.palmCenter, right.hand.lossyScale);
        Vector3 leftOffset = -side * .028f - up * .012f + forward * .025f -
            leftRotation * Vector3.Scale(left.palmCenter, left.hand.lossyScale);
        // Move the shared grip into both arms' reach, including while the body
        // catches up with a quick camera turn. Never stretch the support hand.
        for (int i = 0; i < 16; i++)
        {
            ConstrainWrist(ref gripPosition, right, rightOffset, rightForearm);
            ConstrainReach(ref gripPosition, right, rightOffset);
            ConstrainReach(ref gripPosition, left, leftOffset);
        }
        SolveArm(right, gripPosition + rightOffset, rightRotation,
            shoulders + side * .42f - up * .32f + forward * .18f, 35f);
        gripPosition = right.hand.TransformPoint(right.palmCenter) - side * .023f;
        SolveArm(left, gripPosition + leftOffset, leftRotation,
            shoulders - side * .40f - up * .30f + forward * .20f);

        // Seat the model in the solved palm. Both hands and the gun share the
        // same frame, so neither camera pitch nor recoil can slide the grip.
        weapon.SetPositionAndRotation(gripPosition, grip);
        foreach (var finger in right.fingers) PoseFinger(finger);
        foreach (var finger in left.fingers) PoseFinger(finger);
    }

    public void ApplyRiflePose(Vector3 aimDirection,bool aiming,float recoil,float speed)
    {
        if(right==null||left==null||!root||!weapon)return;
        bool running=speed>2.5f&&!aiming;
        Vector3 direction=aimDirection.sqrMagnitude>.001f?aimDirection.normalized:root.forward;
        Quaternion frame=Quaternion.LookRotation(direction,Vector3.up)*Quaternion.Euler(aiming?-2f:running?26f:0,0,0);
        frame*=Quaternion.Euler(-3f*Mathf.Clamp01(recoil),0,0);
        Vector3 forward=frame*Vector3.forward,up=frame*Vector3.up,side=frame*Vector3.right;
        Vector3 shoulders=(right.upper.position+left.upper.position)*.5f;
        // Keep the authored wrist movement (idle, run and recoil) instead of
        // replacing all three animations with one fixed shoulder-space pose.
        Vector3 position=right.hand.TransformPoint(right.palmCenter);
        if(aiming)position=shoulders+Quaternion.FromToRotation(root.forward,direction)*(position-shoulders);
        Vector3 rightHint=right.lower.position,leftHint=left.lower.position;
        position-=forward*.025f*Mathf.Clamp01(recoil);
        Quaternion rightPalm=Quaternion.LookRotation(forward*.77f+up*.64f,-forward*.64f+up*.77f);
        Quaternion leftPalm=Quaternion.LookRotation(forward*.35f+side*.94f,up);
        Quaternion rr=rightPalm*Quaternion.Inverse(right.palmFrame),lr=leftPalm*Quaternion.Inverse(left.palmFrame);
        var support=weapon.Find("SupportGrip");
        Vector3 supportLocal=support?support.localPosition:new Vector3(-.015f,.025f,.3f);
        Vector3 ro=-(rr*Vector3.Scale(right.palmCenter,right.hand.lossyScale));
        Vector3 lo=frame*supportLocal-lr*Vector3.Scale(left.palmCenter,left.hand.lossyScale);
        for(int i=0;i<8;i++){ConstrainReach(ref position,right,ro);ConstrainReach(ref position,left,lo);}
        SolveArm(right,position+ro,rr,rightHint,0,true);
        SolveArm(left,position+lo,lr,leftHint,0,true);
        weapon.SetPositionAndRotation(position,frame);
        foreach(var finger in right.fingers)PoseFinger(finger);
        foreach(var finger in left.fingers)PoseFinger(finger);
    }

    static void PoseFinger(Finger finger,float amount=1f)
    {
        finger.bone.localRotation = finger.rest * Quaternion.AngleAxis(finger.curl*amount, finger.curlAxis);
    }

    Vector3 StableElbowHint(Arm arm)
    {
        float length=Vector3.Distance(arm.upper.position,arm.lower.position);
        return arm.upper.position+root.TransformDirection(arm.elbowPoleInRoot)*length;
    }

    static void ConstrainReach(ref Vector3 grip, Arm arm, Vector3 wristOffset)
    {
        float length = Vector3.Distance(arm.upper.position, arm.lower.position) +
            Vector3.Distance(arm.lower.position, arm.hand.position);
        Vector3 delta = grip + wristOffset - arm.upper.position;
        float excess = delta.magnitude - length * .96f;
        if (excess > 0f) grip -= delta.normalized * excess;
    }

    static void ConstrainWrist(ref Vector3 grip, Arm arm, Vector3 wristOffset, Vector3 forearmDirection)
    {
        float upperLength = Vector3.Distance(arm.upper.position, arm.lower.position);
        float lowerLength = Vector3.Distance(arm.lower.position, arm.hand.position);
        // A neutral wrist places the elbow exactly one forearm length behind
        // the hand. Project the shared grip onto that anatomical surface.
        Vector3 center = arm.upper.position + forearmDirection * lowerLength - wristOffset;
        Vector3 delta = grip - center;
        if (delta.sqrMagnitude > .000001f) grip = center + delta.normalized * upperLength;
    }

    static void SolveArm(Arm arm, Vector3 target, Quaternion handRotation, Vector3 hint, float elbowSwivel = 0f,bool useHint=false)
    {
        Vector3 origin = arm.upper.position;
        float upperLength = Vector3.Distance(origin, arm.lower.position);
        float lowerLength = Vector3.Distance(arm.lower.position, arm.hand.position);
        Vector3 delta = target - origin;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + .001f,
            (upperLength + lowerLength) * .98f);
        Vector3 along = delta.normalized;
        Quaternion preferredLower = handRotation * Quaternion.Inverse(arm.handInLower);
        Vector3 neutralForearm = preferredLower * arm.lowerAxis;
        Vector3 bend = -Vector3.ProjectOnPlane(neutralForearm, along).normalized;
        if(useHint)bend=Vector3.ProjectOnPlane(hint-origin,along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(hint - origin, along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(Vector3.down, along).normalized;
        if (elbowSwivel > 0f)
        {
            // Open the firing elbow slightly while keeping the hand socket fixed.
            Vector3 outward = Vector3.ProjectOnPlane(hint - origin, along).normalized;
            float turn = Mathf.Clamp(Vector3.SignedAngle(bend, outward, along), -elbowSwivel, elbowSwivel);
            bend = Quaternion.AngleAxis(turn, along) * bend;
        }
        float projection = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - projection * projection));
        Vector3 elbow = origin + along * projection + bend * height;
        Vector3 upperAxis = (elbow - origin).normalized;
        Quaternion neutralUpper = arm.upper.parent.rotation * arm.upperRest;
        Quaternion baseUpper = Quaternion.FromToRotation(neutralUpper * arm.upperAxis, upperAxis) * neutralUpper;
        arm.upper.rotation = Quaternion.LookRotation(upperAxis, bend) * Quaternion.Inverse(arm.upperFrame);
        float upperTwist = AxialAngle(arm.upper.rotation * Quaternion.Inverse(baseUpper), upperAxis);
        foreach (var helper in arm.upperTwists)
            helper.bone.rotation = Quaternion.AngleAxis(upperTwist * helper.amount, upperAxis) * baseUpper * helper.restInLimb;
        Vector3 forearm = (origin + along * distance - arm.lower.position).normalized;
        Quaternion baseLower = Quaternion.LookRotation(forearm, bend) * Quaternion.Inverse(arm.lowerFrame);
        Quaternion rolledLower = Quaternion.FromToRotation(neutralForearm, forearm) * preferredLower;
        Quaternion roll = rolledLower * Quaternion.Inverse(baseLower);
        float twist = AxialAngle(roll, forearm);
        arm.lower.rotation = rolledLower;
        arm.hand.rotation = handRotation;
        foreach (var helper in arm.twists)
            helper.bone.rotation = Quaternion.AngleAxis(twist * helper.amount, forearm) * baseLower * helper.restInLimb;
    }

    static float AxialAngle(Quaternion rotation, Vector3 axis)
    {
        float sine = Vector3.Dot(new Vector3(rotation.x, rotation.y, rotation.z), axis);
        return Mathf.DeltaAngle(0, 2f * Mathf.Atan2(sine, rotation.w) * Mathf.Rad2Deg);
    }
}
