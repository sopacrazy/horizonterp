using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class CombatState
{
    public string epoch,attackId,hitAttackId;
    public int revision,attackSequence,hitSequence,deathSequence;
    public long attackAt,hitAt,deathAt;
    public float health=100;
    public bool dead;
}

// Shares the same authored full-body clips for the local player and observers.
// Event numbers come from the authoritative room; movement/UDP poses never set health.
public sealed class CombatAvatarAnimation : MonoBehaviour
{
    Animator animator;
    AnimationClip combo,hit,death;
    int layer=-1,revision=-1,attackSequence,hitSequence,deathSequence;
    string epoch,predictedId;
    string predictedHitId;
    float predictedHitAt,clipOffset;
    const float PunchStart=.5f,HitStart=.2f,PredictionTimeout=.75f;
    readonly HashSet<string> retiredEpochs=new HashSet<string>();
    float started,ageAtStart,duration;
    bool dead;
    public string Kind {get;private set;}
    public bool Ready=>animator&&layer>=0&&combo&&hit&&death;
    public bool Dead=>dead;
    public bool Busy=>dead||Kind!=null;
    public float AttackDuration=>combo?combo.length:2.2f;
    public float ClipStartSeconds=>clipOffset;
    public float CurrentClipSeconds=>Mathf.Min(Kind=="attack"?combo.length:Kind=="hit"?hit.length:death?death.length:0,clipOffset+Elapsed);
    public bool HasPredictedHit=>!string.IsNullOrEmpty(predictedHitId);
    public float NormalizedTime=>duration>0?Mathf.Clamp01(Elapsed/duration):0;
    public int AttackPlays {get;private set;}
    public int HitPlays {get;private set;}
    public int DeathPlays {get;private set;}
    float Elapsed=>ageAtStart+Mathf.Max(0,Time.unscaledTime-started);
    public void Initialize(Animator target)
    {
        if(animator==target&&Ready)return;
        animator=target;if(!animator)return;
        layer=animator.GetLayerIndex("Combat");
        combo=Load("PunchCombo");hit=Load("HitReaction");death=Load("Stunned");
        if(layer>=0)animator.SetLayerWeight(layer,0);
    }
    static AnimationClip Load(string name)=>Array.Find(Resources.LoadAll<AnimationClip>("Combat/"+name),x=>x.name==name);
    public void PredictAttack(string attackId)
    {
        if(!Ready||dead)return;
        predictedId=attackId;Play("attack",0);
    }
    // Visual anticipation on the attacker's remote replica only. Health and all
    // authoritative sequence numbers stay unchanged until the API confirms it.
    public bool PredictHit(string attackId)
    {
        if(!Ready||dead||string.IsNullOrEmpty(attackId))return false;
        if(predictedHitId==attackId)return true;
        predictedHitId=attackId;predictedHitAt=Time.unscaledTime;Play("hit",0);
        return true;
    }
    public void CancelPredictedHit(string attackId)
    {
        if(predictedHitId!=attackId||!HasPredictedHit)return;
        predictedHitId=null;
        if(!dead&&Kind=="hit")Clear();
    }
    public bool Apply(CombatState state,long serverTime,float roundTripSeconds=0)
    {
        if(state==null||!Ready)return false;
        if(epoch!=state.epoch)
        {
            if(!string.IsNullOrEmpty(state.epoch)&&retiredEpochs.Contains(state.epoch))return false;
            if(!string.IsNullOrEmpty(epoch)){retiredEpochs.Add(epoch);Clear();}
            epoch=state.epoch;revision=-1;attackSequence=hitSequence=deathSequence=0;
        }
        if(state.revision<revision)return false;
        revision=state.revision;
        float Age(long at)=>Mathf.Max(0,(serverTime-at)*.001f+Mathf.Clamp(roundTripSeconds*.5f,0,.25f));
        bool newAttack=state.attackSequence>attackSequence,newHit=state.hitSequence>hitSequence,newDeath=state.deathSequence>deathSequence;
        attackSequence=Mathf.Max(attackSequence,state.attackSequence);
        hitSequence=Mathf.Max(hitSequence,state.hitSequence);
        deathSequence=Mathf.Max(deathSequence,state.deathSequence);
        bool wasDead=dead;dead=state.dead;
        if(dead)
        {
            predictedHitId=null;
            if(newDeath||!wasDead||Kind!="death")Play("death",Age(state.deathAt));
        }
        else
        {
            if(wasDead)Clear();
            if(newHit&&state.hitSequence>=state.attackSequence)
            {
                bool alreadyVisible=HasPredictedHit&&predictedHitId==state.hitAttackId&&Kind=="hit";
                predictedHitId=null;
                if(!alreadyVisible&&Age(state.hitAt)<hit.length)Play("hit",Age(state.hitAt));
            }
            else if(newAttack&&state.attackSequence>state.hitSequence)
            {
                predictedHitId=null;
                if(state.attackId!=predictedId&&Age(state.attackAt)<combo.length-PunchStart)Play("attack",Age(state.attackAt));
            }
        }
        return true;
    }
    void Play(string kind,float age)
    {
        if(!Ready)return;
        Kind=kind;started=Time.unscaledTime;ageAtStart=age;
        var clip=kind=="attack"?combo:kind=="hit"?hit:death;
        clipOffset=kind=="attack"?PunchStart:kind=="hit"?HitStart:0;
        // Skip the authored guard/neutral lead-in. Keep the existing attack
        // cooldown and 1.6-second hit lock; the reaction holds its recovered tail.
        duration=kind=="attack"?clip.length-clipOffset:clip.length;
        if(kind=="attack")AttackPlays++;else if(kind=="hit")HitPlays++;else DeathPlays++;
        animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
        animator.Play("Combat."+clip.name,layer,Mathf.Min(.999f,(clipOffset+age)/clip.length));
        animator.SetLayerWeight(layer,1);
    }
    void Update()
    {
        if(!Ready||Kind==null)return;
        if(HasPredictedHit&&Time.unscaledTime-predictedHitAt>=PredictionTimeout)
        {CancelPredictedHit(predictedHitId);return;}
        float elapsed=Elapsed;
        if(dead||Kind=="death")
        {
            animator.SetLayerWeight(layer,1);
            // This FBX falls to the ground. Hold its final pose instead of looping/getting up.
            if(elapsed>=duration-.015f)animator.Play("Combat.Stunned",layer,.999f);
            return;
        }
        float weight=Mathf.Clamp01((duration-elapsed)/.12f);
        animator.SetLayerWeight(layer,weight);
        if(elapsed>=duration)Clear();
    }
    public void Clear()
    {
        Kind=null;dead=false;predictedHitId=null;
        if(animator&&layer>=0){animator.SetLayerWeight(layer,0);animator.Play("Combat.Empty",layer,0);}
    }
    void OnDisable(){if(!dead)Clear();}
}
