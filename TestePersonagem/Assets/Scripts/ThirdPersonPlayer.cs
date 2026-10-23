using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonPlayer : MonoBehaviour
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
    public bool DemoMode { get; set; }
    public Vector2 DemoInput { get; set; }
    public bool DemoRun { get; set; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        animator.applyRootMotion=false;
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        if(AccountSession.Instance)AccountSession.Instance.AttachPlayer(this);
        GameHud.Bootstrap();
        MobileControls.Create();
        MobilePerformance.Configure();
        MultiplayerWorld.Create(this);
    }

    void Update()
    {
        if(StartupLoader.IsLoading || AccountSession.InputBlocked){jumpRequested=false;CurrentSpeed=0;animator.SetFloat("Speed",0);return;}
        bool panelOpen=InGameChat.Editing||(GameHud.Instance && GameHud.Instance.IsPanelOpen);
        if(!DemoMode&&!panelOpen&&Input.GetKeyDown(KeyCode.Space))jumpRequested=true;
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
        bool run = DemoMode ? DemoRun : Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || (GameHud.Instance && GameHud.Instance.SprintRequested);
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 direction = forward * input.y + right * input.x;
        float speed = input.magnitude * (run ? runSpeed : walkSpeed);
        if (direction.sqrMagnitude > .001f)
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
        if(IsJumping&&jumpLaunched&&controller.isGrounded&&jumpElapsed>=JumpRecovery)
        {IsJumping=false;animator.SetBool("Jumping",false);}
        CurrentSpeed = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, .0001f);
        animator.SetFloat("Speed", CurrentSpeed, .12f, Time.deltaTime);
        if (transform.position.y < -10) ResetPosition();
        if (!DemoMode && Input.GetKeyDown(KeyCode.R) && !panelOpen) ResetPosition();
    }

    public void ResetPosition()
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0, spawnYaw, 0));
        controller.enabled = true;
        verticalSpeed = 0;
        IsJumping=false;jumpRequested=false;jumpLaunched=false;
        animator.SetBool("Jumping",false);
        animator.Play("Locomocao",0,0);
        if(GameHud.Instance)GameHud.Instance.NotifyRespawn();
    }

    public void RequestJump()
    {
        if(StartupLoader.IsLoading||AccountSession.InputBlocked||InGameChat.Editing||IsJumping||!IsGrounded||
            (GameHud.Instance&&GameHud.Instance.IsPanelOpen))return;
        jumpRequested=true;
    }

}
