using System.Collections.Generic;
using UnityEngine;

// A proportion-aware, additive seated pose for the existing humanoid avatar.
// Runs after locomotion/combat corrections and restores its own changes before
// the next Animator evaluation, including when the Animator is paused/culled.
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(500)]
public sealed class VehicleSeatedPose : MonoBehaviour
{
    sealed class Bone
    {
        public Transform transform;
        public Quaternion source, target, applied, transitionSource;
    }
    sealed class Limb
    {
        public Transform upper, lower, end;
        public float upperLength, lowerLength;
        public Quaternion endInLower;
    }
    readonly List<Bone> bones = new List<Bone>();
    Animator animator;
    Avatar configuredAvatar;
    CuffedAvatarPose cuffed;
    Transform hips, spine, head;
    Limb leftLeg, rightLeg, leftArm, rightArm;
    float pelvisDrop, reachWeight;
    Vector3 sourceHipPosition, appliedHipPosition, doorHandle;
    bool applied, ready;
    Transform transitionSpace;
    Vector3 transitionHipPosition;
    float transitionDuration, transitionElapsed;
    bool seatTransition;

    public float Blend { get; private set; }
    public bool Driving { get; private set; }
    public float Steering { get; set; }
    public bool Ready => ready;
    public bool SeatTransitioning => seatTransition;
    public float StandingHipHeight { get; private set; }
    public float SeatedHipHeight { get; private set; }
    // Normally the avatar is oriented along +Z. The caller can supply the car's
    // frame when a visual FBX contains an authored rotation on its root.
    public Transform PoseFrame { get; set; }
    Transform Frame => PoseFrame ? PoseFrame : transform;

    void Awake() { Initialize(); }

