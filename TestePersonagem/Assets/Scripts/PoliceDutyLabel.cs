using UnityEngine;

// Temporary police duty marker until an animated uniform is available.
public sealed class PoliceDutyLabel : MonoBehaviour
{
    TextMesh label;
    void Start()
    {
        var marker=new GameObject("Identificação policial",typeof(TextMesh));
        marker.transform.SetParent(transform,false);
        marker.transform.localPosition=new Vector3(0,2.18f,0);
        label=marker.GetComponent<TextMesh>();
        label.text="POLÍCIA";
        label.anchor=TextAnchor.MiddleCenter;
        label.alignment=TextAlignment.Center;
        label.fontSize=40;
        label.characterSize=.026f;
        label.color=new Color(.36f,.83f,1f);
        label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        marker.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
        marker.SetActive(false);
    }
    void LateUpdate()
    {
        if(!label)return;
        bool active=AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty;
        if(label.gameObject.activeSelf!=active)label.gameObject.SetActive(active);
        if(active&&Camera.main)
            label.transform.rotation=Quaternion.LookRotation(label.transform.position-Camera.main.transform.position);
    }
}
