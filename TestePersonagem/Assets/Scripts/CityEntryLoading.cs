using UnityEngine;

// Covers scene activation so the player never sees an unfinished city frame.
public sealed class CityEntryLoading : MonoBehaviour
{
    static CityEntryLoading current;
    public static bool Active => current;
    public float Progress {get;set;}=-1;
    public string Detail {get;set;}="Carregando seu personagem e o cenário";
    public float Opacity {get;set;}=1;

    public static CityEntryLoading Show()
    {
        if(!current)current=new GameObject("Carregando entrada na cidade").AddComponent<CityEntryLoading>();
        return current;
    }
    void Awake(){current=this;DontDestroyOnLoad(gameObject);}
    void OnGUI()
    {
        GUI.depth=-3000;
        HorizonteEntryVisuals.DrawLoading("ENTRANDO NA CIDADE",Detail,Progress,Opacity);
    }
    void OnDestroy(){if(current==this)current=null;}
}
