using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// Small temporary detention room below the playable city. Coordinates match police-operations.mjs.
public sealed class PolicePrisonWorld : MonoBehaviour
{
    public static readonly Vector3 OfficerEntry = new Vector3(0, -99.9f, 35.5f);
    public static readonly Vector3 StreetExit = new Vector3(1546, 12.62f, 1676);
    static readonly Vector3 Center = new Vector3(0, -100, 40);
    GameObject room,streetMarker,fadeObject;
    ThirdPersonPlayer player;
    VisualElement fadeCover;
    bool transitioning;

    public static bool IsInside(Vector3 point) => point.y < -94 && point.y > -105 &&
        Mathf.Abs(point.x) < 10 && point.z > 31 && point.z < 48;

    public static bool IsCellPosition(Vector3 point) => IsInside(point) &&
        Mathf.Abs(point.z - 42.8f) < .45f &&
        (Mathf.Abs(point.x + 5) < .45f || Mathf.Abs(point.x) < .45f || Mathf.Abs(point.x - 5) < .45f);

    public static void Create(ThirdPersonPlayer player)
    {
        if(player && !player.GetComponent<PolicePrisonWorld>())player.gameObject.AddComponent<PolicePrisonWorld>();
    }

    public static void Travel(ThirdPersonPlayer player,Vector3 destination)
    {
        if(!player)return;
        Create(player);
        var prison=player.GetComponent<PolicePrisonWorld>();
        if(prison)prison.BeginTravel(destination);
        else player.TeleportTo(destination);
    }

    void BeginTravel(Vector3 destination)
    {
        if(transitioning)return;
        StartCoroutine(TravelRoutine(destination));
    }

    IEnumerator TravelRoutine(Vector3 destination)
    {
        transitioning=true;
        bool previousCombatLock=player.CombatLocked;
        player.CombatLocked=true;
        fadeCover.style.display=DisplayStyle.Flex;
        yield return Fade(0,1,.35f);
        player.TeleportTo(destination);
        while(player&&player.IsTeleporting)yield return null;
        if(!player)yield break;
        var camera=FindAnyObjectByType<ThirdPersonCamera>();
        if(camera)camera.ResetView();
        yield return null;
        yield return Fade(1,0,.5f);
        fadeCover.style.display=DisplayStyle.None;
        player.CombatLocked=previousCombatLock;
        transitioning=false;
    }

