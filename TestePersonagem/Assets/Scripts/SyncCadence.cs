using System;

// Bound requests in flight instead of waiting one entire RTT before sampling the player again.
public sealed class SyncCadence
{
    public const double Period=.1;
    public const int Capacity=3;
    public int Pending {get;private set;}
    public double Interval {get;set;}=Period;
    double nextSend;
    public bool TryBegin(double now)
    {
        if(Pending>=Capacity||now<nextSend)return false;
        Pending++;nextSend=Math.Max(nextSend+Interval,now+Interval*.5);return true;
    }
    public void Complete(){Pending=Math.Max(0,Pending-1);}
    public void Backoff(double until){nextSend=Math.Max(nextSend,until);}
}
