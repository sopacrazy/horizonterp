using System.Collections.Generic;
using UnityEngine;

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
        public Vector3 palmCenter, lowerAxis, upperAxis;
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

    public bool Configure(Animator animator, Transform pistol, Transform muzzle, Transform playerRoot)
    {
        if (!animator || !animator.isHuman || !pistol || !playerRoot) return false;
        root = playerRoot;
        weapon = pistol;
        var model = pistol.Find("Colt Pocket Hammer");
        if (model)
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
        if (!arm.upper || !arm.lower || !arm.hand || !index || !middle || !little) return null;
        Vector3 along = (middle.position - arm.hand.position).normalized;
        Vector3 thumb = Vector3.ProjectOnPlane(index.position - little.position, along).normalized;
        Vector3 inward = Vector3.Cross(along, thumb).normalized * (isRight ? 1f : -1f);
        arm.palmFrame = Quaternion.Inverse(arm.hand.rotation) * Quaternion.LookRotation(along, thumb);
        arm.palmCenter = arm.hand.InverseTransformPoint(Vector3.Lerp(arm.hand.position, middle.position, .65f));
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

    static void PoseFinger(Finger finger)
    {
        finger.bone.localRotation = finger.rest * Quaternion.AngleAxis(finger.curl, finger.curlAxis);
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

    static void SolveArm(Arm arm, Vector3 target, Quaternion handRotation, Vector3 hint, float elbowSwivel = 0f)
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
