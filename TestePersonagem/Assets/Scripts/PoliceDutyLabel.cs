using UnityEngine;

// Identification above the local avatar, matching the labels seen on peers.
public sealed class PoliceDutyLabel : MonoBehaviour
{
    ThirdPersonPlayer player;
    TextMesh namePlate, policePlate, adminPlate;
    TextMesh[] nameOutline, policeOutline, adminOutline;
    WorldMicrophoneIndicator microphone;

    void Start()
    {
        player=GetComponent<ThirdPersonPlayer>();
        var font=HudPack.LabelFont;
        namePlate=CreatePlate("Nome local",1.84f,39,Color.white,font,out nameOutline);
        policePlate=CreatePlate("Cargo policial",2.04f,32,new Color(.36f,.83f,1f),font,out policeOutline);
        adminPlate=CreatePlate("Cargo administrativo",1.66f,32,new Color(1f,.82f,.13f),font,out adminOutline);
        microphone=WorldMicrophoneIndicator.Create(namePlate);
    }

    TextMesh CreatePlate(string title,float height,int size,Color color,Font font,out TextMesh[] outline)
    {
        var marker=new GameObject(title,typeof(TextMesh));marker.layer=2;
        marker.transform.SetParent(transform,false);
        marker.transform.localPosition=new Vector3(0,height,0);
        var text=marker.GetComponent<TextMesh>();
        text.richText=false;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
        text.fontSize=size;text.characterSize=.025f;text.fontStyle=FontStyle.Bold;
        text.color=color;text.font=font;
        var renderer=marker.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.sortingOrder=1;
        outline=new TextMesh[8];int index=0;
        foreach(int x in new[]{-1,0,1})foreach(int y in new[]{-1,0,1})
        {
            if(x==0&&y==0)continue;
            var edge=new GameObject("Contorno preto",typeof(TextMesh));edge.layer=2;
            edge.transform.SetParent(marker.transform,false);
            edge.transform.localPosition=new Vector3(x*.008f,y*.008f,.005f);
            var shadow=edge.GetComponent<TextMesh>();shadow.richText=false;
            shadow.anchor=TextAnchor.MiddleCenter;shadow.alignment=TextAlignment.Center;
            shadow.fontSize=size;shadow.characterSize=.025f;shadow.fontStyle=FontStyle.Bold;
            shadow.font=font;shadow.color=Color.black;
            var shadowRenderer=edge.GetComponent<MeshRenderer>();shadowRenderer.sharedMaterial=font.material;shadowRenderer.sortingOrder=0;
            outline[index++]=shadow;
        }
        marker.SetActive(false);
        return text;
    }

    static void SetText(TextMesh plate,TextMesh[] outline,string value)
    {
        if(plate.text==value)return;
        plate.text=value;
        foreach(var edge in outline)edge.text=value;
    }

    void LateUpdate()
    {
        if(!namePlate)return;
        var state=AccountSession.HasCharacter?AccountSession.Instance.State:null;
        bool visible=state!=null&&player&&!player.InPoliceCar&&MissionVehicle.Driver!=player;
        namePlate.gameObject.SetActive(visible);
        if(!visible)
        {
            policePlate.gameObject.SetActive(false);
            adminPlate.gameObject.SetActive(false);
            microphone.gameObject.SetActive(false);
            return;
        }
        SetText(namePlate,nameOutline,PlayerDisplayName.From(state)+" ["+state.uid+"]");
        bool police=AdminPlayerPanel.PoliceDuty;
        policePlate.gameObject.SetActive(police);
        if(police)SetText(policePlate,policeOutline,"POLÍCIA");
        bool admin=state.accountRole=="admin"&&state.adminLevel>0;
        adminPlate.gameObject.SetActive(admin);
        if(admin)SetText(adminPlate,adminOutline,"ADMIN");
        microphone.Refresh(VoiceChat.Instance&&VoiceChat.Instance.Transmitting,namePlate);
        if(Camera.main)
        {
            var rotation=Camera.main.transform.rotation;
            namePlate.transform.rotation=rotation;
            policePlate.transform.rotation=rotation;
            adminPlate.transform.rotation=rotation;
        }
    }
}
