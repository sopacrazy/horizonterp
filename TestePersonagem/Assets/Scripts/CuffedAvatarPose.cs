using UnityEngine;

// Humanoid IK keeps the hands behind the back while the existing walking clip drives the legs.
[RequireComponent(typeof(Animator))]
public sealed class CuffedAvatarPose : MonoBehaviour
{
    public bool Cuffed { get; set; }
    Animator animator;
    Transform hips;
    float weight;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
    }

    void Update()
    {
        weight = Mathf.MoveTowards(weight, Cuffed ? 1f : 0f, Time.deltaTime * 4f);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !hips) return;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        if (weight <= 0f) return;
        // Wrist targets sit just behind the belt, leaving the torso and gait untouched.
        Vector3 center = hips.position - animator.transform.forward * .23f - animator.transform.up * .08f;
        SetHand(AvatarIKGoal.LeftHand, center - animator.transform.right * .12f);
        SetHand(AvatarIKGoal.RightHand, center + animator.transform.right * .12f);
    }

    void SetHand(AvatarIKGoal hand, Vector3 position)
    {
        animator.SetIKPositionWeight(hand, weight);
        animator.SetIKPosition(hand, position);
    }
}
