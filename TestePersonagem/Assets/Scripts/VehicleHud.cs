using UnityEngine;
using UnityEngine.UIElements;

// Vehicle-only controls. Item actions stay unavailable until inventory grants them.
public sealed class VehicleHud : MonoBehaviour
{
    public static bool MenuOpen {get;private set;}
    public static bool PedalsHeld=>accelerating||braking;
    public static bool Braking=>braking;
    public static float Throttle=>braking?0:accelerating?1:0;
    static bool accelerating,braking;
    PoliceVehicleController car;
    VisualElement controls,modal;
    Label speed,status;
    bool shown;
    public void Initialize(PoliceVehicleController owner){car=owner;}
    VisualElement Pedal(string text,float right,System.Action<bool> held)
    {
        var button=new VisualElement();button.style.alignItems=Align.Center;button.style.justifyContent=Justify.Center;
        var label=new Label(text){pickingMode=PickingMode.Ignore};label.style.unityTextAlign=TextAnchor.MiddleCenter;button.Add(label);button.style.position=Position.Absolute;
        button.style.right=right;button.style.bottom=32;button.style.width=100;button.style.height=110;
        button.style.fontSize=17;button.style.color=Color.white;
        button.style.backgroundColor=new Color(.025f,.055f,.08f,.9f);
        button.style.borderTopLeftRadius=button.style.borderTopRightRadius=button.style.borderBottomLeftRadius=button.style.borderBottomRightRadius=20;
        button.RegisterCallback<PointerDownEvent>(e=>{held(true);button.CapturePointer(e.pointerId);button.style.backgroundColor=new Color(.08f,.5f,.28f,.95f);e.StopPropagation();});
        button.RegisterCallback<PointerUpEvent>(e=>{held(false);button.ReleasePointer(e.pointerId);button.style.backgroundColor=new Color(.025f,.055f,.08f,.9f);e.StopPropagation();});
        button.RegisterCallback<PointerCancelEvent>(e=>held(false));
        button.RegisterCallback<PointerCaptureOutEvent>(e=>held(false));
        controls.Add(button);GameHud.Instance.RegisterPointerBlocker(button);return button;
    }
    void Build()
    {
        var hud=GameHud.Instance;controls=new VisualElement{name="vehicle-controls"};
        controls.style.position=Position.Absolute;controls.style.left=controls.style.right=controls.style.top=controls.style.bottom=0;controls.pickingMode=PickingMode.Ignore;
        hud.Root.Add(controls);
        Pedal("▲\nACELERAR",24,v=>accelerating=v).name="vehicle-accelerate";Pedal("■\nFREAR",140,v=>braking=v).name="vehicle-brake";
        var gear=new Button(ToggleMenu){name="vehicle-gear",tooltip="Opções do veículo"};
        gear.style.position=Position.Absolute;gear.style.right=28;gear.style.bottom=160;gear.style.width=64;gear.style.height=64;gear.style.fontSize=30;
        gear.style.alignItems=Align.Center;gear.style.justifyContent=Justify.Center;var gearIcon=new HudIcon("settings");gearIcon.style.width=30;gearIcon.style.height=30;gear.Add(gearIcon);
        controls.Add(gear);hud.RegisterPointerBlocker(gear);
        speed=new Label();speed.style.position=Position.Absolute;speed.style.right=140;speed.style.bottom=175;speed.style.color=Color.white;speed.style.fontSize=22;controls.Add(speed);
        modal=new VisualElement{name="vehicle-options"};modal.style.position=Position.Absolute;modal.style.right=24;modal.style.bottom=240;modal.style.width=330;modal.style.paddingTop=modal.style.paddingBottom=modal.style.paddingLeft=modal.style.paddingRight=20;
        modal.style.backgroundColor=new Color(.025f,.04f,.06f,.98f);modal.style.borderTopLeftRadius=modal.style.borderTopRightRadius=modal.style.borderBottomLeftRadius=modal.style.borderBottomRightRadius=16;
        modal.style.color=Color.white;modal.Add(new Label("VEÍCULO"));
        var close=new Button(CloseMenu){text="FECHAR  ×"};modal.Add(close);
        status=new Label();status.style.whiteSpace=WhiteSpace.Normal;status.style.color=Color.white;status.style.marginTop=12;status.style.marginBottom=12;modal.Add(status);
        var fuel=new Button{text="Abastecer · requer galão de gasolina",tooltip="Indisponível neste teste"};fuel.SetEnabled(false);modal.Add(fuel);
        var repair=new Button{text="Reparar · requer kit de reparo",tooltip="Indisponível neste teste"};repair.SetEnabled(false);modal.Add(repair);
        modal.Add(new Button(()=>status.text="Viatura policial\nVelocidade: "+Mathf.Abs(car.Speed*3.6f).ToString("0")+" km/h\nCombustível: indisponível neste teste.\nIntegridade: indisponível neste teste."){text="Ver status / informações"});
        hud.Root.Add(modal);hud.RegisterPointerBlocker(modal);CloseMenu();
    }
    void ToggleMenu(){if(MenuOpen)CloseMenu();else{accelerating=braking=false;MenuOpen=true;modal.style.display=DisplayStyle.Flex;status.text="Viatura policial · "+car.Id+"\nPare o veículo para realizar manutenção.";}}
    void CloseMenu(){MenuOpen=false;if(modal!=null)modal.style.display=DisplayStyle.None;}
    void FootControls(bool visible)
    {
        foreach(var name in new[]{"action-list","action-sprint","action-jump"})
        {var element=GameHud.Instance?.Root.Q<VisualElement>(name);if(element!=null)element.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;}
        var radio=GameHud.Instance?.Root.Q<VisualElement>("radio-push-to-talk");
        if(radio!=null)radio.style.bottom=visible?126:160;
    }
    void Update()
    {
        bool active=car&&PoliceVehicleController.Active==car&&car.Occupied;
        if(!active&&!shown)return;
        if(!GameHud.Instance)return;
        if(controls==null)Build();
        if(active!=shown){shown=active;FootControls(!active);controls.style.display=active?DisplayStyle.Flex:DisplayStyle.None;if(!active){accelerating=braking=false;CloseMenu();}}
        if(active){speed.text=Mathf.Abs(car.Speed*3.6f).ToString("0")+" km/h";if(MenuOpen&&Input.GetKeyDown(KeyCode.Escape))CloseMenu();}
    }
    void OnApplicationFocus(bool focus){if(!focus)accelerating=braking=false;}
    public void ReleaseControls()
    {
        if(shown)FootControls(true);
        shown=false;accelerating=braking=false;CloseMenu();
        if(controls!=null)controls.style.display=DisplayStyle.None;
    }
    void OnDestroy(){if(shown)FootControls(true);if(shown)CloseMenu();accelerating=braking=false;controls?.RemoveFromHierarchy();modal?.RemoveFromHierarchy();}
}
