using UnityEngine;

public sealed partial class GameHud
{
    float nextLatencyRefresh;
    void RefreshLatencyIndicator()
    {
        if(latencyLabel==null||Time.unscaledTime<nextLatencyRefresh)return;
        nextLatencyRefresh=Time.unscaledTime+.25f;
        var world=MultiplayerWorld.Instance;
        if(!world||!world.Connected)
        {
            latencyLabel.text=world?"SEM CONEXÃO":"PING —";
            SetLatencyColor(new Color(.76f,.79f,.81f));return;
        }
        if(!world.HasLatencySample)
        {latencyLabel.text="MEDINDO…";SetLatencyColor(Color.white);return;}
        bool stale=world.LatencyAge>3;
        float ms=world.LatencyMilliseconds;
        latencyLabel.text=stale?"SEM RESPOSTA":"PING "+Mathf.RoundToInt(ms)+" ms";
        SetLatencyColor(stale||ms>180?new Color(1f,.35f,.32f):ms>100?new Color(1f,.82f,.24f):new Color(.35f,1f,.61f));
        latencyLabel.parent.tooltip=(world.UsingUdp?"Movimento UDP":"Sincronização HTTPS")+" · Ida e volta ao servidor\nVerde até 100 ms · Amarelo até 180 ms · Vermelho acima de 180 ms";
    }
    void SetLatencyColor(Color color)
    {
        latencyLabel.style.color=color;
        latencyLabel.style.unityTextOutlineColor=color;
    }
}
