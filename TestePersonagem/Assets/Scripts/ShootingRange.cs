using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(ThirdPersonPlayer))]
public sealed class ShootingRange : MonoBehaviour
{
    public static ShootingRange Instance { get; private set; }
    public bool IsActive { get; private set; }
    public int Hits { get; private set; }
    public int Shots { get; private set; }
    public int Ammo { get; private set; } = MagazineSize;
    public int TargetCount => targets.Count;
    public VisualElement OverlayElement => overlay;
    public bool UsesImportedPistol => pistol&&pistol.Find("Colt Pocket Hammer");
    public bool WaitingForShot => animator&&animator.GetFloat(SpeedParameter)<.01f;

    const int MagazineSize = 12;
    const float InteriorLimitY = -45f;
    const string LayerName = "SocoParteSuperior";
    const string SpeedParameter = "ShootingSpeed";
    const float AimPoseTime = .08f;
    const float FirePoseTime = .22f;
    ThirdPersonPlayer player;
    ThirdPersonCamera orbit;
    Animator animator;
    FighterCombo fighter;
    Transform room;
    Transform pistol,muzzle,weaponHand;
    readonly List<RangeTarget> targets=new List<RangeTarget>();
    UIDocument document;
    VisualElement overlay,fireButton;
    Label score,ammoLabel,status;
    int shootingLayer=-1;
    float nextShot;
    bool reloading;
    AudioSource audioSource;
    AudioClip shotSound;
    Coroutine shootingAnimation;

    public static ShootingRange Create(ThirdPersonPlayer target,Transform roomRoot)
    {
        if(!target||!roomRoot)return null;
        var range=target.GetComponent<ShootingRange>()??target.gameObject.AddComponent<ShootingRange>();
        range.Initialize(target,roomRoot);
        return range;
    }

    void Initialize(ThirdPersonPlayer target,Transform roomRoot)
    {
        player=target;room=roomRoot;orbit=FindAnyObjectByType<ThirdPersonCamera>();
        BuildTargets();
    }

    void Awake(){Instance=this;player=GetComponent<ThirdPersonPlayer>();}

    IEnumerator Start()
    {
        while(player&&(StartupLoader.IsLoading||!GameHud.Instance))yield return null;
        if(!player)yield break;
        animator=player.animator;fighter=GetComponent<FighterCombo>();orbit=FindAnyObjectByType<ThirdPersonCamera>();
        shootingLayer=animator?animator.GetLayerIndex(LayerName):-1;
        CreateInterface();CreateShotSound();
    }

    void Update()
    {
        if(!player||!room)return;
        bool inside=player.transform.position.y<InteriorLimitY&&
            Mathf.Abs(player.transform.position.x-room.position.x)<6.3f&&
            player.transform.position.z-room.position.z>-14.8f&&player.transform.position.z-room.position.z<11.8f;
        if(inside&&!IsActive&&!StartupLoader.IsLoading&&!AccountSession.InputBlocked)Activate();
        else if(!inside&&IsActive)Deactivate();
        if(!IsActive)return;
        if(orbit)
        {
            var facing=Quaternion.Euler(0,orbit.yaw,0);
            player.transform.rotation=Quaternion.RotateTowards(player.transform.rotation,facing,540f*Time.deltaTime);
        }
        if(!MobileControls.HasTouches&&Input.GetMouseButtonDown(0)&&
            (!GameHud.Instance||!GameHud.Instance.BlocksPointer(Input.mousePosition)))RequestFire();
        if(Input.GetKeyDown(KeyCode.F))RequestFire();
    }

    void LateUpdate()
    {
        if(IsActive&&pistol&&weaponHand)AlignPistol();
    }

    void Activate()
    {
        IsActive=true;Hits=Shots=0;Ammo=MagazineSize;reloading=false;
        animator=player.animator;fighter=GetComponent<FighterCombo>();
        shootingLayer=animator?animator.GetLayerIndex(LayerName):-1;
        if(fighter)fighter.enabled=false;
        if(animator&&shootingLayer>=0)
        {
            animator.SetFloat(SpeedParameter,0f);
            animator.Play("Shooting",shootingLayer,AimPoseTime);
            animator.SetLayerWeight(shootingLayer,1f);
            animator.Update(0f);
        }
        orbit?.BeginAim();CreatePistol();
        FindAnyObjectByType<HousePortal>()?.SetInteriorArrowVisible(false);
        if(overlay!=null)overlay.style.display=DisplayStyle.Flex;
        SetStatus("MODO DE TREINO · MIRE E DISPARE");RefreshInterface();
        GameHud.Instance?.ShowToast("Treino de tiro iniciado");
    }

