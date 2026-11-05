using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public partial class ThirdPersonPlayer : MonoBehaviour
{
    public Transform cameraTransform;
    public Animator animator;
    public float walkSpeed = 1.65f;
    public float runSpeed = 4.5f;
    public float turnSpeed = 540f;
    public float jumpHeight = .75f;
    public float jumpPreparation = .105f;
    const float Gravity = 22f;
    const float JumpRecovery = 1.15f;
    float jumpElapsed;
    bool jumpLaunched, jumpRequested;
    public bool IsJumping { get; private set; }
    public float JumpPhase => IsJumping?Mathf.Clamp01(jumpElapsed/JumpRecovery):0;
    public bool IsGrounded => controller && controller.isGrounded;
    public Vector3 spawnPosition = new Vector3(0, .05f, 0);
    public float spawnYaw = 0f;
    public float Health = 100f;
    public float Energy = 100f;
    public float Hydration = 100f;
    public float Hunger = 100f;
    public long Cash;
    public int Level = 1;
    public float Experience;
    CharacterController controller;
    float verticalSpeed;
    public float CurrentSpeed { get; private set; }
    bool combatLocked;
    public bool CombatLocked
    {
        get=>combatLocked||(animator&&animator.TryGetComponent<CombatAvatarAnimation>(out var combat)&&combat.Busy);
        set=>combatLocked=value;
    }
    int adminSpeedMultiplier = 1;
    Coroutine teleportRoutine;
    bool teleporting;
    public bool IsTeleporting => teleporting;
    public bool IsSpectating {get;private set;}
    public bool IsCuffed {get;private set;}
    public bool InPoliceCar {get;private set;}
    float policeCarOldDistance,policeCarOldPitch;
    Coroutine policeCarExitRoutine;
    Vector3 custodyPosition;
    float custodyYaw;
    bool escorted;
    CuffedAvatarPose cuffedPose;
    public int AdminSpeedMultiplier => HasAdminAccess ? adminSpeedMultiplier : 1;
    bool HasAdminAccess => AdminPermissions.CurrentCanUseTools;
    public bool SetAdminSpeedMultiplier(int multiplier)
    {
        if (!HasAdminAccess || (multiplier != 1 && multiplier != 2 && multiplier != 4 &&
            multiplier != 6 && multiplier != 8 && multiplier != 10 && multiplier != 12)) return false;
        adminSpeedMultiplier = multiplier;
        return true;
    }
    public bool DemoMode { get; set; }
    public Vector2 DemoInput { get; set; }
    public bool DemoRun { get; set; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        groundStreaming = FindAnyObjectByType<GtaSaSectorStreaming>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        animator.applyRootMotion=false;
        cuffedPose=animator.GetComponent<CuffedAvatarPose>();
        if(!cuffedPose)cuffedPose=animator.gameObject.AddComponent<CuffedAvatarPose>();
        Application.targetFrameRate = 60;
    }

    public void RebindVisual(Animator replacement)
    {
        if (!replacement) return;
        var pose = replacement.GetComponent<CuffedAvatarPose>();
        if (!pose) pose = replacement.gameObject.AddComponent<CuffedAvatarPose>();
        pose.Cuffed = IsCuffed;
        replacement.applyRootMotion = false;
        animator = replacement;
        cuffedPose = pose;
    }

    void Start()
    {
        if(AccountSession.Instance)AccountSession.Instance.AttachPlayer(this);
        GameHud.Bootstrap();
        HousePortal.Create(this);
        // Initial tutorial is paused while the new city is being evaluated.
        MobileControls.Create();
        MobilePerformance.Configure();
        MultiplayerWorld.Create(this);
        VoiceChat.Create(this);
        RifleWeapon.Create(this);
        PlayerLifeHud.Create(this);
        gameObject.AddComponent<PoliceDutyLabel>();
    }

    void Update()
    {
        if(PoliceVehicleController.BlocksPlayer(this)||policeCarExitRoutine!=null){CurrentSpeed=0;jumpRequested=false;return;}
        if(MissionVehicle.Driver==this){CurrentSpeed=0;jumpRequested=false;return;}
        if(InPoliceCar){CurrentSpeed=0;jumpRequested=false;return;}
        if (!HasAdminAccess) adminSpeedMultiplier = 1;
        if(StartupLoader.IsLoading || IntegratedMapEntry.Loading || AccountSession.InputBlocked || teleporting || IsSpectating)
        {jumpRequested=false;CurrentSpeed=0;animator.SetFloat("Speed",0);return;}
        if(Health<=0||(animator.TryGetComponent<CombatAvatarAnimation>(out var combat)&&combat.Busy))
        {
            jumpRequested=false;IsJumping=false;jumpLaunched=false;
            CurrentSpeed=0;animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
            if(controller.enabled)
            {
                if(WaitForStreamingGround(Vector3.zero))return;
                if(controller.isGrounded&&verticalSpeed<0)verticalSpeed=-2;
                verticalSpeed=Mathf.Max(verticalSpeed-Gravity*Time.deltaTime,-30);
                controller.Move(Vector3.up*verticalSpeed*Time.deltaTime);
            }
            return;
        }
        if(IsCuffed)
        {
            jumpRequested=false;IsJumping=false;animator.SetBool("Jumping",false);
            Vector3 delta=Vector3.ProjectOnPlane(custodyPosition-transform.position,Vector3.up);
            float custodySpeed=escorted?1.65f:2.5f;
            Vector3 horizontal=delta.magnitude>.03f?delta.normalized*Mathf.Min(custodySpeed,delta.magnitude/Mathf.Max(Time.deltaTime,.001f)):Vector3.zero;
            if(WaitForStreamingGround(horizontal*Time.deltaTime))return;
            if(controller.isGrounded&&verticalSpeed<0)verticalSpeed=-2f;
            verticalSpeed=Mathf.Max(verticalSpeed-Gravity*Time.deltaTime,-30f);
            controller.Move((horizontal+Vector3.up*verticalSpeed)*Time.deltaTime);
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.Euler(0,custodyYaw,0),turnSpeed*Time.deltaTime);
            CurrentSpeed=horizontal.magnitude;animator.SetFloat("Speed",CurrentSpeed,.12f,Time.deltaTime);
            return;
        }
        bool panelOpen=InGameChat.Editing||(GameHud.Instance && GameHud.Instance.IsPanelOpen);
        if(!DemoMode&&!panelOpen&&Input.GetKeyDown(KeyCode.Space))jumpRequested=true;
        if(CombatLocked)jumpRequested=false;
        if(jumpRequested)
        {
            if(!panelOpen&&controller.isGrounded&&!IsJumping)
            {IsJumping=true;jumpLaunched=false;jumpElapsed=0;animator.SetBool("Jumping",true);}
            jumpRequested=false;
        }
        Vector2 input = DemoMode ? DemoInput : new Vector2(
            (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
            (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
            (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) -
            (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
        if(!DemoMode && MobileControls.HasTouches)input=MobileControls.Movement;
        input = Vector2.ClampMagnitude(input, 1);
        if (InGameChat.Editing||(!DemoMode && GameHud.Instance && GameHud.Instance.IsPanelOpen)) input=Vector2.zero;
        if(CombatLocked)input=Vector2.zero;
        bool run = DemoMode ? DemoRun : AdminSpeedMultiplier > 1 || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || (GameHud.Instance && GameHud.Instance.SprintRequested);
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 direction = forward * input.y + right * input.x;
        bool rifleAiming=TryGetComponent<RifleWeapon>(out var rifle)&&rifle.Equipped&&rifle.Aiming;
        float speed = input.magnitude * (run&&!rifleAiming ? runSpeed * (DemoMode ? 1 : AdminSpeedMultiplier) : walkSpeed);
        if(WaitForStreamingGround(direction*speed*Time.deltaTime))return;
        if(rifleAiming)
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(forward),turnSpeed*Time.deltaTime);
        else if (direction.sqrMagnitude > .001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
        if(IsJumping)
        {
            jumpElapsed+=Time.deltaTime;
            if(!jumpLaunched&&jumpElapsed>=jumpPreparation)
            {verticalSpeed=Mathf.Sqrt(2f*Gravity*jumpHeight);jumpLaunched=true;}
        }
        if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
        verticalSpeed = Mathf.Max(verticalSpeed - Gravity * Time.deltaTime, -30f);
        Vector3 before = transform.position;
        var collisions=controller.Move((direction * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        if((collisions&CollisionFlags.Above)!=0&&verticalSpeed>0)verticalSpeed=0;
        bool landed=IsJumping&&jumpLaunched&&verticalSpeed<=0&&
            (controller.isGrounded||(collisions&CollisionFlags.Below)!=0);
        if(landed)
        {
            IsJumping=false;jumpLaunched=false;verticalSpeed=-2f;
            animator.SetBool("Jumping",false);
        }
        CurrentSpeed = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, .0001f);
        if(landed)
        {
            animator.SetFloat("Speed",CurrentSpeed);
            float landingBlend = .06f;
            foreach (var info in animator.GetCurrentAnimatorClipInfo(0))
                if (info.clip && info.clip.name == "PuloSA") landingBlend = .18f;
            animator.CrossFadeInFixedTime("Locomocao",landingBlend,0);
        }
        else animator.SetFloat("Speed", CurrentSpeed, .12f, Time.deltaTime);
        float fallLimit = NeighborhoodMap.Active && NeighborhoodMap.Active.isOfficialCity ? NeighborhoodMap.Active.minY : -10;
        if (transform.position.y < fallLimit) ResetPosition();
        if (!DemoMode && Input.GetKeyDown(KeyCode.R) && !panelOpen && !(rifle&&rifle.Equipped)) ResetPosition();
    }

    public void ResetPosition()
    {
        if(IsCuffed)return;
        if(FindAnyObjectByType<GtaSaSectorStreaming>())
        {TeleportTo(spawnPosition,spawnYaw);return;}
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0, spawnYaw, 0));
        controller.enabled = true;
        verticalSpeed = 0;
        IsJumping=false;jumpRequested=false;jumpLaunched=false;
        animator.SetBool("Jumping",false);
        animator.Play("Locomocao",0,0);
        if(GameHud.Instance)GameHud.Instance.NotifyRespawn();
    }

    // Online callers must use the confirmed respawn event, never a client-supplied heal.
    public void ApplyConfirmedRespawn(Vector3 position,float yaw)
    {
        Health=100;CurrentSpeed=0;combatLocked=false;verticalSpeed=0;
        jumpRequested=jumpLaunched=IsJumping=false;
        GetComponent<RifleWeapon>()?.SetEquipped(false);
        if(policeCarExitRoutine!=null){StopCoroutine(policeCarExitRoutine);policeCarExitRoutine=null;}
        IsCuffed=false;escorted=false;InPoliceCar=false;
        if(cuffedPose)cuffedPose.Cuffed=false;
        if(animator)
        {
            animator.GetComponent<CombatAvatarAnimation>()?.Clear();
            animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
            animator.Play("Locomocao",0,0);
        }
        TeleportTo(position,yaw);
    }

    public void TeleportTo(Vector3 destination)
    {
        if(teleportRoutine!=null)StopCoroutine(teleportRoutine);
        teleportRoutine=StartCoroutine(TeleportRoutine(destination,null));
    }
    public void TeleportTo(Vector3 destination,float yaw)
    {
        if(teleportRoutine!=null)StopCoroutine(teleportRoutine);
        teleportRoutine=StartCoroutine(TeleportRoutine(destination,yaw));
    }
    IEnumerator TeleportRoutine(Vector3 destination,float? yaw)
    {
        teleporting=true;controller.enabled=false;
        transform.position=destination;
        if(yaw.HasValue)transform.rotation=Quaternion.Euler(0,yaw.Value,0);
        verticalSpeed=0;IsJumping=false;jumpRequested=false;jumpLaunched=false;
        animator.SetBool("Jumping",false);animator.SetFloat("Speed",0);
        var streaming=FindAnyObjectByType<CitySectorStreaming>();
        if(streaming)
        {
            streaming.RefreshNow();
            float deadline=Time.realtimeSinceStartup+15f;
            while(!streaming.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
        }
        var saStreaming=FindAnyObjectByType<GtaSaSectorStreaming>();
        if(saStreaming)
        {
            saStreaming.RefreshNow();
            while(!saStreaming.Ready && string.IsNullOrEmpty(saStreaming.LastError))yield return null;
            if(!saStreaming.Ready){Debug.LogError(saStreaming.LastError);yield break;}
        }
        controller.enabled=true;verticalSpeed=-2f;teleporting=false;teleportRoutine=null;
        if(GameHud.Instance)GameHud.Instance.NotifyRespawn();
    }
    public void SetSpectating(bool value)
    {
        IsSpectating=value;
        if(!value)verticalSpeed=0;
    }

    public void RequestJump()
    {
        if(IsCuffed||StartupLoader.IsLoading||AccountSession.InputBlocked||InGameChat.Editing||CombatLocked||IsJumping||!IsGrounded||
            (GameHud.Instance&&GameHud.Instance.IsPanelOpen))return;
        jumpRequested=true;
    }

    public void SetCustody(bool cuffed,string officerUid,Vector3 position,float yaw)
    {
        bool changed=IsCuffed!=cuffed;
        IsCuffed=cuffed;escorted=cuffed&&!string.IsNullOrEmpty(officerUid);
        custodyPosition=position;custodyYaw=yaw;
        if(cuffedPose)cuffedPose.Cuffed=cuffed;
        if(changed&&GameHud.Instance)GameHud.Instance.ShowAccountMessage(cuffed?"Você foi algemado":"Algemas retiradas");
    }

    public void SetPoliceCarPassenger(bool inCar,Vector3 seatPosition,float seatYaw)
    {
        if(InPoliceCar==inCar)
        {
            if(inCar)transform.SetPositionAndRotation(seatPosition,Quaternion.Euler(0,seatYaw,0));
            return;
        }
        InPoliceCar=inCar;
        var orbit=FindAnyObjectByType<ThirdPersonCamera>();
        if(inCar)
        {
            if(policeCarExitRoutine!=null){StopCoroutine(policeCarExitRoutine);policeCarExitRoutine=null;}
            controller.enabled=false;transform.SetPositionAndRotation(seatPosition,Quaternion.Euler(0,seatYaw,0));
            if(orbit)orbit.RetargetPreservingView(transform,false);
            animator.gameObject.SetActive(false);
            GameHud.Instance?.ShowAccountMessage("Você está no banco traseiro da viatura");
        }
        else
        {
            transform.SetPositionAndRotation(seatPosition,Quaternion.Euler(0,seatYaw,0));
            if(orbit)orbit.RetargetPreservingView(transform,false);
            policeCarExitRoutine=StartCoroutine(FinishPolicePassengerExit(orbit));
        }
    }

    IEnumerator FinishPolicePassengerExit(ThirdPersonCamera orbit)
    {
        yield return null;
        float deadline=Time.unscaledTime+3;
        string uid=AccountSession.Instance?.State?.uid;
        while(PoliceCarWorld.Instance&&PoliceCarWorld.Instance.PassengerExiting(uid)&&Time.unscaledTime<deadline)yield return null;
        animator.gameObject.SetActive(true);controller.enabled=true;
        if(orbit)orbit.RetargetPreservingView(transform,false);
        policeCarExitRoutine=null;
    }

}
