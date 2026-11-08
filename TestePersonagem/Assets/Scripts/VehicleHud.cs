using UnityEngine;
using UnityEngine.UIElements;

// Vehicle-only controls. Item actions stay unavailable until inventory grants them.
public sealed class VehicleHud : MonoBehaviour
{
    public static bool MenuOpen {get;private set;}
    public static bool Driving=>PoliceVehicleController.Driving||MissionVehicle.Driver;
    public static bool PedalsHeld=>accelerating||braking;
    public static bool Braking=>braking;
    public static float Throttle=>braking?-1:accelerating?1:0;
    public static bool SteeringHeld=>steeringLeft||steeringRight;
    public static float Steering=>steeringLeft==steeringRight?0:steeringLeft?-1:1;
    static bool accelerating,braking,steeringLeft,steeringRight;
    PoliceVehicleController car;
    MissionVehicle loan;
    static AudioClip hornClip;
    VisualElement controls,modal;
    Label speed,status;
    bool shown;
    public void Initialize(PoliceVehicleController owner){car=owner;}
    public void Initialize(MissionVehicle owner){loan=owner;}
    void Honk()
    {
        if(!shown||MenuOpen)return;
        if(!hornClip)
        {
            const int rate=16000;
            var samples=new float[rate/3];
            for(int i=0;i<samples.Length;i++)
            {
                float fade=Mathf.Min(1f,i/(rate*.015f),(samples.Length-i)/(rate*.04f));
                samples[i]=(Mathf.Sin(2*Mathf.PI*420*i/rate)+.35f*Mathf.Sin(2*Mathf.PI*630*i/rate))*fade*.25f;
            }
            hornClip=AudioClip.Create("Buzina do veículo",samples.Length,1,rate,false);
            hornClip.SetData(samples,0);
        }
        AudioSource.PlayClipAtPoint(hornClip,transform.position,.7f);
    }
    float Speed=>car?car.Speed:loan?loan.Speed:0;
    string VehicleName=>car?"Viatura policial · "+car.Id:"Carro emprestado";
    static Image RedutoIcon(string icon)
    {
        return new Image{image=Resources.Load<Texture2D>("RedutoVehicleHud/"+icon),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
    }
    VisualElement Pedal(string icon,float right,float bottom,System.Action<bool> held,bool onLeft=false)
    {
        var button=new VisualElement{tooltip=icon=="accelerate"?"Segure para acelerar":icon=="brake"?"Segure para frear":icon=="left"?"Virar à esquerda":"Virar à direita"};
        button.style.alignItems=Align.Center;button.style.justifyContent=Justify.Center;
        var artwork=RedutoIcon(icon);artwork.name="vehicle-"+icon+"-icon";artwork.style.width=56;artwork.style.height=56;button.Add(artwork);
        button.style.position=Position.Absolute;
        if(onLeft)button.style.left=right;else button.style.right=right;
        button.style.bottom=bottom;button.style.width=104;button.style.height=104;
        button.style.fontSize=17;button.style.color=Color.white;
        HudPack.Skin(button,52);
        button.RegisterCallback<PointerDownEvent>(e=>{held(true);button.CapturePointer(e.pointerId);button.style.backgroundColor=new Color(.1f,.66f,.49f,.9f);e.StopPropagation();});
        button.RegisterCallback<PointerUpEvent>(e=>{held(false);button.ReleasePointer(e.pointerId);HudPack.Skin(button,46);e.StopPropagation();});
        button.RegisterCallback<PointerCancelEvent>(e=>{held(false);HudPack.Skin(button,46);});
        button.RegisterCallback<PointerCaptureOutEvent>(e=>{held(false);HudPack.Skin(button,46);});
        controls.Add(button);GameHud.Instance.RegisterPointerBlocker(button);return button;
    }
    void Build()
    {
        var hud=GameHud.Instance;controls=new VisualElement{name="vehicle-controls"};
        controls.style.position=Position.Absolute;controls.style.left=controls.style.right=controls.style.top=controls.style.bottom=0;controls.pickingMode=PickingMode.Ignore;
        hud.Root.Add(controls);
        Pedal("accelerate",24,28,v=>accelerating=v).name="vehicle-accelerate";
        Pedal("brake",140,28,v=>braking=v).name="vehicle-brake";
        Pedal("left",24,28,v=>steeringLeft=v,true).name="vehicle-left";
        Pedal("right",140,28,v=>steeringRight=v,true).name="vehicle-right";
        var horn=new Button(Honk){name="vehicle-horn",tooltip="Buzinar"};
        horn.style.position=Position.Absolute;horn.style.right=96;horn.style.bottom=150;horn.style.width=60;horn.style.height=60;
        horn.style.alignItems=Align.Center;horn.style.justifyContent=Justify.Center;HudPack.Skin(horn,28);
        var hornIcon=RedutoIcon("horn");hornIcon.style.width=34;hornIcon.style.height=34;horn.Add(hornIcon);
        controls.Add(horn);hud.RegisterPointerBlocker(horn);
        var gear=new Button(ToggleMenu){name="vehicle-gear",tooltip="Opções do veículo"};
        gear.style.position=Position.Absolute;gear.style.right=164;gear.style.bottom=150;gear.style.width=60;gear.style.height=60;
        gear.style.alignItems=Align.Center;gear.style.justifyContent=Justify.Center;
        HudPack.Skin(gear,28);var gearImage=HudPack.Image("settings");gearImage.name="vehicle-gear-icon";gearImage.style.width=27;gearImage.style.height=27;gear.Add(gearImage);
        controls.Add(gear);hud.RegisterPointerBlocker(gear);
        var exit=new Button(()=>{if(car)car.RequestExit();else loan?.Exit();}){name="vehicle-exit",text="SAIR",tooltip="Sair do veículo"};
        exit.style.position=Position.Absolute;exit.style.right=24;exit.style.bottom=150;exit.style.width=62;exit.style.height=60;
        exit.style.color=Color.white;exit.style.unityFontStyleAndWeight=FontStyle.Bold;HudPack.Skin(exit,18);
        controls.Add(exit);hud.RegisterPointerBlocker(exit);
        speed=new Label{name="vehicle-speed"};speed.style.position=Position.Absolute;speed.style.right=242;speed.style.bottom=65;speed.style.color=Color.white;speed.style.fontSize=20;controls.Add(speed);
        modal=new VisualElement{name="vehicle-options"};modal.style.position=Position.Absolute;modal.style.right=24;modal.style.bottom=240;modal.style.width=330;modal.style.paddingTop=modal.style.paddingBottom=modal.style.paddingLeft=modal.style.paddingRight=20;
        HudPack.Skin(modal,18);modal.style.color=Color.white;
        var header=new VisualElement{name="vehicle-options-header"};header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;modal.Add(header);
        var title=new Label("VEÍCULO"){name="vehicle-options-title"};title.style.flexGrow=1;header.Add(title);
        var close=new Button(CloseMenu){name="vehicle-options-close",tooltip="Fechar opções do veículo"};HudPack.Skin(close,12);close.style.width=32;close.style.height=32;close.style.alignItems=Align.Center;close.style.justifyContent=Justify.Center;
        var closeIcon=HudPack.Image("close");closeIcon.style.width=18;closeIcon.style.height=18;close.Add(closeIcon);header.Add(close);
        status=new Label{name="vehicle-options-status"};status.style.whiteSpace=WhiteSpace.Normal;status.style.color=Color.white;status.style.marginTop=12;status.style.marginBottom=12;modal.Add(status);
        var fuel=new Button{name="vehicle-fuel",text="Abastecer · requer galão de gasolina",tooltip="Indisponível neste teste"};HudPack.Skin(fuel,10);fuel.SetEnabled(false);modal.Add(fuel);
        var repair=new Button{name="vehicle-repair",text="Reparar · requer kit de reparo",tooltip="Indisponível neste teste"};HudPack.Skin(repair,10);repair.SetEnabled(false);modal.Add(repair);
        var info=new Button(()=>status.text=VehicleName+"\nVelocidade: "+Mathf.Abs(Speed*3.6f).ToString("0")+" km/h\nCombustível: indisponível neste teste.\nIntegridade: indisponível neste teste."){name="vehicle-information",text="Ver status / informações"};HudPack.Skin(info,10);modal.Add(info);
        hud.Root.Add(modal);hud.RegisterPointerBlocker(modal);CloseMenu();
    }
    void ToggleMenu(){if(MenuOpen)CloseMenu();else{accelerating=braking=steeringLeft=steeringRight=false;MenuOpen=true;modal.style.display=DisplayStyle.Flex;status.text=VehicleName+"\nPare o veículo para realizar manutenção.";}}
    void CloseMenu(){MenuOpen=false;if(modal!=null)modal.style.display=DisplayStyle.None;}
    void FootControls(bool visible)
    {
        foreach(var name in new[]{"action-list","action-sprint","action-jump"})
        {var element=GameHud.Instance?.Root.Q<VisualElement>(name);if(element!=null)element.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;}
        var radio=GameHud.Instance?.Root.Q<VisualElement>("radio-push-to-talk");
        if(radio!=null)radio.style.bottom=visible?132:152;
    }
    void Update()
    {
        bool active=car&&PoliceVehicleController.Active==car&&car.Occupied||loan&&loan.Occupied&&MissionVehicle.Driver;
        if(!active&&!shown)return;
        if(!GameHud.Instance)return;
        if(controls==null)Build();
        if(active!=shown){shown=active;FootControls(!active);controls.style.display=active?DisplayStyle.Flex:DisplayStyle.None;if(!active){accelerating=braking=steeringLeft=steeringRight=false;CloseMenu();}}
        if(active){speed.text=Mathf.Abs(Speed*3.6f).ToString("0")+" km/h";if(MenuOpen&&Input.GetKeyDown(KeyCode.Escape))CloseMenu();}
    }
    void OnApplicationFocus(bool focus){if(!focus)accelerating=braking=steeringLeft=steeringRight=false;}
    public void ReleaseControls()
    {
        if(shown)FootControls(true);
        shown=false;accelerating=braking=steeringLeft=steeringRight=false;CloseMenu();
        if(controls!=null)controls.style.display=DisplayStyle.None;
    }
    void OnDestroy(){if(shown)FootControls(true);if(shown)CloseMenu();accelerating=braking=steeringLeft=steeringRight=false;controls?.RemoveFromHierarchy();modal?.RemoveFromHierarchy();}
}