    IEnumerator Fade(float from,float to,float seconds)
    {
        float elapsed=0;
        fadeCover.style.opacity=from;
        while(elapsed<seconds)
        {
            elapsed+=Time.unscaledDeltaTime;
            fadeCover.style.opacity=Mathf.Lerp(from,to,Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/seconds)));
            yield return null;
        }
        fadeCover.style.opacity=to;
    }

    void CreateFade()
    {
        fadeObject=new GameObject("Transição da custódia");
        fadeObject.transform.SetParent(transform,false);
        var document=fadeObject.AddComponent<UIDocument>();
        document.panelSettings=Resources.Load<PanelSettings>("HudPanel");
        document.sortingOrder=600;
        fadeCover=new VisualElement {name="police-prison-fade"};
        fadeCover.style.position=Position.Absolute;
        fadeCover.style.left=0;fadeCover.style.right=0;
        fadeCover.style.top=0;fadeCover.style.bottom=0;
        fadeCover.style.backgroundColor=Color.black;
        fadeCover.style.opacity=0;
        fadeCover.style.display=DisplayStyle.None;
        fadeCover.pickingMode=PickingMode.Position;
        document.rootVisualElement.Add(fadeCover);
    }
    void Awake()
    {
        player=GetComponent<ThirdPersonPlayer>();
        CreateFade();
        room = new GameObject("Prisão temporária · três celas");
        room.transform.position = Center;
        var concrete = Material(new Color(.31f,.34f,.36f));
        var dark = Material(new Color(.12f,.15f,.18f));
        var steel = Material(new Color(.56f,.62f,.66f));
        var yellow = Material(new Color(.78f,.56f,.14f));
        Cube("Piso",new Vector3(0,-.18f,0),new Vector3(19,.36f,17),concrete);
        Cube("Teto",new Vector3(0,4.5f,0),new Vector3(19,.3f,17),dark);
        Cube("Parede traseira",new Vector3(0,2.2f,7.8f),new Vector3(19,4.5f,.3f),concrete);
        Cube("Parede esquerda",new Vector3(-9.4f,2.2f,0),new Vector3(.3f,4.5f,16),concrete);
        Cube("Parede direita",new Vector3(9.4f,2.2f,0),new Vector3(.3f,4.5f,16),concrete);
        Cube("Parede da entrada fechada",new Vector3(0,2.2f,-7.8f),new Vector3(19,4.5f,.3f),concrete);
        for(int i=0;i<4;i++)
        {
            float x=-7.5f+i*5;
            Cube("Divisória da cela",new Vector3(x,2.2f,4),new Vector3(.24f,4.4f,7.5f),concrete);
        }
        for(int cell=0;cell<3;cell++)
        {
            float center=-5+cell*5;
            Cube("Travessa superior",new Vector3(center,3.95f,.3f),new Vector3(4.8f,.15f,.15f),steel);
            Cube("Travessa inferior",new Vector3(center,.2f,.3f),new Vector3(4.8f,.15f,.15f),steel);
            for(int bar=0;bar<9;bar++)
                Cube("Grade da cela",new Vector3(center-2.2f+bar*.55f,2.1f,.3f),new Vector3(.09f,3.8f,.1f),steel);
            Cube("Cama",new Vector3(center, .48f,5.65f),new Vector3(2,.32f,.9f),dark);
            Cube("Colchão",new Vector3(center,.68f,5.65f),new Vector3(1.9f,.13f,.82f),steel);
            Sign("CELA 0"+(cell+1),new Vector3(center,4.05f,.05f),.18f);
        }
        Cube("Linha de segurança",new Vector3(0,.015f,-1.8f),new Vector3(16,.03f,.1f),yellow);
        Sign("DELEGACIA  ·  CUSTÓDIA",new Vector3(0,3.65f,-7.53f),.27f);
        streetMarker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        streetMarker.name="Entrada da custódia · E";
        streetMarker.transform.position=StreetExit+new Vector3(0,.08f,0);
        streetMarker.transform.localScale=new Vector3(.85f,.08f,.85f);
        streetMarker.GetComponent<Renderer>().sharedMaterial=yellow;
        Destroy(streetMarker.GetComponent<Collider>());
        var markerText=new GameObject("ENTRAR NA CUSTÓDIA  [E]",typeof(TextMesh));
        markerText.transform.SetParent(streetMarker.transform,false);
        markerText.transform.localPosition=new Vector3(0,10,0);
        markerText.transform.localRotation=Quaternion.Euler(0,180,0);
        var markerLabel=markerText.GetComponent<TextMesh>();
        markerLabel.text="CUSTÓDIA  [E]";markerLabel.fontSize=64;markerLabel.characterSize=.06f;
        markerLabel.anchor=TextAnchor.MiddleCenter;markerLabel.alignment=TextAlignment.Center;markerLabel.color=Color.yellow;
        var lamp=new GameObject("Iluminação da custódia",typeof(Light));
        lamp.transform.SetParent(room.transform,false);
        lamp.transform.localPosition=new Vector3(0,3.8f,-2.5f);
        var light=lamp.GetComponent<Light>();light.type=LightType.Point;light.range=18;light.intensity=1.9f;
        light.color=new Color(.92f,.96f,1f);
    }

    static Material Material(Color color)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material=new Material(shader);material.color=color;
        return material;
    }

    void Cube(string title,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name=title;part.transform.SetParent(room.transform,false);
        part.transform.localPosition=position;part.transform.localScale=scale;
        part.GetComponent<Renderer>().sharedMaterial=material;
    }

    void Sign(string value,Vector3 position,float size)
    {
        var label=new GameObject(value,typeof(TextMesh));label.transform.SetParent(room.transform,false);
        label.transform.localPosition=position;label.transform.localRotation=Quaternion.Euler(0,180,0);
        var text=label.GetComponent<TextMesh>();text.text=value;text.fontSize=64;
        text.characterSize=size/6;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
        text.color=Color.white;text.fontStyle=FontStyle.Bold;
    }

    string NearbyAction()
    {
        if(!player||transitioning||player.IsJailed||player.IsCuffed||!AdminPlayerPanel.PoliceDuty)return null;
        if(Vector3.Distance(player.transform.position,StreetExit)<4)return "ENTRAR NA CUSTÓDIA";
        if(IsInside(player.transform.position))
        {
            if(Vector3.Distance(player.transform.position,OfficerEntry)<2.5f)return "SAIR DA CUSTÓDIA";
            if(player.transform.position.z>38.2f)return "COLOCAR ACOMPANHANTE NA CELA";
        }
        return null;
    }

    void Activate(string action)
    {
        if(action=="COLOCAR ACOMPANHANTE NA CELA")InGameChat.Instance?.OpenWithText("/cela 10 ");
        else InGameChat.Instance?.SendCommand("/delegacia");
    }

    void Update()
    {
        if(InGameChat.Editing||StartupLoader.IsLoading||GameHud.Instance?.IsPanelOpen==true)return;
        string action=NearbyAction();
        if(action!=null&&Input.GetKeyDown(KeyCode.E))Activate(action);
    }

    void OnGUI()
    {
        if(InGameChat.Editing||StartupLoader.IsLoading||GameHud.Instance?.IsPanelOpen==true)return;
        string action=NearbyAction();
        if(action==null)return;
        var area=new Rect((Screen.width-340)/2f,Screen.height-100,340,48);
        if(GUI.Button(area,"E  ·  "+action))Activate(action);
    }
    void OnDestroy(){if(room)Destroy(room);if(streetMarker)Destroy(streetMarker);if(fadeObject)Destroy(fadeObject);}
}
