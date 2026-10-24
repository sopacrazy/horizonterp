using UnityEngine;

public sealed class RemoteAvatarAnimation
{
    readonly Animator animator;
    bool wasJumping;
    public RemoteAvatarAnimation(Animator animator)
    {
        this.animator=animator;
        animator.enabled=true;animator.speed=1;animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode=AnimatorUpdateMode.UnscaledTime;
        // A clone must not inherit the local player's jump or paused state.
        animator.Rebind();animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
        animator.Play("Locomocao",0,0);animator.Update(0);
    }
    public void Apply(float speed,bool jumping,float phase,float deltaTime)
    {
        animator.SetFloat("Speed",speed,.08f,Mathf.Max(.0001f,deltaTime));
        animator.SetBool("Jumping",jumping);
        if(jumping&&!wasJumping)animator.Play("Jump",0,Mathf.Clamp01(phase));
        if(!jumping&&wasJumping)
        {
            animator.SetFloat("Speed",speed);
            animator.CrossFadeInFixedTime("Locomocao",.06f,0);
        }
        wasJumping=jumping;
    }
}
