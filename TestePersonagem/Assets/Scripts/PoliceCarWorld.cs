using System.Collections.Generic;
using UnityEngine;

// Server-owned parked police cars. The seat marker is visible even if the source model's windows are dark.
public sealed class PoliceCarWorld : MonoBehaviour
{
    sealed class CarVisual
    {
        public GameObject root,occupant;
        public TextMesh label;
    }
    readonly Dictionary<string,CarVisual> visuals=new Dictionary<string,CarVisual>();
    ThirdPersonPlayer player;

    public void Initialize(ThirdPersonPlayer owner){player=owner;}

    public void Apply(PoliceCarState[] cars)
    {
        var current=new HashSet<string>();
        if(cars!=null)foreach(var car in cars)
        {
            if(string.IsNullOrEmpty(car.id))continue;
            current.Add(car.id);
            if(!visuals.TryGetValue(car.id,out var visual))
            {visual=Build(car.id);visuals.Add(car.id,visual);}
            Vector3 position=new Vector3(car.x,car.y,car.z);
            if(Physics.Raycast(position+Vector3.up*2,Vector3.down,out var hit,5,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                position.y=hit.point.y;
            visual.root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,car.yaw,0));
            bool occupied=!string.IsNullOrEmpty(car.occupantUid);
            visual.label.gameObject.SetActive(occupied);
            visual.label.text=occupied?"DETIDO  ID "+car.occupantUid+"  ·  "+car.occupantName:"";
            if(visual.occupant)visual.occupant.SetActive(occupied);
        }
        var removed=new List<string>();
        foreach(var id in visuals.Keys)if(!current.Contains(id))removed.Add(id);
        foreach(var id in removed){Destroy(visuals[id].root);visuals.Remove(id);}
    }

    CarVisual Build(string id)
    {
        var root=new GameObject("Viatura policial "+id);root.layer=2;
        var source=Resources.Load<GameObject>("Vehicles/Police/PoliceCar");
        if(source)
        {
            var model=Instantiate(source,root.transform);model.name="carro_policia";
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length>0)
            {
                Bounds bounds=renderers[0].bounds;
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                if(bounds.size.x>bounds.size.z)model.transform.localRotation=Quaternion.Euler(0,90,0);
                float length=Mathf.Max(bounds.size.x,bounds.size.z);
                if(length>.01f)model.transform.localScale*=4.55f/length;
                renderers=model.GetComponentsInChildren<Renderer>();bounds=renderers[0].bounds;
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                model.transform.position+=Vector3.up*(root.transform.position.y+.03f-bounds.min.y);
            }
        }
        else
        {
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.name="Viatura provisória";
            body.transform.SetParent(root.transform,false);body.transform.localPosition=new Vector3(0,.75f,0);
            body.transform.localScale=new Vector3(2,1.25f,4.55f);
            body.GetComponent<Renderer>().material.color=new Color(.12f,.19f,.26f);
            Destroy(body.GetComponent<Collider>());
        }
        var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.82f,0);collider.size=new Vector3(2.05f,1.65f,4.5f);
        var badge=new GameObject("Ocupante",typeof(TextMesh));badge.layer=2;badge.transform.SetParent(root.transform,false);
        badge.transform.localPosition=new Vector3(0,2.15f,0);
        var text=badge.GetComponent<TextMesh>();text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
        text.fontSize=36;text.characterSize=.025f;text.color=new Color(1f,.78f,.28f);
        text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        badge.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
        GameObject occupant=null;
        if(player&&player.animator)
        {
            occupant=Instantiate(player.animator.gameObject,root.transform,false);
            occupant.name="Detido no banco traseiro";
            occupant.transform.localPosition=new Vector3(.43f,.55f,-.38f);
            occupant.transform.localRotation=Quaternion.identity;
            occupant.transform.localScale=Vector3.one*.62f;
            foreach(var c in occupant.GetComponentsInChildren<Collider>())c.enabled=false;
            foreach(var b in occupant.GetComponentsInChildren<MonoBehaviour>())b.enabled=false;
            var animator=occupant.GetComponent<Animator>();
            if(animator){animator.Rebind();animator.SetFloat("Speed",0);animator.SetBool("Jumping",false);}
        }
        badge.SetActive(false);if(occupant)occupant.SetActive(false);
        return new CarVisual{root=root,occupant=occupant,label=text};
    }

    void LateUpdate()
    {
        if(!Camera.main)return;
        foreach(var visual in visuals.Values)visual.label.transform.rotation=Camera.main.transform.rotation;
    }
    public void Clear(){Apply(System.Array.Empty<PoliceCarState>());}
    void OnDestroy(){Clear();}
}