    void Deactivate()
    {
        IsActive=false;reloading=false;
        if(shootingAnimation!=null){StopCoroutine(shootingAnimation);shootingAnimation=null;}
        if(animator&&shootingLayer>=0){animator.SetFloat(SpeedParameter,0f);animator.SetLayerWeight(shootingLayer,0f);}
        if(fighter)fighter.enabled=true;
        orbit?.EndAim();
        FindAnyObjectByType<HousePortal>()?.SetInteriorArrowVisible(true);
        if(pistol)Destroy(pistol.gameObject);
        pistol=muzzle=weaponHand=null;
        if(overlay!=null)overlay.style.display=DisplayStyle.None;
    }

    public void RequestFire()
    {
        if(!IsActive||reloading||Time.unscaledTime<nextShot||StartupLoader.IsLoading||
            AccountSession.InputBlocked||InGameChat.Editing||(GameHud.Instance&&GameHud.Instance.IsPanelOpen))return;
        if(Ammo<=0){StartCoroutine(Reload());return;}
        nextShot=Time.unscaledTime+.18f;Ammo--;Shots++;
        if(shootingAnimation!=null)StopCoroutine(shootingAnimation);
        shootingAnimation=StartCoroutine(PlayShotAnimation());
        if(audioSource&&shotSound)audioSource.PlayOneShot(shotSound,.32f);
        var camera=Camera.main;
        if(!camera){RefreshInterface();return;}
        Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f));
        Vector3 end=ray.origin+ray.direction*45f;
        if(Physics.Raycast(ray,out RaycastHit hit,45f,~(1<<2),QueryTriggerInteraction.Ignore))
        {
            end=hit.point;
            var target=hit.collider.GetComponentInParent<RangeTarget>();
            if(target&&target.RegisterHit()){Hits++;SetStatus("ALVO ATINGIDO · "+Hits+" ACERTOS");}
        }
        StartCoroutine(Tracer(ray.origin,end));
        RefreshInterface();
        if(Ammo==0)StartCoroutine(Reload());
    }

    IEnumerator PlayShotAnimation()
    {
        if(!animator||shootingLayer<0)yield break;
        animator.SetFloat(SpeedParameter,1.45f);
        animator.Play("Shooting",shootingLayer,FirePoseTime);
        animator.Update(0f);
        yield return new WaitForSecondsRealtime(.32f);
        if(!IsActive)yield break;
        animator.SetFloat(SpeedParameter,0f);
        animator.Play("Shooting",shootingLayer,AimPoseTime);
        animator.Update(0f);
        shootingAnimation=null;
    }

    IEnumerator Reload()
    {
        if(reloading)yield break;
        reloading=true;SetStatus("RECARREGANDO...");
        if(fireButton!=null)fireButton.SetEnabled(false);
        yield return new WaitForSecondsRealtime(1.05f);
        Ammo=MagazineSize;reloading=false;
        if(fireButton!=null)fireButton.SetEnabled(true);
        SetStatus("PRONTO · CONTINUE O TREINO");RefreshInterface();
    }

    IEnumerator Tracer(Vector3 from,Vector3 to)
    {
        var tracer=new GameObject("Rastro do disparo");
        var line=tracer.AddComponent<LineRenderer>();
        line.positionCount=2;line.SetPosition(0,muzzle?muzzle.position:from);line.SetPosition(1,to);
        line.startWidth=.018f;line.endWidth=.004f;
        line.material=new Material(Shader.Find("Sprites/Default"));
        line.startColor=new Color(1f,.8f,.25f,.95f);line.endColor=new Color(1f,.35f,.05f,.05f);
        yield return new WaitForSecondsRealtime(.055f);
        Destroy(tracer);
    }

    void BuildTargets()
    {
        if(!room||targets.Count>0)return;
        var board=MaterialColor(new Color(.83f,.85f,.82f));
        var dark=MaterialColor(new Color(.05f,.06f,.075f));
        var red=MaterialColor(new Color(.84f,.12f,.1f),true);
        for(int i=0;i<4;i++)
        {
            float x=-4.5f+i*3f;
            var root=new GameObject("Alvo móvel "+(i+1));root.transform.SetParent(room,false);
            root.transform.localPosition=new Vector3(x,1.55f,7.3f+i%2*1.6f);
            var target=root.AddComponent<RangeTarget>();target.Configure(i,board,dark,red);
            targets.Add(target);
        }
    }

    void CreatePistol()
    {
        if(pistol||!animator||!animator.isHuman)return;
        var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);if(!hand)return;
        weaponHand=hand;
        pistol=new GameObject("Pistola de treino").transform;pistol.SetParent(transform,true);
        pistol.position=hand.position;pistol.rotation=transform.rotation;
        var prefab=Resources.Load<GameObject>("Weapons/ColtPocket/ColtPocketHammer");
        if(prefab)
        {
            var model=Instantiate(prefab,pistol,false);model.name="Colt Pocket Hammer";
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
            foreach(var importedCamera in model.GetComponentsInChildren<Camera>(true))
                importedCamera.gameObject.SetActive(false);
            foreach(var importedLight in model.GetComponentsInChildren<Light>(true))
                importedLight.gameObject.SetActive(false);
            var gripBone=model.transform.Find("Armature/body/mag");
            var slideBone=model.transform.Find("Armature/body/slide");
            var muzzleBone=model.transform.Find("Armature/body/slide/slide_end");
            if(slideBone&&muzzleBone)
            {
                Vector3 barrelAxis=model.transform.InverseTransformDirection(muzzleBone.position-slideBone.position);
                if(barrelAxis.sqrMagnitude>.000001f)
                    model.transform.localRotation=Quaternion.FromToRotation(barrelAxis.normalized,Vector3.forward);
            }
            var renderers=model.GetComponentsInChildren<Renderer>(false);
            if(renderers.Length>0)
            {
                Bounds bounds=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                float largest=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                if(largest>.0001f)model.transform.localScale=Vector3.one*(.27f/largest);
                if(gripBone)model.transform.localPosition-=pistol.InverseTransformPoint(gripBone.position);
                else
                {
                    bounds=LocalBounds(pistol,renderers);
                    Vector3 grip=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.32f,bounds.min.z+bounds.size.z*.22f);
                    model.transform.localPosition-=grip;
                }
                foreach(var renderer in renderers)renderer.gameObject.layer=2;
                muzzle=new GameObject("Boca do cano").transform;muzzle.SetParent(pistol,false);
                if(muzzleBone)muzzle.localPosition=pistol.InverseTransformPoint(muzzleBone.position)+Vector3.forward*.018f;
                else
                {
                    bounds=LocalBounds(pistol,renderers);
                    muzzle.localPosition=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.72f,bounds.max.z+.015f);
                }
            }
        }
        else
        {
            var metal=MaterialColor(new Color(.045f,.05f,.06f));
            Part("Ferrolho",pistol,new Vector3(0,.025f,.14f),new Vector3(.09f,.10f,.29f),metal);
            Part("Empunhadura",pistol,new Vector3(0,-.09f,.035f),new Vector3(.075f,.20f,.085f),metal,Quaternion.Euler(-12,0,0));
        }
        if(!muzzle){muzzle=new GameObject("Boca do cano").transform;muzzle.SetParent(pistol,false);muzzle.localPosition=new Vector3(0,.08f,.31f);}
        AlignPistol();
    }

    static Bounds LocalBounds(Transform root,Renderer[] renderers)
    {
        Bounds result=new Bounds(root.InverseTransformPoint(renderers[0].bounds.center),Vector3.zero);
        foreach(var renderer in renderers)
        {
            Bounds world=renderer.bounds;Vector3 min=world.min,max=world.max;
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
                result.Encapsulate(root.InverseTransformPoint(new Vector3(x==0?min.x:max.x,y==0?min.y:max.y,z==0?min.z:max.z)));
        }
        return result;
    }

    void AlignPistol()
    {
        var camera=Camera.main;
        Vector3 direction=camera?camera.transform.forward:transform.forward;
        pistol.SetPositionAndRotation(weaponHand.position+direction*.015f+transform.up*.012f,
            Quaternion.LookRotation(direction,transform.up));
    }

    void CreateInterface()
    {
        if(document||!GameHud.Instance)return;
        var go=new GameObject("Interface do treino de tiro");go.transform.SetParent(transform,false);
        document=go.AddComponent<UIDocument>();document.panelSettings=Resources.Load<PanelSettings>("HudPanel");document.sortingOrder=90;
        overlay=new VisualElement{name="shooting-range-overlay"};overlay.style.position=Position.Absolute;
        overlay.style.left=overlay.style.right=overlay.style.top=overlay.style.bottom=0;overlay.pickingMode=PickingMode.Ignore;
        var cross=new Label("+"){name="shooting-crosshair"};cross.style.position=Position.Absolute;cross.style.left=Length.Percent(50);cross.style.top=Length.Percent(50);
        cross.style.marginLeft=-15;cross.style.marginTop=-25;cross.style.width=30;cross.style.height=50;
        cross.style.fontSize=32;cross.style.color=new Color(1f,.82f,.22f);cross.style.unityFontStyleAndWeight=FontStyle.Bold;
        cross.style.unityTextAlign=TextAnchor.MiddleCenter;cross.pickingMode=PickingMode.Ignore;overlay.Add(cross);
        var card=new VisualElement{name="shooting-score"};card.style.position=Position.Absolute;card.style.left=Length.Percent(50);card.style.marginLeft=-150;card.style.top=62;
        card.style.width=300;card.style.paddingLeft=card.style.paddingRight=18;card.style.paddingTop=card.style.paddingBottom=10;
        card.style.backgroundColor=new Color(.015f,.025f,.04f,.82f);card.style.borderBottomLeftRadius=card.style.borderBottomRightRadius=10;
        card.style.borderTopLeftRadius=card.style.borderTopRightRadius=10;card.pickingMode=PickingMode.Ignore;
        status=new Label("MODO DE TREINO");status.style.color=new Color(1f,.78f,.18f);status.style.fontSize=13;status.style.unityFontStyleAndWeight=FontStyle.Bold;card.Add(status);
        score=new Label("ACERTOS 0 / DISPAROS 0");score.style.color=Color.white;score.style.fontSize=16;score.style.marginTop=4;card.Add(score);overlay.Add(card);
        fireButton=new Button{name="shooting-fire"};fireButton.RegisterCallback<PointerDownEvent>(_=>RequestFire());
        fireButton.style.position=Position.Absolute;fireButton.style.right=34;fireButton.style.bottom=248;
        fireButton.style.width=fireButton.style.height=92;fireButton.style.borderTopLeftRadius=fireButton.style.borderTopRightRadius=46;
        fireButton.style.borderBottomLeftRadius=fireButton.style.borderBottomRightRadius=46;fireButton.style.backgroundColor=new Color(.055f,.065f,.08f,.94f);
        fireButton.style.borderTopWidth=fireButton.style.borderBottomWidth=fireButton.style.borderLeftWidth=fireButton.style.borderRightWidth=2;
        fireButton.style.borderTopColor=fireButton.style.borderBottomColor=fireButton.style.borderLeftColor=fireButton.style.borderRightColor=new Color(1f,.72f,.12f);
        ammoLabel=new Label("12"){pickingMode=PickingMode.Ignore};ammoLabel.style.color=Color.white;ammoLabel.style.fontSize=24;ammoLabel.style.unityFontStyleAndWeight=FontStyle.Bold;
        ammoLabel.style.unityTextAlign=TextAnchor.MiddleCenter;fireButton.Add(ammoLabel);overlay.Add(fireButton);
        document.rootVisualElement.Add(overlay);GameHud.Instance.RegisterPointerBlocker(fireButton);overlay.style.display=DisplayStyle.None;
    }

    void RefreshInterface()
    {
        if(score!=null)score.text="ACERTOS "+Hits+"  /  DISPAROS "+Shots;
        if(ammoLabel!=null)ammoLabel.text=Ammo.ToString("D2")+"\nTIRO";
    }
    void SetStatus(string value){if(status!=null)status.text=value;}

    void CreateShotSound()
    {
        audioSource=gameObject.AddComponent<AudioSource>();audioSource.spatialBlend=0;
        const int rate=22050,length=1450;var samples=new float[length];var random=new System.Random(9182);
        for(int i=0;i<length;i++)
        {
            float decay=Mathf.Exp(-i/210f);samples[i]=((float)random.NextDouble()*2f-1f)*decay*.75f;
            if(i<90)samples[i]+=Mathf.Sin(i*.62f)*(1f-i/90f)*.45f;
        }
        shotSound=AudioClip.Create("Disparo de treino",length,1,rate,false);shotSound.SetData(samples,0);
    }

    static GameObject Part(string name,Transform parent,Vector3 position,Vector3 scale,Material material,Quaternion? rotation=null)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localRotation=rotation??Quaternion.identity;part.transform.localScale=scale;
        part.GetComponent<Renderer>().sharedMaterial=material;Destroy(part.GetComponent<Collider>());part.layer=2;return part;
    }
    static Material MaterialColor(Color color,bool luminous=false)
    {
        var shader=Shader.Find("Standard")??Shader.Find("Universal Render Pipeline/Lit");var material=new Material(shader){color=color};
        if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",color);
        if(luminous){material.EnableKeyword("_EMISSION");if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",color*1.8f);}return material;
    }
    void OnDestroy(){if(IsActive)Deactivate();if(Instance==this)Instance=null;if(shotSound)Destroy(shotSound);}
}

