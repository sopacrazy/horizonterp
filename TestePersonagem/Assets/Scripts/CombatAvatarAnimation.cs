using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class CombatState
{
    public string epoch,attackId,hitAttackId;
    public int revision,attackSequence,hitSequence,deathSequence,respawnSequence,hitReactions;
    public long attackAt,hitAt,deathAt,respawnAt;
    public float respawnX,respawnY,respawnZ,respawnYaw;
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
    static readonly float[] MeleeHitOffsets={0,.9333333f,2.4666667f};
    static readonly Dictionary<string,int> reactionCounts=new Dictionary<string,int>();
    static readonly Queue<string> reactionIds=new Queue<string>();
    int hitCount=1,hitIndex;
    bool unresolvedHitCount;
    string currentHitId;
    bool importedHit;
    float HitLeadIn=>importedHit?0:HitStart;
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
    public float CurrentClipSeconds=>Mathf.Min(Kind=="attack"?combo.length:Kind=="hit"?hit.length:death?death.length:0,clipOffset+Elapsed-(Kind=="hit"?MeleeHitOffsets[hitIndex]:0));
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
        importedHit=hit&&Array.Exists(Resources.LoadAll<AnimationClip>("SAExpanded/HitReaction_Personagem"),c=>c==hit);
        if(layer>=0)animator.SetLayerWeight(layer,0);
    }
    static AnimationClip Load(string name)
    {
        if(name=="HitReaction")
        {
            var reaction=Array.Find(Resources.LoadAll<AnimationClip>("SAExpanded/HitReaction_Personagem"),x=>x.humanMotion&&!x.name.StartsWith("__preview"));
            if(reaction)return reaction;
        }
        if(name=="PunchCombo")
        {
            var fightB=Array.Find(Resources.LoadAll<AnimationClip>("Combat/FightBCombo_Personagem"),x=>x.name==name&&x.humanMotion);
            if(fightB)return fightB;
        }
        return Array.Find(Resources.LoadAll<AnimationClip>("Combat/"+name),x=>x.name==name);
    }
    public void PredictAttack(string attackId)
    {
        if(!Ready||dead)return;
        RegisterReactionCount(attackId,3);predictedId=attackId;Play("attack",0);
    }
    // Visual anticipation on the attacker's remote replica only. Health and all
    // authoritative sequence numbers stay unchanged until the API confirms it.
    public bool PredictHit(string attackId,int reactions=3)
    {
        if(!Ready||dead||string.IsNullOrEmpty(attackId))return false;
        if(predictedHitId==attackId)return true;
        RegisterReactionCount(attackId,reactions);
        hitCount=Mathf.Clamp(reactions,1,3);currentHitId=attackId;unresolvedHitCount=false;
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
        if(state.attackSequence>0&&!string.IsNullOrEmpty(state.attackId))RegisterReactionCount(state.attackId,3);
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
                currentHitId=state.hitAttackId;
                unresolvedHitCount=state.hitReactions<=0&&!reactionCounts.ContainsKey(currentHitId??"");
                hitCount=state.hitReactions>0?Mathf.Clamp(state.hitReactions,1,3):reactionCounts.TryGetValue(currentHitId??"",out var known)?known:1;
                float hitDuration=MeleeHitOffsets[hitCount-1]+hit.length;
                if(alreadyVisible)duration=hitDuration;
                else if(Age(state.hitAt)<hitDuration)Play("hit",Age(state.hitAt));
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
        clipOffset=kind=="attack"?PunchStart:kind=="hit"?HitLeadIn:0;
        // Both attack assets have a half-second guard lead-in. Start at the
        // first strike; the cooldown follows the complete authored clip.
        duration=kind=="attack"?clip.length-clipOffset:kind=="hit"?MeleeHitOffsets[hitCount-1]+clip.length:clip.length;
        hitIndex=0;
        if(kind=="hit")while(hitIndex+1<hitCount&&age>=MeleeHitOffsets[hitIndex+1])hitIndex++;
        if(kind=="attack")AttackPlays++;else if(kind=="hit")HitPlays++;else DeathPlays++;
        animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
        if(kind=="attack")animator.CrossFadeInFixedTime("Combat."+clip.name,.06f,layer,Mathf.Min(clip.length-.001f,clipOffset+age));
        else animator.Play("Combat."+(kind=="hit"?"HitReaction":clip.name),layer,Mathf.Min(.999f,(clipOffset+age-(kind=="hit"?MeleeHitOffsets[hitIndex]:0))/clip.length));
        animator.SetLayerWeight(layer,1);
    }
    void Update()
    {
        if(!Ready||Kind==null)return;
        if(HasPredictedHit&&Time.unscaledTime-predictedHitAt>=PredictionTimeout)
        {CancelPredictedHit(predictedHitId);return;}
        float elapsed=Elapsed;
        if(Kind=="hit")
        {
            if(unresolvedHitCount&&reactionCounts.TryGetValue(currentHitId??"",out var known))
            {hitCount=known;duration=MeleeHitOffsets[hitCount-1]+hit.length;unresolvedHitCount=false;}
            int next=hitIndex;
            while(next+1<hitCount&&elapsed>=MeleeHitOffsets[next+1])next++;
            if(next!=hitIndex)
            {
                hitIndex=next;HitPlays++;
                animator.Play("Combat.HitReaction",layer,Mathf.Min(.999f,(HitLeadIn+elapsed-MeleeHitOffsets[hitIndex])/hit.length));
            }
        }
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
        Kind=null;dead=false;predictedHitId=null;unresolvedHitCount=false;currentHitId=null;hitCount=1;hitIndex=0;
        if(animator&&layer>=0){animator.SetLayerWeight(layer,0);animator.Play("Combat.Empty",layer,0);}
    }
    void OnDisable(){if(!dead)Clear();}
    static void RegisterReactionCount(string id,int count)
    {
        if(string.IsNullOrEmpty(id))return;
        if(!reactionCounts.ContainsKey(id))reactionIds.Enqueue(id);
        reactionCounts[id]=Mathf.Clamp(count,1,3);
        while(reactionIds.Count>128)reactionCounts.Remove(reactionIds.Dequeue());
    }
}
