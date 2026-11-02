using UnityEngine;
using UnityEngine.UIElements;

[DefaultExecutionOrder(110)]
[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class RifleWeapon : MonoBehaviour
{
    public static RifleWeapon Instance {get;private set;}
    public bool Equipped {get;private set;}
    public bool Aiming {get;private set;}
    public int ShotSequence {get;private set;}
    public float AimPitch=>orbit?orbit.pitch:0;
    public int Ammo {get;private set;}=30;
    public bool Reloading=>reloadUntil>Time.unscaledTime;
    ThirdPersonPlayer player;
    ThirdPersonCamera orbit;
    RifleAvatar visual;
    VisualElement overlay,cross,fireButton;
    Button aimButton,reloadButton;
    Label counter;
    bool heldFire,mobileAim;
    int firePointer=-1;
    float nextShot,reloadUntil;
    AudioSource audioSource;
    AudioClip shotSound;

    public static RifleWeapon Create(ThirdPersonPlayer player)
        =>player.GetComponent<RifleWeapon>()??player.gameObject.AddComponent<RifleWeapon>();
    void Awake(){Instance=this;player=GetComponent<ThirdPersonPlayer>();}
    void Start(){orbit=FindAnyObjectByType<ThirdPersonCamera>();CreateSound();}
    bool Blocked=>!player||player.Health<=0||player.IsCuffed||player.InPoliceCar||player.IsSpectating||player.IsTeleporting||
        player.CombatLocked||PoliceVehicleController.BlocksPlayer(player)||MissionVehicle.Driver==player||
        (ShootingRange.Instance&&ShootingRange.Instance.IsActive)||StartupLoader.IsLoading||IntegratedMapEntry.Loading||
        AccountSession.InputBlocked||InGameChat.Editing||(GameHud.Instance&&GameHud.Instance.IsPanelOpen);

    public bool SetEquipped(bool value)
    {
        if(value&&Blocked){GameHud.Instance?.ShowToast("Não é possível equipar a arma agora.");return false;}
        EnsureVisual();
        if(value&&(!visual||!visual.Ready)){GameHud.Instance?.ShowToast("Rifle ainda não preparado nesta versão.");return false;}
        Equipped=value;heldFire=mobileAim=false;SetAiming(false);
        visual?.Apply(value,false,ShotSequence,AimPitch,0);
        if(!value)reloadUntil=0;
        Refresh();return true;
    }
    void EnsureVisual()
    {
        if(!player||!player.animator)return;
        if(!visual||visual.gameObject!=player.animator.gameObject)
        {
            visual=player.animator.GetComponent<RifleAvatar>()??player.animator.gameObject.AddComponent<RifleAvatar>();
            visual.Configure(player.animator,player.transform);
        }
    }
    void Update()
    {
        if(!Equipped)return;
        EnsureVisual();if(overlay==null&&GameHud.Instance)BuildInterface();
        if(player.Health<=0||player.IsCuffed||player.InPoliceCar||PoliceVehicleController.BlocksPlayer(player)||
            MissionVehicle.Driver==player||(ShootingRange.Instance&&ShootingRange.Instance.IsActive))
        {SetEquipped(false);return;}
        bool blocked=Blocked;
        if(blocked)heldFire=false;
        bool mouseAim=!MobileControls.HasTouches&&Input.GetMouseButton(1)&&
            (!GameHud.Instance||!GameHud.Instance.BlocksPointer(Input.mousePosition));
        SetAiming(!blocked&&(mouseAim||mobileAim||heldFire||Time.unscaledTime-nextShot<.12f));
        if(!blocked)
        {
            if(Input.GetKeyDown(KeyCode.R))Reload();
            bool mouseFire=!MobileControls.HasTouches&&Input.GetMouseButton(0)&&
                (!GameHud.Instance||!GameHud.Instance.BlocksPointer(Input.mousePosition));
            if(heldFire||mouseFire||Input.GetKeyDown(KeyCode.F))RequestFire();
        }
        if(reloadUntil>0&&!Reloading){Ammo=30;reloadUntil=0;}
        visual?.Apply(Equipped,Aiming,ShotSequence,AimPitch,player.CurrentSpeed);
        Refresh();
    }
    void SetAiming(bool value)
    {
        if(Aiming==value)return;Aiming=value;
        if(!orbit)orbit=FindAnyObjectByType<ThirdPersonCamera>();
        if(value)orbit?.BeginWeaponAim();else orbit?.EndAim();
    }
    public void RequestFire()
    {
        if(!Equipped||Blocked||Reloading||Time.unscaledTime<nextShot)return;
        if(Ammo<=0){Reload();return;}
        SetAiming(true);Ammo--;ShotSequence++;nextShot=Time.unscaledTime+.14f;
        visual?.PlayShot(ShotSequence);
        if(audioSource&&shotSound)audioSource.PlayOneShot(shotSound,.3f);
        var camera=Camera.main;if(!camera)return;
        Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f));
        Vector3 end=ray.origin+ray.direction*160;
        if(Physics.Raycast(ray,out var hit,160,~(1<<2),QueryTriggerInteraction.Ignore))
        {
            end=hit.point;
            // The practice target can react locally. Player damage is never client-authored.
            hit.collider.GetComponentInParent<RangeTarget>()?.RegisterHit();
        }
        Vector3 from=visual&&visual.Muzzle?visual.Muzzle.position:player.transform.position+Vector3.up*1.4f;
        // A nearby wall at the muzzle must stop the tracer even if the camera sees past it.
        if(Physics.Linecast(from,end,out var obstruction,~(1<<2),QueryTriggerInteraction.Ignore))end=obstruction.point;
        var world=MultiplayerWorld.Instance;
        Vector3 direction=(end-from).normalized;
        if(world&&world.Connected&&world.RifleDamageAvailable)
        {
            string targetUid=world.RaycastRifleTarget(new Ray(from,direction),Mathf.Min(160,Vector3.Distance(from,end)));
            world.FireRifle(System.Guid.NewGuid().ToString(),from,direction,targetUid);
        }
        var go=new GameObject("Disparo do rifle");go.layer=2;
        var line=go.AddComponent<LineRenderer>();line.positionCount=2;line.SetPosition(0,from);line.SetPosition(1,end);
        line.startWidth=.018f;line.endWidth=.003f;line.sharedMaterial=TracerMaterial;
        line.startColor=new Color(1,.85f,.45f,.95f);line.endColor=new Color(1,.65f,.25f,.15f);Destroy(go,.055f);
        Refresh();
    }
    static Material tracerMaterial;
    static Material TracerMaterial=>tracerMaterial?tracerMaterial:(tracerMaterial=new Material(Shader.Find("Sprites/Default")));
    public void Reload(){if(Equipped&&!Blocked&&!Reloading&&Ammo<30){reloadUntil=Time.unscaledTime+1.5f;heldFire=false;Refresh();}}
    void BuildInterface()
    {
        overlay=new VisualElement{name="rifle-controls",pickingMode=PickingMode.Ignore};
        overlay.style.position=Position.Absolute;overlay.style.left=overlay.style.right=overlay.style.top=overlay.style.bottom=0;
        GameHud.Instance.Root.Add(overlay);
        cross=new Label("+"){pickingMode=PickingMode.Ignore};cross.style.position=Position.Absolute;
        cross.style.left=Length.Percent(50);cross.style.top=Length.Percent(50);cross.style.width=24;cross.style.height=30;
        cross.style.marginLeft=-12;cross.style.marginTop=-15;cross.style.fontSize=26;cross.style.color=Color.white;
        cross.style.unityTextAlign=TextAnchor.MiddleCenter;cross.style.unityTextOutlineWidth=1;cross.style.unityTextOutlineColor=Color.black;overlay.Add(cross);
        fireButton=new VisualElement{tooltip="Atirar · mouse esquerdo / F",name="rifle-fire"};
        SkinControl(fireButton,"target",24,226,78);overlay.Add(fireButton);
        fireButton.RegisterCallback<PointerDownEvent>(e=>{if(firePointer>=0||e.button!=0)return;firePointer=e.pointerId;heldFire=true;fireButton.CapturePointer(e.pointerId);RequestFire();e.StopPropagation();});
        fireButton.RegisterCallback<PointerUpEvent>(e=>{if(e.pointerId!=firePointer)return;heldFire=false;firePointer=-1;fireButton.ReleasePointer(e.pointerId);e.StopPropagation();});
        fireButton.RegisterCallback<PointerCaptureOutEvent>(_=>{heldFire=false;firePointer=-1;});
        fireButton.RegisterCallback<PointerCancelEvent>(_=>{heldFire=false;firePointer=-1;});
        aimButton=Button("target","Mirar · mouse direito",110,226,60);aimButton.clicked+=()=>mobileAim=!mobileAim;overlay.Add(aimButton);
        reloadButton=Button("bag","Recarregar · R",182,226,48);reloadButton.Clear();reloadButton.text="R";
        reloadButton.style.color=Color.white;reloadButton.style.fontSize=20;reloadButton.clicked+=Reload;overlay.Add(reloadButton);
        counter=new Label{pickingMode=PickingMode.Ignore};counter.style.position=Position.Absolute;counter.style.right=24;counter.style.bottom=312;
        counter.style.color=Color.white;counter.style.fontSize=15;HudPack.Skin(counter,8);counter.style.paddingLeft=counter.style.paddingRight=10;
        counter.style.paddingTop=counter.style.paddingBottom=6;overlay.Add(counter);
    }
    Button Button(string icon,string tooltip,float right,float bottom,float size)
    {
        var button=new Button{tooltip=tooltip};SkinControl(button,icon,right,bottom,size);return button;
    }
    void SkinControl(VisualElement button,string icon,float right,float bottom,float size)
    {
        button.style.position=Position.Absolute;button.style.right=right;button.style.bottom=bottom;
        button.style.width=button.style.height=size;HudPack.Skin(button,size/2);button.style.alignItems=Align.Center;button.style.justifyContent=Justify.Center;
        var image=HudPack.Image(icon);image.style.width=image.style.height=size*.46f;button.Add(image);
        GameHud.Instance.RegisterPointerBlocker(button);
    }
    void Refresh()
    {
        if(overlay==null&&Equipped&&GameHud.Instance)BuildInterface();
        var punch=GameHud.Instance?GameHud.Instance.Root?.Q("action-list"):null;
        if(punch!=null)punch.style.display=Equipped?new StyleEnum<DisplayStyle>(DisplayStyle.None):new StyleEnum<DisplayStyle>(StyleKeyword.Null);
        bool show=Equipped&&!Blocked;
        if(overlay!=null)overlay.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        // Explicit child display also releases the HUD pointer blockers while hidden.
        foreach(var control in new VisualElement[]{fireButton,aimButton,reloadButton})
            if(control!=null)control.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        if(cross!=null)cross.style.display=Aiming?DisplayStyle.Flex:DisplayStyle.None;
        if(counter!=null)counter.text=Reloading?"RECARREGANDO":"RIFLE  ·  "+Ammo.ToString("D2")+" / 30";
    }
    void CreateSound()
    {
        audioSource=gameObject.AddComponent<AudioSource>();audioSource.spatialBlend=.5f;audioSource.minDistance=4;audioSource.maxDistance=65;
        const int rate=22050;var samples=new float[2400];var random=new System.Random(71);
        for(int i=0;i<samples.Length;i++)samples[i]=((float)random.NextDouble()*2-1)*Mathf.Exp(-i/380f)*.8f;
        shotSound=AudioClip.Create("Rifle · disparo",samples.Length,1,rate,false);shotSound.SetData(samples,0);
    }
    void OnApplicationFocus(bool focused){if(!focused){heldFire=mobileAim=false;SetAiming(false);}}
    void OnDisable(){heldFire=mobileAim=false;SetAiming(false);Equipped=false;visual?.Apply(false,false,ShotSequence,0,0);Refresh();}
    void OnDestroy(){if(Instance==this)Instance=null;if(overlay!=null)overlay.RemoveFromHierarchy();if(shotSound)Destroy(shotSound);}
}