public sealed class RangeTarget : MonoBehaviour
{
    Vector3 rest;
    Quaternion restRotation;
    float phase;
    bool available=true;
    Collider hitbox;

    public void Configure(int index,Material board,Material dark,Material red)
    {
        phase=index*1.73f;rest=transform.localPosition;restRotation=transform.localRotation;
        var plate=GameObject.CreatePrimitive(PrimitiveType.Cube);plate.name="Placa do alvo";plate.transform.SetParent(transform,false);
        plate.transform.localScale=new Vector3(.82f,1.18f,.09f);plate.GetComponent<Renderer>().sharedMaterial=board;hitbox=plate.GetComponent<Collider>();
        for(int ring=0;ring<3;ring++)
        {
            var circle=GameObject.CreatePrimitive(PrimitiveType.Cylinder);circle.name="Círculo "+ring;circle.transform.SetParent(transform,false);
            circle.transform.localPosition=new Vector3(0,0,-.07f-ring*.006f);circle.transform.localRotation=Quaternion.Euler(90,0,0);
            float size=.52f-ring*.15f;circle.transform.localScale=new Vector3(size,.012f,size);
            circle.GetComponent<Renderer>().sharedMaterial=ring==2?red:dark;Destroy(circle.GetComponent<Collider>());
        }
        var stand=GameObject.CreatePrimitive(PrimitiveType.Cube);stand.name="Suporte";stand.transform.SetParent(transform,false);
        stand.transform.localPosition=new Vector3(0,-1.05f,.06f);stand.transform.localScale=new Vector3(.08f,.95f,.08f);
        stand.GetComponent<Renderer>().sharedMaterial=dark;Destroy(stand.GetComponent<Collider>());
    }

    void Update()
    {
        if(!available)return;
        var position=rest;position.x+=Mathf.Sin(Time.time*.72f+phase)*.52f;transform.localPosition=position;
    }

    public bool RegisterHit()
    {
        if(!available)return false;
        available=false;if(hitbox)hitbox.enabled=false;StartCoroutine(Reactivate());return true;
    }

    IEnumerator Reactivate()
    {
        float elapsed=0;
        while(elapsed<.24f){elapsed+=Time.deltaTime;transform.localRotation=Quaternion.Slerp(restRotation,restRotation*Quaternion.Euler(82,0,0),elapsed/.24f);yield return null;}
        yield return new WaitForSecondsRealtime(1.15f);
        transform.localPosition=rest;transform.localRotation=restRotation;available=true;if(hitbox)hitbox.enabled=true;
    }
}
