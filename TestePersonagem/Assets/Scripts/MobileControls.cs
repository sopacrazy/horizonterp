using UnityEngine;

// Each gesture owns a finger until release; UI touches never become gameplay gestures.
[DefaultExecutionOrder(-100)]
public sealed class MobileControls : MonoBehaviour
{
    public static Vector2 Movement { get; private set; }
    public static Vector2 Look { get; private set; }
    public static bool HasTouches => Input.touchCount > 0;
    int moveFinger = -1, lookFinger = -1;
    Vector2 origin, knob;
    float Radius => Mathf.Clamp(Screen.height * .11f, 55, 150);
    Texture2D circle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Create() { if(FindAnyObjectByType<MobileControls>())return; Movement=Look=Vector2.zero; if(FindAnyObjectByType<ThirdPersonPlayer>())new GameObject("Controles por toque").AddComponent<MobileControls>(); }

    void Awake() { Input.simulateMouseWithTouches=false; }
    void Update()
    {
        Look=Vector2.zero;
        var hud=GameHud.Instance;
        if(StartupLoader.IsLoading || AccountSession.InputBlocked || InGameChat.Editing || (hud && hud.IsPanelOpen)) { Clear(); return; }
        bool foundMove=false,foundLook=false;
        foreach(var touch in Input.touches)
        {
            ProcessTouch(touch.fingerId,touch.position,touch.deltaPosition,touch.phase);
            if(touch.fingerId==moveFinger)foundMove=true;
            if(touch.fingerId==lookFinger)foundLook=true;
        }
        if(!foundMove){moveFinger=-1;Movement=Vector2.zero;}
        if(!foundLook)lookFinger=-1;
    }
    public void ProcessTouch(int id, Vector2 position, Vector2 delta, TouchPhase phase)
    {
            var hud=GameHud.Instance;
            if(InGameChat.Editing || (hud && hud.IsPanelOpen)){Clear();return;}
            if(phase==TouchPhase.Began && !(hud && hud.BlocksPointer(position)))
            {
                if(position.x<Screen.width*.5f && moveFinger<0){moveFinger=id;origin=position;}
                else if(position.x>=Screen.width*.5f && lookFinger<0)lookFinger=id;
            }
            bool ended=phase==TouchPhase.Ended || phase==TouchPhase.Canceled;
            if(id==moveFinger)
            {
                if(ended){moveFinger=-1;Movement=Vector2.zero;}
                else { knob=Vector2.ClampMagnitude(position-origin,Radius);Movement=knob/Radius;if(Movement.magnitude<.12f)Movement=Vector2.zero; }
            }
            if(id==lookFinger)
            {
                if(ended)lookFinger=-1;
                else if(phase==TouchPhase.Moved)Look=delta*(720f/Screen.height);
            }
    }
    void Clear(){moveFinger=lookFinger=-1;Movement=Look=Vector2.zero;}
    void OnApplicationFocus(bool focus){if(!focus)Clear();}
    void OnDisable(){Clear();}
    void OnGUI()
    {
        if(moveFinger<0)return;
        if(!circle)
        {
            circle=new Texture2D(64,64,TextureFormat.RGBA32,false);
            var pixels=new Color[64*64];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01((31.5f-Vector2.Distance(new Vector2(x,y),new Vector2(31.5f,31.5f)))*2));
            circle.SetPixels(pixels);circle.Apply();
        }
        float r=Radius;Vector2 center=new Vector2(origin.x,Screen.height-origin.y);
        GUI.color=new Color(.08f,.09f,.1f,.45f);GUI.DrawTexture(new Rect(center.x-r,center.y-r,r*2,r*2),circle);
        GUI.color=new Color(1,.81f,.25f,.8f);GUI.DrawTexture(new Rect(center.x+knob.x-r*.38f,center.y-knob.y-r*.38f,r*.76f,r*.76f),circle);
        GUI.color=Color.white;
    }
    void OnDestroy(){if(circle)Destroy(circle);}
}
