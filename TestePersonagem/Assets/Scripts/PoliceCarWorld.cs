using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Server-owned police cars with visible animated driver/passenger seats.
public sealed class PoliceCarWorld : MonoBehaviour
{
    public static PoliceCarWorld Instance {get;private set;}
    sealed class CarVisual
    {
        public GameObject root;
        public PoliceCarRig rig;
        public PoliceSeatAvatar driver,passenger;
        public PoliceVehicleController controls;
        public string ownerUid,lastPassengerUid,lastDriverUid;
        public Vector3 target;
        public float yaw;
        public TextMesh label;
    }
    readonly Dictionary<string,CarVisual> visuals=new Dictionary<string,CarVisual>();
    ThirdPersonPlayer player;
    Button interaction;
    Image interactionIcon;
    Label interactionCaption;
    PoliceVehicleController nearest;
    public void Initialize(ThirdPersonPlayer owner){player=owner;Instance=this;}
    public void Apply(PoliceCarState[] cars,string selfCarId=null,Vector3? selfPosition=null,int revision=-1)
    {
        if(revision>=0&&revision!=PoliceVehicleController.StateRevision)return;
        var current=new HashSet<string>();
        if(cars!=null)foreach(var car in cars)
        {
            if(string.IsNullOrEmpty(car.id))continue;
            current.Add(car.id);
            if(!visuals.TryGetValue(car.id,out var visual))
            {visual=Build(car.id,new Vector3(car.x,car.y,car.z),car.yaw);visuals.Add(car.id,visual);}
            visual.ownerUid=car.ownerUid;
            visual.target=Ground(new Vector3(car.x,car.y,car.z));visual.yaw=car.yaw;
            visual.controls.ApplyState(car,player,selfCarId,selfPosition);
            bool driverExiting=string.IsNullOrEmpty(car.driverUid);
            string driverUid=driverExiting?visual.lastDriverUid:car.driverUid;
            if(!string.IsNullOrEmpty(driverUid))visual.driver.UseVisual(
                MultiplayerWorld.Instance?MultiplayerWorld.Instance.AnimatorForUid(driverUid):player.animator);
            Vector3 driverStart=PlayerPosition(driverUid,driverExiting
                ?Ground(visual.root.transform.position-visual.root.transform.right*2.1f)
                :visual.root.transform.TransformPoint(visual.rig.DriverDoor));
            if(!string.IsNullOrEmpty(car.driverUid))visual.lastDriverUid=car.driverUid;
            float driverYaw=driverExiting?car.yaw+(Vector3.Dot(driverStart-visual.root.transform.position,visual.root.transform.right)>0?90:-90):PlayerYaw(driverUid,car.yaw);
            if(!visual.controls.Occupied)visual.driver.SetOccupant(car.driverUid,driverStart,startYaw:driverYaw);
            bool passengerExiting=string.IsNullOrEmpty(car.occupantUid);
            string passengerUid=passengerExiting?visual.lastPassengerUid:car.occupantUid;
            if(!string.IsNullOrEmpty(passengerUid))visual.passenger.UseVisual(
                MultiplayerWorld.Instance?MultiplayerWorld.Instance.AnimatorForUid(passengerUid):player.animator);
            // The reliable peer pose has already moved to the car's center. Start
            // boarding where the actor is still visibly standing instead.
            Vector3 passengerPoint=passengerExiting
                ?PlayerPosition(passengerUid,Ground(visual.root.transform.position+visual.root.transform.right*2.1f),selfPosition)
                :VisiblePlayerPosition(passengerUid,visual.root.transform.TransformPoint(visual.rig.RearDoor));
            float passengerYaw=passengerExiting?float.NaN:VisiblePlayerYaw(passengerUid,car.yaw);
            if(!passengerExiting)visual.lastPassengerUid=car.occupantUid;
            visual.passenger.SetOccupant(car.occupantUid,passengerPoint,startYaw:passengerYaw);
            bool occupied=!string.IsNullOrEmpty(car.occupantUid);
            visual.label.gameObject.SetActive(occupied);
            visual.label.text=occupied?"DETIDO  ID "+car.occupantUid+"  ·  "+car.occupantName:"";
        }
        var removed=new List<string>();foreach(var id in visuals.Keys)if(!current.Contains(id))removed.Add(id);
        foreach(var id in removed){Destroy(visuals[id].root);visuals.Remove(id);}
    }
    Vector3 PlayerPosition(string uid,Vector3 fallback,Vector3? selfPosition=null)
    {
        if(string.IsNullOrEmpty(uid))return fallback;
        if(AccountSession.HasCharacter&&AccountSession.Instance.State.uid==uid)return selfPosition??player.transform.position;
        if(MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemotePoseByUid(uid,out var position,out _))return position;
        return fallback;
    }
    Vector3 VisiblePlayerPosition(string uid,Vector3 fallback)
    {
        if(string.IsNullOrEmpty(uid))return fallback;
        if(AccountSession.HasCharacter&&AccountSession.Instance.State.uid==uid)return player.transform.position;
        if(MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemoteByUid(uid,out var actor)&&actor)return actor.position;
        return fallback;
    }
    float VisiblePlayerYaw(string uid,float fallback)
    {
        if(string.IsNullOrEmpty(uid))return fallback;
        if(AccountSession.HasCharacter&&AccountSession.Instance.State.uid==uid)return player.transform.eulerAngles.y;
        if(MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemoteByUid(uid,out var actor)&&actor)return actor.eulerAngles.y;
        return fallback;
    }
    float PlayerYaw(string uid,float fallback)
    {
        if(string.IsNullOrEmpty(uid))return fallback;
        if(AccountSession.HasCharacter&&AccountSession.Instance.State.uid==uid)return player.transform.eulerAngles.y;
        if(MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemotePoseByUid(uid,out _,out var yaw))return yaw;
        return fallback;
    }
    static Vector3 Ground(Vector3 position)
    {
        if(Physics.Raycast(position+Vector3.up*3,Vector3.down,out var hit,8,1,QueryTriggerInteraction.Ignore))position.y=hit.point.y;
        return position;
    }
    CarVisual Build(string id,Vector3 position,float yaw)
    {
        var root=new GameObject("Viatura policial "+id);root.layer=2;
        root.transform.SetPositionAndRotation(Ground(position),Quaternion.Euler(0,yaw,0));
        var rig=PoliceCarRig.Create(root.transform,Resources.Load<GameObject>("Vehicles/Police/PoliceCar"));
        var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.82f,0);collider.size=new Vector3(2.05f,1.65f,4.5f);
        var driver=new GameObject("Banco do motorista").AddComponent<PoliceSeatAvatar>();driver.transform.SetParent(root.transform,false);driver.Initialize(rig,player.animator,true);
        var passenger=new GameObject("Banco traseiro").AddComponent<PoliceSeatAvatar>();passenger.transform.SetParent(root.transform,false);passenger.Initialize(rig,player.animator,false);
        var controls=root.AddComponent<PoliceVehicleController>();controls.Initialize(id,rig,driver);
        var badge=new GameObject("Ocupante",typeof(TextMesh));badge.layer=2;badge.transform.SetParent(root.transform,false);
        badge.transform.localPosition=new Vector3(0,2.15f,0);
        var text=badge.GetComponent<TextMesh>();text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
        text.fontSize=36;text.characterSize=.025f;text.color=new Color(1f,.78f,.28f);text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        badge.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;badge.SetActive(false);
        return new CarVisual{root=root,rig=rig,driver=driver,passenger=passenger,controls=controls,label=text,target=root.transform.position,yaw=yaw};
    }
    public bool PassengerExiting(string uid)
    {foreach(var v in visuals.Values)if(v.lastPassengerUid==uid&&v.passenger.Exiting)return true;return false;}
    public bool HasSeatedVisual(string uid)
    {
        foreach(var v in visuals.Values)
            if(v.driver.Uid==uid||v.passenger.Uid==uid||v.lastDriverUid==uid&&v.driver.Exiting||v.lastPassengerUid==uid&&v.passenger.Exiting)return true;
        return false;
    }
    public PoliceVehicleController CreatePreview(Vector3 position)
    {
        var visual=Build("previa-viatura",position,0);visual.controls.LocalPreview=true;
        visuals.Add("previa-viatura",visual);return visual.controls;
    }
    void Update()
    {
        if(!player)return;
        foreach(var v in visuals.Values)
        {
            if(v.controls.Occupied)
            {
                if(v.controls.LocalPreview){v.target=v.root.transform.position;v.yaw=v.root.transform.eulerAngles.y;}
                continue;
            }
            var before=v.root.transform.position;
            v.root.transform.position=Vector3.Lerp(before,v.target,1-Mathf.Exp(-12*Time.deltaTime));
            v.root.transform.rotation=Quaternion.Slerp(v.root.transform.rotation,Quaternion.Euler(0,v.yaw,0),1-Mathf.Exp(-12*Time.deltaTime));
            float distance=Vector3.ProjectOnPlane(v.root.transform.position-before,Vector3.up).magnitude;
            v.rig.RotateWheels(distance,0);
        }
        var hud=GameHud.Instance;if(!hud)return;
        if(interaction==null)
        {
            interaction=new Button(()=>Interact()){name="police-car-interact"};
            interaction.style.position=Position.Absolute;interaction.style.right=240;interaction.style.bottom=126;
            interaction.style.width=70;interaction.style.height=70;HudPack.Skin(interaction,35);
            interaction.style.flexDirection=FlexDirection.Column;interaction.style.alignItems=Align.Center;interaction.style.justifyContent=Justify.Center;
            interactionIcon=HudPack.Image("door");interactionIcon.name="police-car-interact-icon";interactionIcon.style.width=28;interactionIcon.style.height=28;interaction.Add(interactionIcon);
            interactionCaption=new Label("E ENTRAR"){name="police-car-interact-label",pickingMode=PickingMode.Ignore};interactionCaption.style.fontSize=10;interactionCaption.style.color=Color.white;interactionCaption.style.marginTop=3;interactionCaption.style.unityFontStyleAndWeight=FontStyle.Bold;
            interactionCaption.style.textShadow=new TextShadow{offset=new Vector2(0,1),blurRadius=2,color=new Color(0,0,0,.95f)};interaction.Add(interactionCaption);
            hud.Root.Add(interaction);hud.RegisterPointerBlocker(interaction);
        }
        nearest=null;float best=.85f;
        foreach(var v in visuals.Values)
        {
            // Keep the interaction visible while waiting for server capability sync.
            bool allowed=v.controls.LocalPreview||AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty&&v.ownerUid==AccountSession.Instance.State.uid;
            float distance=v.controls.EntryDistance(player);
            if(allowed&&distance<best){best=distance;nearest=v.controls;}
        }
        bool active=PoliceVehicleController.Active;
        interactionIcon.image=HudPack.Texture(active?"exit":"door");
        interactionCaption.text=active?"F SAIR":"E ENTRAR";
        bool show=(active||nearest)&&!hud.IsPanelOpen&&!InGameChat.Editing&&!StartupLoader.IsLoading;
        interaction.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        interaction.SetEnabled(active?!PoliceVehicleController.Active.Busy:!nearest||!nearest.Busy);
        interaction.tooltip=active?"Sair da viatura [F]":nearest&&!nearest.LocalPreview&&(!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.PoliceVehicleAvailable)
            ?"Aguardando sincronização da viatura com o servidor":"Entrar na viatura [E]";
        if(show&&!active&&Input.GetKeyDown(KeyCode.E))Interact();
    }
    void Interact(){if(PoliceVehicleController.Active)PoliceVehicleController.Active.RequestExit();else nearest?.RequestEnter(player);}
    void LateUpdate(){if(Camera.main)foreach(var v in visuals.Values)v.label.transform.rotation=Camera.main.transform.rotation;}
    public void Clear(){Apply(System.Array.Empty<PoliceCarState>());}
    void OnDestroy(){Clear();if(interaction!=null)interaction.RemoveFromHierarchy();if(Instance==this)Instance=null;}
}
