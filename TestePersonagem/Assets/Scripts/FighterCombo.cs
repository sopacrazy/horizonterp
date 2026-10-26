using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class FighterCombo : MonoBehaviour
{
    const string StateName = "Soco";
    const float BlendSeconds = .08f;
    const float ReleaseSeconds = .12f;
    const float WindupSeconds = .16f;
    const int AttackLayer = 1;
    ThirdPersonPlayer player;
    Animator animator;
    AnimationClip punch;
    Coroutine attack;
    bool queued;
    float stanceWeight;
    float leanDegrees;
    Transform hips, spine, rightUpperLeg, rightLowerLeg, rightFoot;
    Transform leftUpperLeg, leftLowerLeg, leftFoot;

    void Awake()
    {
        player = GetComponent<ThirdPersonPlayer>();
        animator = player.animator;
        if (!animator || !animator.runtimeAnimatorController) return;
        punch = Array.Find(animator.runtimeAnimatorController.animationClips,
            clip => clip && clip.name == "Punching");
        if (!punch) Debug.LogError("Animação Punching ausente do controlador");
        if (!animator.isHuman) return;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F)) Trigger();
    }

    void LateUpdate()
    {
        if (stanceWeight <= 0f || !hips || !spine || !rightUpperLeg || !rightLowerLeg ||
            !rightFoot || !leftUpperLeg || !leftLowerLeg || !leftFoot) return;
        var axis = transform.right;
        float footY = Mathf.Min(rightFoot.position.y, leftFoot.position.y);
        var rightFootRotation = rightFoot.rotation;
        var leftFootRotation = leftFoot.rotation;
        rightUpperLeg.rotation = Quaternion.AngleAxis(9f * stanceWeight, axis) * rightUpperLeg.rotation;
        rightLowerLeg.rotation = Quaternion.AngleAxis(13f * stanceWeight, axis) * rightLowerLeg.rotation;
        leftUpperLeg.rotation = Quaternion.AngleAxis(-5f * stanceWeight, axis) * leftUpperLeg.rotation;
        leftLowerLeg.rotation = Quaternion.AngleAxis(10f * stanceWeight, axis) * leftLowerLeg.rotation;
        rightFoot.rotation = rightFootRotation;
        leftFoot.rotation = leftFootRotation;
        hips.position += Vector3.up * Mathf.Clamp(footY - Mathf.Min(rightFoot.position.y, leftFoot.position.y), -.05f, .05f);
        spine.rotation = Quaternion.AngleAxis(leanDegrees, axis) * spine.rotation;
    }

    public void Trigger()
    {
        if (!player || !animator || !punch || !hips || animator.layerCount <= AttackLayer ||
            !player.IsGrounded || player.IsJumping ||
            player.IsSpectating || player.DemoMode || StartupLoader.IsLoading || AccountSession.InputBlocked ||
            InGameChat.Editing || (GameHud.Instance && GameHud.Instance.IsPanelOpen)) return;
        if (attack != null) { queued = true; return; }
        attack = StartCoroutine(PlayPunch());
    }

    IEnumerator PlayPunch()
    {
        player.CombatLocked = true;
        do
        {
            queued = false;
            float windup = 0f;
            while (windup < WindupSeconds)
            {
                windup += Time.deltaTime;
                stanceWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(windup / WindupSeconds));
                leanDegrees = -5f * stanceWeight;
                yield return null;
            }
            animator.Play(StateName, AttackLayer, 0f);
            float elapsed = 0f;
            while (elapsed < BlendSeconds)
            {
                elapsed += Time.deltaTime;
                animator.SetLayerWeight(AttackLayer, Mathf.Clamp01(elapsed / BlendSeconds));
                leanDegrees = Mathf.Lerp(-5f, 2f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .28f)));
                yield return null;
            }
            float remaining = Mathf.Max(.1f, punch.length - elapsed);
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                elapsed += Time.deltaTime;
                leanDegrees = Mathf.Lerp(-5f, 2f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .28f)));
                yield return null;
            }
        }
        while (queued);
        float release = 0f;
        while (release < ReleaseSeconds)
        {
            release += Time.deltaTime;
            float amount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(release / ReleaseSeconds));
            animator.SetLayerWeight(AttackLayer, amount);
            stanceWeight = amount;
            leanDegrees = 2f * amount;
            yield return null;
        }
        animator.SetLayerWeight(AttackLayer, 0f);
        stanceWeight = 0f;
        leanDegrees = 0f;
        player.CombatLocked = false;
        attack = null;
    }

    void OnDisable()
    {
        if (attack != null) StopCoroutine(attack);
        attack = null;
        queued = false;
        stanceWeight = 0f;
        leanDegrees = 0f;
        if (player) player.CombatLocked = false;
        if (animator && animator.layerCount > AttackLayer) animator.SetLayerWeight(AttackLayer, 0f);
    }
}
