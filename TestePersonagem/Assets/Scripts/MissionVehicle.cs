using UnityEngine;
using System.Linq;

// A mission loan. Never creates a garage entry or permanent player vehicle.
public sealed class MissionVehicle : MonoBehaviour
{
    public static ThirdPersonPlayer Driver {get;private set;}
    public float Speed {get;private set;}
    public bool Occupied=>driver;
    internal static Vector2? VerificationInput;
    public static float DriverSpeed=>Driver&&TutorialJourney.Instance&&TutorialJourney.Instance.LoanCar?Mathf.Abs(TutorialJourney.Instance.LoanCar.Speed):0;
    ThirdPersonPlayer driver;
    CharacterController controller;
    ThirdPersonCamera camera;
    Transform oldTarget;
    float oldDistance,oldPitch,vertical;
    bool oldCombat;
    Transform[] wheels;
    public static MissionVehicle Create(Vector3 position)
    {
        var root=new GameObject("Carro pessoal do NPC · empréstimo temporário");root.layer=2;
        root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,90,0));
        var loan=root.AddComponent<MissionVehicle>();loan.controller=root.AddComponent<CharacterController>();
        loan.controller.height=1.55f;loan.controller.center=new Vector3(0,.80f,0);loan.controller.radius=.83f;loan.controller.stepOffset=.30f;loan.controller.slopeLimit=40;
        loan.BuildVisual();return loan;
    }
    public static GameObject CreateRemoteVisual()
    {
        var root=new GameObject("Carro emprestado remoto");root.layer=2;var visual=root.AddComponent<MissionVehicle>();visual.enabled=false;visual.BuildVisual();return root;
    }
    Material Material(Color color){return new Material(Shader.Find("Standard")){color=color};}
    void BuildVisual()
    {
        var blue=Material(new Color(.08f,.22f,.37f));var black=Material(new Color(.035f,.045f,.055f));var metal=Material(new Color(.6f,.65f,.7f));
        Mesh mesh=Resources.Load<Mesh>("Arrival/CarBody");
        if(mesh)
        {
            var body=new GameObject("Carro emprestado");body.layer=2;body.transform.SetParent(transform,false);
            body.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=body.AddComponent<MeshRenderer>();renderer.sharedMaterials=System.Linq.Enumerable.Repeat(blue,Mathf.Max(1,mesh.subMeshCount)).ToArray();
            float scale=4.2f/Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.z);
            body.transform.localScale=Vector3.one*scale;
            Quaternion rotation=mesh.bounds.size.x>mesh.bounds.size.z?Quaternion.Euler(0,90,0):Quaternion.identity;
            body.transform.localRotation=rotation;body.transform.localPosition=-(rotation*mesh.bounds.center)*scale+Vector3.up*(mesh.bounds.size.y*scale*.5f+.22f);
            var details=Resources.Load<Mesh>("Arrival/CarDetails");
            if(details){var part=new GameObject("Interior e vidros");part.layer=2;part.transform.SetParent(transform,false);part.transform.localPosition=body.transform.localPosition;part.transform.localRotation=body.transform.localRotation;part.transform.localScale=body.transform.localScale;part.AddComponent<MeshFilter>().sharedMesh=details;part.AddComponent<MeshRenderer>().sharedMaterials=Enumerable.Repeat(black,Mathf.Max(1,details.subMeshCount)).ToArray();}
        }
        else
        {
            Part("Carroceria",new Vector3(0,.65f,0),new Vector3(1.75f,.55f,4.1f),blue);
            Part("Cabine",new Vector3(0,1.12f,-.15f),new Vector3(1.48f,.65f,1.95f),black);
        }
        wheels=new Transform[4];int index=0;
        foreach(float x in new[]{-.86f,.86f})foreach(float z in new[]{-1.3f,1.3f})
        {
            var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);wheel.name="Roda";wheel.layer=2;Destroy(wheel.GetComponent<Collider>());
            wheel.transform.SetParent(transform,false);wheel.transform.localPosition=new Vector3(x,.35f,z);wheel.transform.localRotation=Quaternion.Euler(0,0,90);wheel.transform.localScale=new Vector3(.65f,.13f,.65f);wheel.GetComponent<Renderer>().sharedMaterial=black;wheels[index++]=wheel.transform;
            var rim=GameObject.CreatePrimitive(PrimitiveType.Cylinder);rim.name="Aro";rim.layer=2;Destroy(rim.GetComponent<Collider>());rim.transform.SetParent(transform,false);rim.transform.localPosition=new Vector3(Mathf.Sign(x)*1.005f,.35f,z);rim.transform.localRotation=Quaternion.Euler(0,0,90);rim.transform.localScale=new Vector3(.35f,.025f,.35f);rim.GetComponent<Renderer>().sharedMaterial=metal;
        }
        Part("Farol esquerdo",new Vector3(-.55f,.65f,2.08f),new Vector3(.35f,.15f,.06f),metal);
        Part("Farol direito",new Vector3(.55f,.65f,2.08f),new Vector3(.35f,.15f,.06f),metal);
    }
    void Part(string name,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.layer=2;Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(transform,false);part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=material;
    }
    public bool Enter(ThirdPersonPlayer player)
    {
        if(driver||Driver||!player||player.IsJumping||Vector3.Distance(player.transform.position,transform.position)>4)return false;
        driver=player;Driver=player;oldCombat=player.CombatLocked;player.CombatLocked=true;
        player.GetComponent<CharacterController>().enabled=false;player.animator.gameObject.SetActive(false);
        camera=UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>();
        if(camera){oldTarget=camera.target;oldDistance=camera.distance;oldPitch=camera.pitch;camera.target=transform;camera.distance=6;camera.pitch=18;camera.yaw=transform.eulerAngles.y;}
        return true;
    }
    public bool Exit(bool force=false)
    {
        if(!driver)return true;
        Vector3 point=transform.position-transform.right*2.1f;
        bool safe=false;
        foreach(var direction in new[]{-transform.right,transform.right,-transform.forward,transform.forward})
        {
            Vector3 candidate=transform.position+direction*2.2f;
            if(!Physics.Raycast(candidate+Vector3.up*2,Vector3.down,out var hit,5,1,QueryTriggerInteraction.Ignore))continue;
            candidate=hit.point+Vector3.up*.08f;
            if(Physics.CheckCapsule(candidate+Vector3.up*.4f,candidate+Vector3.up*1.5f,.35f,1,QueryTriggerInteraction.Ignore))continue;
            point=candidate;safe=true;break;
        }
        if(!safe&&!force)return false;
        var player=driver;driver=null;Driver=null;Speed=0;
        player.transform.position=point;player.animator.gameObject.SetActive(true);player.GetComponent<CharacterController>().enabled=true;player.CombatLocked=oldCombat;
        if(camera){camera.target=oldTarget?oldTarget:player.transform;camera.distance=oldDistance;camera.pitch=oldPitch;camera.ResetView();}
        return true;
    }
    void Update()
    {
        if(driver&&!AccountSession.HasCharacter){Exit(true);return;}
        bool blocked=!driver||StartupLoader.IsLoading||AccountSession.InputBlocked||InGameChat.Editing||(GameHud.Instance&&GameHud.Instance.IsPanelOpen)||(TutorialJourney.Instance&&TutorialJourney.Instance.ModalOpen);
        Vector2 input=Vector2.zero;
        if(!blocked)
        {
            input=MobileControls.HasTouches?MobileControls.Movement:new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
            if(AccountSession.VerificationTransport!=null&&TutorialJourney.AllowVerification&&VerificationInput.HasValue)input=VerificationInput.Value;
            if(Input.GetKeyDown(KeyCode.F)&&Exit())return;
        }
        float target=blocked?0:input.y*7.5f;Speed=Mathf.MoveTowards(Speed,target,(blocked?15:5)*Time.deltaTime);
        if(Mathf.Abs(Speed)>.15f)transform.Rotate(0,input.x*65*Mathf.Clamp(Speed/3,-1,1)*Time.deltaTime,0);
        if(controller.isGrounded&&vertical<0)vertical=-2;vertical=Mathf.Max(-30,vertical-22*Time.deltaTime);
        Vector3 before=transform.position;controller.Move((transform.forward*Speed+Vector3.up*vertical)*Time.deltaTime);
        float moved=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
        if(Mathf.Abs(Speed)>1&&moved<.001f)Speed=0;
        foreach(var wheel in wheels)wheel.Rotate(0,Speed*180*Time.deltaTime,0,Space.Self);
    }
    void LateUpdate(){if(driver)driver.transform.SetPositionAndRotation(transform.position+Vector3.up*.08f,transform.rotation);}
    void OnDestroy(){Exit(true);}
}