    bool Initialize()
    {
        Restore();
        CancelTransition();
        ready = false;
        animator = GetComponent<Animator>();
        if (!animator || !animator.isHuman || !animator.avatar || !animator.avatar.isValid) return false;
        configuredAvatar = animator.avatar;
        bones.Clear();
        foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine,
            HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand })
        {
            var bone = animator.GetBoneTransform(id);
            if (bone) bones.Add(new Bone { transform = bone });
        }
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        leftLeg = LimbFor(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        rightLeg = LimbFor(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        leftArm = LimbFor(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightArm = LimbFor(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        if (!hips || leftLeg == null || rightLeg == null) return false;
        cuffed = GetComponent<CuffedAvatarPose>();
        StandingHipHeight = Vector3.Dot(hips.position - transform.position, Frame.up);
        pelvisDrop = (leftLeg.upperLength + rightLeg.upperLength) * .45f;
        SeatedHipHeight = StandingHipHeight - pelvisDrop;
        ready = true;
        return true;
    }

    Limb LimbFor(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end)
    {
        var limb = new Limb { upper = animator.GetBoneTransform(upper), lower = animator.GetBoneTransform(lower), end = animator.GetBoneTransform(end) };
        if (!limb.upper || !limb.lower || !limb.end) return null;
        limb.upperLength = Vector3.Distance(limb.upper.position, limb.lower.position);
        limb.lowerLength = Vector3.Distance(limb.lower.position, limb.end.position);
        limb.endInLower = Quaternion.Inverse(limb.lower.rotation) * limb.end.rotation;
        return limb.upperLength > .001f && limb.lowerLength > .001f ? limb : null;
    }

    public void SetBlend(float value, bool driving)
    {
        Blend = Mathf.Clamp01(value); Driving = driving;
        if (!ready || animator && configuredAvatar != animator.avatar) Initialize();
        if (Blend <= 0) CancelTransition();
        if (Blend <= 0 && reachWeight <= 0) Restore();
    }

    // Capture the visible final car-clip pose before its graph/root is reset.
    // The car frame keeps the hip snapshot stable when the seat root changes.
    public void BeginSeatTransition(float duration = .25f)
    {
        CancelTransition();
        if (!ready || !hips || duration <= 0) return;
        transitionSpace = PoseFrame && PoseFrame.parent ? PoseFrame.parent : Frame;
        transitionHipPosition = transitionSpace.InverseTransformPoint(hips.position);
        foreach (var bone in bones) bone.transitionSource = bone.transform.localRotation;
        transitionDuration = duration;
        transitionElapsed = 0;
        seatTransition = true;
    }

    public void CancelTransition()
    {
        seatTransition = false;
        transitionSpace = null;
        transitionDuration = transitionElapsed = 0;
    }

    // Optional opening/closing gesture. The closest arm reaches the handle;
    // the torso and legs remain governed by the seated Blend.
    public void ReachDoor(float weight, Vector3 worldHandle)
    {
        reachWeight = Mathf.Clamp01(weight); doorHandle = worldHandle;
        if (!ready) Initialize();
        if (Blend <= 0 && reachWeight <= 0) Restore();
    }

    void Update() { Restore(); }

    void LateUpdate()
    {
        if (!ready || Blend <= 0 && reachWeight <= 0) return;
        Restore();
        sourceHipPosition = hips.localPosition;
        foreach (var bone in bones) bone.source = bone.transform.localRotation;
        if (Blend > 0)
        {
            ApplyFullSeat();
            Vector3 seatedHipPosition = hips.localPosition;
            foreach (var bone in bones) bone.target = bone.transform.localRotation;
            hips.localPosition = Vector3.Lerp(sourceHipPosition, seatedHipPosition, Blend);
            foreach (var bone in bones) bone.transform.localRotation = Quaternion.Slerp(bone.source, bone.target, Blend);
        }
        if (reachWeight > 0 && leftArm != null && rightArm != null)
        {
            if(spine)spine.rotation=Quaternion.AngleAxis(12f*reachWeight*(1-Blend),Frame.right)*spine.rotation;
            var arm = Vector3.SqrMagnitude(leftArm.upper.position - doorHandle) < Vector3.SqrMagnitude(rightArm.upper.position - doorHandle) ? leftArm : rightArm;
            Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, hand = arm.end.localRotation;
            var frame = Frame;
            Solve(arm, doorHandle, arm.upper.position - frame.up * .3f + frame.right * (arm == leftArm ? -.25f : .25f));
            Quaternion targetUpper = arm.upper.localRotation, targetLower = arm.lower.localRotation, targetHand = arm.end.localRotation;
            arm.upper.localRotation = Quaternion.Slerp(upper, targetUpper, reachWeight);
            arm.lower.localRotation = Quaternion.Slerp(lower, targetLower, reachWeight);
            arm.end.localRotation = Quaternion.Slerp(hand, targetHand, reachWeight);
        }
        if (seatTransition)
        {
            if (!transitionSpace) CancelTransition();
            else
            {
                float progress = Mathf.Clamp01(transitionElapsed / transitionDuration);
                float weight = Mathf.SmoothStep(0, 1, progress);
                Vector3 targetHip = hips.position;
                foreach (var bone in bones)
                    bone.transform.localRotation = Quaternion.Slerp(bone.transitionSource, bone.transform.localRotation, weight);
                hips.position = Vector3.Lerp(transitionSpace.TransformPoint(transitionHipPosition), targetHip, weight);
                if (progress >= 1) CancelTransition();
                else transitionElapsed += Mathf.Max(0, Time.deltaTime);
            }
        }
        appliedHipPosition = hips.localPosition;
        foreach (var bone in bones) bone.applied = bone.transform.localRotation;
        applied = true;
    }

    void ApplyFullSeat()
    {
        var frame = Frame;
        Vector3 up = frame.up, forward = frame.forward, side = frame.right;
        Quaternion headRotation = head ? head.rotation : Quaternion.identity;
        Quaternion leftFoot = leftLeg.end.rotation, rightFoot = rightLeg.end.rotation;
        hips.position -= up * pelvisDrop;
        if (spine) spine.rotation = Quaternion.AngleAxis(Driving ? 7f : -3f, side) * spine.rotation;
        PoseLeg(leftLeg, forward - up * .09f - side * .045f, -up + forward * (Driving ? .19f : .05f), leftFoot);
        PoseLeg(rightLeg, forward - up * .09f + side * .045f, -up + forward * (Driving ? .3f : .05f), rightFoot);
        if (head) head.rotation = headRotation;
        // OnAnimatorIK already positioned a detained passenger's hands behind
        // their back. Keep that work rather than moving the wrists to the lap.
        if (leftArm == null || rightArm == null || !Driving && cuffed && cuffed.Cuffed) return;
        Vector3 shoulders = (leftArm.upper.position + rightArm.upper.position) * .5f;
        float armReach = (leftArm.upperLength + leftArm.lowerLength + rightArm.upperLength + rightArm.lowerLength) * .5f;
        Vector3 center = Driving ? shoulders + forward * armReach * .7f - up * armReach * .28f : hips.position + up * armReach * .15f + forward * armReach * .55f;
        float spread = armReach * (Driving ? .27f : .24f);
        var wheelOffset = Driving ? Quaternion.AngleAxis(-Mathf.Clamp(Steering,-1,1)*40,forward)*side*spread : side*spread;
        Solve(leftArm, center - wheelOffset, shoulders - side * armReach * .5f - up * armReach * .55f);
        Solve(rightArm, center + wheelOffset, shoulders + side * armReach * .5f - up * armReach * .55f);
    }

    static void PoseLeg(Limb limb, Vector3 thighDirection, Vector3 shinDirection, Quaternion footRotation)
    {
        limb.upper.rotation = Quaternion.FromToRotation(limb.lower.position - limb.upper.position, thighDirection) * limb.upper.rotation;
        limb.lower.rotation = Quaternion.FromToRotation(limb.end.position - limb.lower.position, shinDirection) * limb.lower.rotation;
        limb.end.rotation = footRotation;
    }

    static void Solve(Limb limb, Vector3 target, Vector3 hint)
    {
        Vector3 origin = limb.upper.position, delta = target - origin;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(limb.upperLength - limb.lowerLength) + .001f, (limb.upperLength + limb.lowerLength) * .97f);
        Vector3 along = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
        Vector3 bend = Vector3.ProjectOnPlane(hint - origin, along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(Vector3.down, along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(Vector3.right, along).normalized;
        float projection = (limb.upperLength * limb.upperLength - limb.lowerLength * limb.lowerLength + distance * distance) / (2 * distance);
        Vector3 elbow = origin + along * projection + bend * Mathf.Sqrt(Mathf.Max(0, limb.upperLength * limb.upperLength - projection * projection));
        limb.upper.rotation = Quaternion.FromToRotation(limb.lower.position - origin, elbow - origin) * limb.upper.rotation;
        limb.lower.rotation = Quaternion.FromToRotation(limb.end.position - limb.lower.position, origin + along * distance - limb.lower.position) * limb.lower.rotation;
        limb.end.rotation = limb.lower.rotation * limb.endInLower;
    }

    void Restore()
    {
        if (!applied) return;
        if (hips && (hips.localPosition - appliedHipPosition).sqrMagnitude < .00000001f) hips.localPosition = sourceHipPosition;
        foreach (var bone in bones)
            if (bone.transform && Quaternion.Angle(bone.transform.localRotation, bone.applied) < .01f) bone.transform.localRotation = bone.source;
        applied = false;
    }
    void OnDisable() { Restore(); CancelTransition(); }
    void OnDestroy() { Restore(); CancelTransition(); }
}
