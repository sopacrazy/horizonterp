using System;
using System.Collections;
using UnityEngine;

// Client presentation/controls after the API grants the driver's seat.
public sealed class PoliceVehicleController : MonoBehaviour
{
    [Serializable] sealed class CarAction {public string action,carId;}
    public static PoliceVehicleController Active {get;private set;}
    // An HTTP room snapshot started before an enter/exit action cannot undo it.
    public static int StateRevision {get;private set;}
    public static bool BlocksPlayer(ThirdPersonPlayer p)=>Active&&Active.driver==p;
    public static bool Driving=>Active&&Active.driver&&!Active.exiting&&!Active.exitPending;
    public static string DrivingCarId=>Driving?Active.Id:null;
    public string Id {get;private set;}
    public float Speed {get;private set;}
    public bool Busy {get;private set;}
    public bool LocalPreview {get;set;}
#if UNITY_EDITOR
    public Vector2? PreviewInput {get;set;}
#endif
    public bool Occupied=>driver;
    public float EntryDistance(ThirdPersonPlayer p)=>p?Vector3.Distance(p.transform.position,transform.TransformPoint(rig.DriverDoor)):float.PositiveInfinity;
    PoliceSeatAvatar seat;
    PoliceCarRig rig;
    CharacterController body;
    BoxCollider parked;
    ThirdPersonPlayer driver;
    ThirdPersonCamera camera;
    Transform previousTarget;
    float previousDistance,previousPitch,vertical;
    bool exiting,exitPending,exitReplyLost,oldCombat,oldController,oldVisible;
    Vector3 exitPoint;
    Quaternion exitRotation;
    public void Initialize(string id,PoliceCarRig model,PoliceSeatAvatar avatar)
    {
        Id=id;rig=model;seat=avatar;
        gameObject.AddComponent<VehicleHud>().Initialize(this);
        parked=GetComponent<BoxCollider>();body=gameObject.AddComponent<CharacterController>();
        body.height=1.8f;body.center=new Vector3(0,.9f,0);body.radius=.88f;body.skinWidth=.03f;
        body.stepOffset=.3f;body.slopeLimit=40;body.enabled=false;
    }
    public void ApplyState(PoliceCarState state,ThirdPersonPlayer owner=null,string selfCarId=null,Vector3? serverPosition=null)
    {
        if(LocalPreview||selfCarId==null||!AccountSession.HasCharacter)return;
        bool ours=selfCarId==Id&&state.driverUid==AccountSession.Instance.State.uid;
        // A sync can confirm a successful action even when its HTTP reply was lost.
        if(ours&&!driver&&!Active&&owner)
            StartCoroutine(BeginDriving(owner));
        else if(ours&&exitPending&&exitReplyLost)
        {exitPending=exitReplyLost=false;Busy=false;++StateRevision;}
        else if(!ours&&driver&&!exiting)
        {
            if(exitPending)StartCoroutine(FinishExit(ResolveExit(serverPosition)));
            else if(!Busy)Abort();
        }
    }
    public void RequestEnter(ThirdPersonPlayer player)
    {
        if(!LocalPreview&&(!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.PoliceVehicleAvailable))
        {
            GameHud.Instance?.ShowAccountMessage("Aguardando sincronização da viatura com o servidor.");
            return;
        }
        if(Busy||Active||!player||player.IsCuffed||player.InPoliceCar||player.IsJumping||player.IsTeleporting||player.IsSpectating||player.CombatLocked||MissionVehicle.Driver||EntryDistance(player)>.85f)return;
        StartCoroutine(EnterRoutine(player));
    }
    IEnumerator EnterRoutine(ThirdPersonPlayer player)
    {
        Busy=true;int revision=++StateRevision;
        if(!LocalPreview)
        {
            AdminReply reply=null;
            yield return AccountSession.Instance.AdminRequest("POST","/v1/police/car",JsonUtility.ToJson(new CarAction{action="enter",carId=Id}),r=>reply=r);
            if(revision!=StateRevision)yield break;
            if(reply==null||!string.IsNullOrEmpty(reply.code)||!reply.driving)
            {Busy=false;++StateRevision;GameHud.Instance?.ShowToast(reply?.error??"Não foi possível entrar na viatura.");yield break;}
        }
        yield return BeginDriving(player);
    }
    IEnumerator BeginDriving(ThirdPersonPlayer player)
    {
        if(!player||!this||Active||(!LocalPreview&&!AccountSession.HasCharacter)){Busy=false;yield break;}
        ++StateRevision;Busy=true;driver=player;Active=this;exiting=exitPending=exitReplyLost=false;Speed=0;vertical=0;
        oldCombat=player.CombatLocked;oldController=player.GetComponent<CharacterController>().enabled;oldVisible=player.animator.gameObject.activeSelf;
        seat.SetOccupant(LocalPreview?"PRÉVIA":AccountSession.Instance.State.uid,player.transform.position,startYaw:player.transform.eulerAngles.y);
        player.CombatLocked=true;player.GetComponent<CharacterController>().enabled=false;
        player.animator.gameObject.SetActive(false);
        parked.enabled=false;body.enabled=true;
        camera=FindAnyObjectByType<ThirdPersonCamera>();
        if(camera)
        {
            previousTarget=camera.target;previousDistance=camera.distance;previousPitch=camera.pitch;
            camera.RetargetPreservingView(transform,true);
        }
        while(seat&&seat.Transitioning&&driver)yield return null;
        // Keep the freely selected orbit after the animation finishes.
        Busy=false;
    }
    public void RequestExit()
    {
        if(Busy||!driver||exiting)return;
        if(Mathf.Abs(Speed)>.6f){GameHud.Instance?.ShowToast("Pare a viatura antes de sair.");return;}
        if(!FindExit(out var point)){GameHud.Instance?.ShowToast("Não há espaço para abrir a porta deste lado.");return;}
        StartCoroutine(ExitRoutine(point));
    }
    IEnumerator ExitRoutine(Vector3 point)
    {
        Busy=true;Speed=0;exitPending=true;exitReplyLost=false;exitPoint=point;int revision=++StateRevision;
        // Before release the room pins neutral frames to the car; after release
        // the same frames must already carry the checked foot position.
        driver.transform.SetPositionAndRotation(point,ExitFacing(point));
        if(!LocalPreview)
        {
            AdminReply reply=null;
            yield return AccountSession.Instance.AdminRequest("POST","/v1/police/car",JsonUtility.ToJson(new CarAction{action="exit",carId=Id}),r=>reply=r);
            if(revision!=StateRevision)yield break;
            if(reply==null||reply.code=="CONNECTION_LOST")
            {
                // Keep frames neutral until a fresh room snapshot resolves an
                // ambiguous timeout; resending a released seat ID would be denied.
                exitReplyLost=true;++StateRevision;
                GameHud.Instance?.ShowToast("Confirmando a saída da viatura...");yield break;
            }
            if(reply==null||!string.IsNullOrEmpty(reply.code)||reply.driving)
            {exitPending=false;Busy=false;++StateRevision;GameHud.Instance?.ShowToast(reply?.error??"Não foi possível sair da viatura.");yield break;}
            point=ResolveExit(new Vector3(reply.x,reply.y,reply.z));
        }
        yield return FinishExit(point);
    }
    IEnumerator FinishExit(Vector3 point)
    {
        ++StateRevision;Busy=true;exiting=true;exitPending=exitReplyLost=false;exitPoint=point;
        exitRotation=ExitFacing(point);
        driver.transform.SetPositionAndRotation(exitPoint,exitRotation);
        seat.SetOccupant("",exitPoint,startYaw:exitRotation.eulerAngles.y);
        while(seat&&seat.Transitioning&&driver)yield return null;
        RestorePlayer();Busy=false;
    }
    bool FindExit(out Vector3 point)
    {
        foreach(float side in new[]{-1f,1f})foreach(float offset in new[]{0f,.45f,-.45f})
        {
            if(TryExitPoint(transform.position+transform.right*(side*2.1f)+transform.forward*offset,out point))return true;
        }
        point=default;return false;
    }
    Vector3 ResolveExit(Vector3? serverPosition)
    {
        if(serverPosition.HasValue&&(serverPosition.Value-transform.position).sqrMagnitude<16&&
            Mathf.Abs(Vector3.Dot(serverPosition.Value-transform.position,transform.right))>1.3f&&TryExitPoint(serverPosition.Value,out var checkedPoint))return checkedPoint;
        if(TryExitPoint(exitPoint,out checkedPoint))return checkedPoint;
        return FindExit(out checkedPoint)?checkedPoint:exitPoint;
    }
    bool TryExitPoint(Vector3 candidate,out Vector3 point)
    {
        point=default;
        if(!Physics.Raycast(candidate+Vector3.up*3,Vector3.down,out var hit,7,1,QueryTriggerInteraction.Ignore)||hit.normal.y<.7f)return false;
        candidate=hit.point+Vector3.up*.04f;
        var map=NeighborhoodMap.Active;if(map&&map.restrictRegion&&!map.ContainsXZ(candidate))return false;
        if(Physics.CheckCapsule(candidate+Vector3.up*.35f,candidate+Vector3.up*1.5f,.32f,1,QueryTriggerInteraction.Ignore))return false;
        point=candidate;return true;
    }
    Quaternion ExitFacing(Vector3 point)=>Quaternion.Euler(0,transform.eulerAngles.y+(Vector3.Dot(point-transform.position,transform.right)>0?90:-90),0);
    void Update()
    {
        if(!driver)return;
        if(!LocalPreview&&(!AccountSession.HasCharacter||!AdminPlayerPanel.PoliceDuty)){Abort();return;}
        bool blocked=Busy||exiting||rig.RearTransitioning||StartupLoader.IsLoading||AccountSession.InputBlocked||InGameChat.Editing||(GameHud.Instance&&GameHud.Instance.IsPanelOpen);
        Vector2 input=blocked?Vector2.zero:MobileControls.HasTouches?MobileControls.Movement:new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
#if UNITY_EDITOR
        if(LocalPreview&&!blocked&&PreviewInput.HasValue)input=Vector2.ClampMagnitude(PreviewInput.Value,1);
#endif
        if(!blocked&&Input.GetKeyDown(KeyCode.F)){RequestExit();input=Vector2.zero;}
        if(exiting)return;
        if(!blocked&&VehicleHud.PedalsHeld)input.y=VehicleHud.Throttle;
        seat.Steering=input.x;
        Speed=Mathf.MoveTowards(Speed,input.y*7.5f,(blocked||VehicleHud.Braking?15:5)*Time.deltaTime);
        float steering=input.x*Mathf.Clamp(Speed/3,-1,1);
        if(Mathf.Abs(Speed)>.15f)transform.Rotate(0,steering*65*Time.deltaTime,0);
        if(body.isGrounded&&vertical<0)vertical=-2;vertical=Mathf.Max(-30,vertical-22*Time.deltaTime);
        Vector3 before=transform.position;
        body.Move((transform.forward*Speed+Vector3.up*vertical)*Time.deltaTime);
        var map=NeighborhoodMap.Active;
        if(map&&map.restrictRegion&&!map.ContainsXZ(transform.position))
        {
            var bounded=transform.position;bounded.x=Mathf.Clamp(bounded.x,map.regionMin.x+4,map.regionMax.x-4);bounded.z=Mathf.Clamp(bounded.z,map.regionMin.y+4,map.regionMax.y-4);
            body.enabled=false;transform.position=bounded;body.enabled=true;Speed=0;
        }
        float distance=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
        rig.RotateWheels(distance*Mathf.Sign(Speed),input.x);
    }
    void LateUpdate(){if(driver&&!exiting&&!exitPending)driver.transform.SetPositionAndRotation(transform.position,transform.rotation);}
    void RestorePlayer()
    {
        if(driver)
        {
            driver.transform.SetPositionAndRotation(exitPoint,exitRotation);
            driver.animator.SetFloat("Speed",0);driver.animator.SetBool("Jumping",false);
            driver.animator.gameObject.SetActive(oldVisible);driver.CombatLocked=oldCombat;
            driver.GetComponent<CharacterController>().enabled=oldController;
            if(camera)
            {
                camera.RetargetPreservingView(previousTarget?previousTarget:driver.transform,false);
            }
        }
        driver=null;if(Active==this)Active=null;Speed=0;exiting=exitPending=exitReplyLost=false;
        GetComponent<VehicleHud>()?.ReleaseControls();
        if(body)body.enabled=false;if(parked)parked.enabled=true;
    }
    public void Abort()
    {
        if(!driver&&!Busy)return;
        ++StateRevision;StopAllCoroutines();Busy=false;exitPending=exitReplyLost=false;if(!driver)return;
        if(!FindExit(out exitPoint))exitPoint=transform.position-transform.right*2.1f+Vector3.up*.1f;
        exitRotation=Quaternion.Euler(0,transform.eulerAngles.y+90,0);
        if(seat)seat.Abort();RestorePlayer();Busy=false;
    }
    void OnDestroy(){Abort();}
}
