using UnityEngine;

// Covers scene activation so the player never sees an unfinished city frame.
public sealed class CityEntryLoading : MonoBehaviour
{
    static CityEntryLoading current;
    public static bool Active => current;
    public float Progress {get;set;}
    public float Opacity {get;set;}=1;
    GUIStyle title,detail;

    public static CityEntryLoading Show()
    {
        if(!current)current=new GameObject("Carregando entrada na cidade").AddComponent<CityEntryLoading>();
        return current;
    }
    void Awake(){current=this;DontDestroyOnLoad(gameObject);}
    void OnGUI()
    {
        GUI.depth=-3000;
        GUI.color=new Color(0,0,0,Opacity);
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
        float scale=Screen.height/720f;
        if(title==null)
        {
            title=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            title.normal.textColor=Color.white;
            detail=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter};
            detail.normal.textColor=new Color(.62f,.67f,.74f);
        }
        title.fontSize=Mathf.Max(15,Mathf.RoundToInt(21*scale));
        detail.fontSize=Mathf.Max(10,Mathf.RoundToInt(12*scale));
        float width=Mathf.Min(Screen.width*.68f,420*scale),left=(Screen.width-width)/2;
        float top=Screen.height*.69f;
        GUI.color=new Color(1,1,1,Opacity);
        string dots=new string('.',1+(Mathf.FloorToInt(Time.realtimeSinceStartup*2)%3));
        GUI.Label(new Rect(0,top-55*scale,Screen.width,35*scale),"ENTRANDO NA CIDADE"+dots,title);
        GUI.Label(new Rect(0,top-18*scale,Screen.width,25*scale),"Carregando seu personagem e o cenário",detail);
        GUI.color=new Color(.12f,.14f,.17f,Opacity);
        GUI.DrawTexture(new Rect(left,top+25*scale,width,4*scale),Texture2D.whiteTexture);
        GUI.color=new Color(1,.8f,.27f,Opacity);
        GUI.DrawTexture(new Rect(left,top+25*scale,width*Mathf.Clamp01(Progress),4*scale),Texture2D.whiteTexture);
        GUI.color=Color.white;
    }
    void OnDestroy(){if(current==this)current=null;}
}
