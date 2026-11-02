using System;
using UnityEngine;

[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class FighterCombo : MonoBehaviour
{
    ThirdPersonPlayer player;
    CombatAvatarAnimation animation;
    float nextAttack;
    public CombatAvatarAnimation Animation => EnsureAnimation();

    void Awake(){player=GetComponent<ThirdPersonPlayer>();}
    CombatAvatarAnimation EnsureAnimation()
    {
        if(!player)player=GetComponent<ThirdPersonPlayer>();
        if(!player||!player.animator)return null;
        if(!animation||animation.gameObject!=player.animator.gameObject)
        {
            animation=player.animator.GetComponent<CombatAvatarAnimation>();
            if(!animation)animation=player.animator.gameObject.AddComponent<CombatAvatarAnimation>();
            animation.Initialize(player.animator);
        }
        return animation;
    }
    void Update(){if(Input.GetKeyDown(KeyCode.F))Trigger();}
    public void Trigger()
    {
        var rifle=GetComponent<RifleWeapon>();
        if(rifle&&rifle.Equipped){rifle.RequestFire();return;}
        var visual=EnsureAnimation();
        if(!enabled||!player||!visual||!visual.Ready||Time.unscaledTime<nextAttack||visual.Busy||player.CombatLocked||
            player.Health<=0||player.IsCuffed||player.InPoliceCar||PoliceVehicleController.BlocksPlayer(player)||MissionVehicle.Driver==player||
            !player.IsGrounded||player.IsJumping||player.IsSpectating||player.IsTeleporting||player.DemoMode||
            StartupLoader.IsLoading||AccountSession.InputBlocked||InGameChat.Editing||
            (GameHud.Instance&&GameHud.Instance.IsPanelOpen))return;
        var world=MultiplayerWorld.Instance;
        if(AccountSession.HasCharacter&&(!world||!world.Connected||!world.CombatReady))
        {GameHud.Instance?.ShowAccountMessage("Aguarde o combate ficar disponível no servidor.");return;}
        string attackId=Guid.NewGuid().ToString();
        // Start on this frame. The confirmed echo uses the same id and cannot restart it.
        visual.PredictAttack(attackId);nextAttack=Time.unscaledTime+visual.AttackDuration;
        if(world&&world.Connected)world.Attack(attackId);
    }
    public void ApplyState(CombatState state,long serverTime,float roundTrip=0)
    {
        var visual=EnsureAnimation();
        if(state!=null&&visual&&visual.Apply(state,serverTime,roundTrip))player.Health=state.health;
    }
}
