using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class LocalHudConnectionVerification
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
    [MenuItem("Horizonte/Testes locais/Ping, radar e botão do rádio")]
    public static void Run()
    {
        checks.Clear();
        var g=new RadioTalkGesture();
        Check(g.Down(1,0)&&g.Active,"First finger immediately transmits without another screen touch");
        Check(!g.Down(2,.1)&&!g.Up(2,.2)&&g.Active,"Another finger cannot steal or release the radio");
        Check(g.Up(1,2)&&!g.Active,"Holding stops immediately on release");
        g.Down(1,3);g.Up(1,3.1);g.Down(2,3.25);
        Check(g.Active&&g.Latched,"Two short taps latch transmission");
        g.Up(2,3.3);Check(g.Active&&g.Latched,"Second release keeps a latched radio on");
        g.Down(1,4);Check(!g.Active&&!g.Latched,"One press stops a latched radio immediately");
        g.Up(1,4.1);Check(!g.Active,"Releasing the stop press does not restart the radio");
        g.Down(1,5);g.Up(1,5.1);g.Down(1,5.2);g.Up(1,5.3);
        g.Cancel();Check(!g.Active&&!g.Latched&&g.Pointer<0,"Pause, focus loss and channel changes clear the latch");
        g.Down(1,6);g.Up(1,6.1,true);Check(!g.Active,"Canceled touch stops transmission");
        g.Down(1,7);g.Up(1,7.1);g.Down(1,7.6);
        Check(!g.Latched,"Widely separated taps do not accidentally latch");g.Cancel();
        Check(PlayerRadar.Visible(false,false,Vector3.zero,new Vector3(1000,70,0)),"Player exactly 1 km away remains visible");
        Check(!PlayerRadar.Visible(false,false,Vector3.zero,new Vector3(1001,0,0)),"Civilian minimap excludes players beyond 1 km");
        Check(!PlayerRadar.Visible(false,true,Vector3.zero,new Vector3(1500,0,0)),"Civilian full map respects the same limit");
        Check(PlayerRadar.Visible(true,true,Vector3.zero,new Vector3(2500,0,0)),"Admin full map includes distant online players");
        Check(!PlayerRadar.Visible(true,false,Vector3.zero,new Vector3(1500,0,0)),"Minimap retains its geographic scale for administrators");
        Check(!PlayerRadar.Visible(true,true,Vector3.zero,new Vector3(float.NaN,0,0)),"Invalid map positions are hidden");
        var east=PlayerRadar.OnMinimap(Vector3.zero,Vector3.right*1000,0,200);
        var north=PlayerRadar.OnMinimap(Vector3.zero,Vector3.forward*-1000,0,200);
        Check((east-new Vector2(-100,0)).magnitude<.001f&&(north-new Vector2(0,100)).magnitude<.001f,"Radar uses the same atlas rotation and metre scale");
        var turn=PlayerRadar.OnMinimap(Vector3.zero,Vector3.right*1000,90,200);
        Check((turn-new Vector2(0,-100)).magnitude<.001f,"Player markers rotate together with the map");
        var streetZoom=PlayerRadar.OnMinimap(Vector3.zero,Vector3.right*90,0,200,450);
        Check((streetZoom-new Vector2(-40,0)).magnitude<.001f,"Street zoom remains 450 m and nearby player dots align with the artwork");
        string folder=Path.GetFullPath("../tools/local-hud-connection");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"unity-validation.json"),JsonUtility.ToJson(new Report{passed=true,checks=checks},true));
        Debug.Log("LOCAL_HUD_CONNECTION_VERIFIED checks="+checks.Count);
    }
    [Serializable] sealed class Report{public bool passed;public List<string> checks;}
}
