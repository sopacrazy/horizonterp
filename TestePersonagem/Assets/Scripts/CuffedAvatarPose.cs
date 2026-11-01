using UnityEngine;

// Normal locomotion uses Humanoid IK. Manually sampled car clips need their
// hand constraint after the clip (400) and seated correction (500) have run.
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(650)]
public sealed class CuffedAvatarPose : MonoBehaviour
{
    public bool Cuffed { get; set; }
    public bool UseLatePose;

    sealed class Arm
    {
        public Transform upper, lower, hand;
        public Quaternion sourceUpper, sourceLower, sourceHand;
        public Quaternion appliedUpper, appliedLower, appliedHand;
    }

    Animator animator;
    Avatar configuredAvatar;
    Transform hips, head;
    Arm leftArm, rightArm;
    float weight;
    bool applied;

    void Awake() { Initialize(); }

    bool Initialize()
    {
        Restore();
        animator = GetComponent<Animator>();
        configuredAvatar = animator ? animator.avatar : null;
        hips = head = null;
        leftArm = rightArm = null;
        if (!animator || !animator.isHuman) return false;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        leftArm = ArmFor(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightArm = ArmFor(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        return hips && leftArm != null && rightArm != null;
    }

    Arm ArmFor(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
    {
        var arm = new Arm { upper = animator.GetBoneTransform(upper), lower = animator.GetBoneTransform(lower),
            hand = animator.GetBoneTransform(hand) };
        return arm.upper && arm.lower && arm.hand ? arm : null;
    }

    void OnEnable()
    {
        if (UseLatePose && Cuffed) weight = 1f;
    }

    void Update()
    {
        // Remove only our previous arm rotations before the next evaluation.
        // This also prevents accumulation when an Animator is paused/culled.
        Restore();
        weight = UseLatePose && Cuffed ? 1f : Mathf.MoveTowards(weight, Cuffed ? 1f : 0f, Time.deltaTime * 4f);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (UseLatePose) return;
        if (animator && configuredAvatar != animator.avatar) Initialize();
        if (layerIndex != 0 || !hips) return;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        if (weight <= 0f) return;
        // Wrist targets sit just behind the belt, leaving the torso and gait untouched.
        Vector3 center = hips.position - animator.transform.forward * .23f - animator.transform.up * .08f;
        SetHand(AvatarIKGoal.LeftHand, center - animator.transform.right * .12f);
        SetHand(AvatarIKGoal.RightHand, center + animator.transform.right * .12f);
    }

    void LateUpdate() { ApplyAfterAnimatedPose(); }

    // Public so explicit clip sampling/verification can evaluate the same final
    // wrist constraint without depending on another Animator update.
    public void ApplyAfterAnimatedPose()
    {
        Restore();
        if (!UseLatePose) return;
        if ((!hips || leftArm == null || rightArm == null || configuredAvatar != animator.avatar) && !Initialize()) return;
        // A newly activated passenger must already be cuffed on its first frame.
        if (Cuffed) weight = 1f;
        if (weight <= 0f) return;

        Vector3 shoulders = (leftArm.upper.position + rightArm.upper.position) * .5f;
        Vector3 right = rightArm.upper.position - leftArm.upper.position;
        if (right.sqrMagnitude < .000001f) right = transform.right;
        right.Normalize();
        Vector3 torso = shoulders - hips.position;
        if (head) torso += (head.position - shoulders) * .35f;
        Vector3 up = Vector3.ProjectOnPlane(torso, right);
        if (up.sqrMagnitude < .000001f) up = Vector3.ProjectOnPlane(transform.up, right);
        if (up.sqrMagnitude < .000001f) return;
        up.Normalize();
        Vector3 forward = Vector3.Cross(right, up).normalized;
        up = Vector3.Cross(forward, right).normalized;

        float leftReach = Reach(leftArm), rightReach = Reach(rightArm);
        if (leftReach <= .002f || rightReach <= .002f) return;
        float reach = Mathf.Min(leftReach, rightReach);
        float shoulderWidth = Vector3.Distance(leftArm.upper.position, rightArm.upper.position);
        // The shared cuff center is below the belt, behind the pelvis, and
        // follows the actual leaning/turning torso rather than the visual root.
        Vector3 center = hips.position - forward * Mathf.Max(reach * .26f, shoulderWidth * .46f) - up * reach * .05f;
        float halfGap = reach * .065f;
        Vector3 leftOffset = -right * halfGap, rightOffset = right * halfGap;
        // Keep the cuff pair together when an unusually short arm cannot reach
        // the initial center. Each final solve also enforces the .97 limit.
        for (int i = 0; i < 8; i++)
        {
            ConstrainReach(ref center, leftArm, leftOffset, leftReach);
            ConstrainReach(ref center, rightArm, rightOffset, rightReach);
        }

        Capture(leftArm); Capture(rightArm);
        Solve(leftArm, center + leftOffset, leftArm.upper.position - right * reach * .4f - up * reach * .3f - forward * reach * .42f, weight);
        Solve(rightArm, center + rightOffset, rightArm.upper.position + right * reach * .4f - up * reach * .3f - forward * reach * .42f, weight);
        RememberApplied(leftArm); RememberApplied(rightArm);
        applied = true;
    }

    static float Reach(Arm arm)
    {
        return Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.hand.position);
    }

    static void ConstrainReach(ref Vector3 center, Arm arm, Vector3 offset, float reach)
    {
        Vector3 delta = center + offset - arm.upper.position;
        float excess = delta.magnitude - reach * .97f;
        if (excess > 0f) center -= delta.normalized * excess;
    }

    static void Capture(Arm arm)
    {
        arm.sourceUpper = arm.upper.localRotation;
        arm.sourceLower = arm.lower.localRotation;
        arm.sourceHand = arm.hand.localRotation;
    }

    static void Solve(Arm arm, Vector3 target, Vector3 hint, float blend)
    {
        // Rotate joints only: preserve every local position, scale and segment
        // length, as well as all hips, torso and leg animation.
        Vector3 origin = arm.upper.position, delta = target - origin;
        float upperLength = Vector3.Distance(origin, arm.lower.position);
        float lowerLength = Vector3.Distance(arm.lower.position, arm.hand.position);
        if (upperLength < .001f || lowerLength < .001f) return;
        float minimum = Mathf.Abs(upperLength - lowerLength) + .0001f;
        float maximum = (upperLength + lowerLength) * .97f;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Min(minimum, maximum), maximum);
        Vector3 along = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.down;
        Vector3 bend = Vector3.ProjectOnPlane(hint - origin, along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(Vector3.down, along).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(Vector3.right, along).normalized;
        float projection = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        Vector3 elbow = origin + along * projection + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - projection * projection));
        arm.upper.rotation = Quaternion.FromToRotation(arm.lower.position - origin, elbow - origin) * arm.upper.rotation;
        arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position,
            origin + along * distance - arm.lower.position) * arm.lower.rotation;
        if (blend >= 1f) return;
        arm.upper.localRotation = Quaternion.Slerp(arm.sourceUpper, arm.upper.localRotation, blend);
        arm.lower.localRotation = Quaternion.Slerp(arm.sourceLower, arm.lower.localRotation, blend);
    }

    static void RememberApplied(Arm arm)
    {
        arm.appliedUpper = arm.upper.localRotation;
        arm.appliedLower = arm.lower.localRotation;
        arm.appliedHand = arm.hand.localRotation;
    }

    void Restore()
    {
        if (!applied) return;
        Restore(leftArm); Restore(rightArm);
        applied = false;
    }

    static void Restore(Arm arm)
    {
        if (arm == null) return;
        // A fresh manual sample takes precedence over the pose saved last frame.
        if (arm.upper && Mathf.Abs(Quaternion.Dot(arm.upper.localRotation, arm.appliedUpper)) > .999999f) arm.upper.localRotation = arm.sourceUpper;
        if (arm.lower && Mathf.Abs(Quaternion.Dot(arm.lower.localRotation, arm.appliedLower)) > .999999f) arm.lower.localRotation = arm.sourceLower;
        if (arm.hand && Mathf.Abs(Quaternion.Dot(arm.hand.localRotation, arm.appliedHand)) > .999999f) arm.hand.localRotation = arm.sourceHand;
    }

    void OnDisable() { Restore(); }
    void OnDestroy() { Restore(); }

    void SetHand(AvatarIKGoal hand, Vector3 position)
    {
        animator.SetIKPositionWeight(hand, weight);
        animator.SetIKPosition(hand, position);
    }
}
