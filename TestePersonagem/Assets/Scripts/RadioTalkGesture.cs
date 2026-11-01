// One pointer owns a press. Two short taps latch transmission; another press releases it.
public sealed class RadioTalkGesture
{
    public bool Active => Latched || pointer >= 0 && !releaseOnly;
    public bool Latched { get; private set; }
    public int Pointer => pointer;
    int pointer = -1;
    bool releaseOnly;
    double downAt, lastTap = double.NegativeInfinity;
    const double TapSeconds = .28, DoubleTapSeconds = .36;
    public bool Down(int id, double now)
    {
        if (pointer >= 0) return false;
        pointer = id; downAt = now; releaseOnly = Latched;
        if (Latched) { Latched = false; lastTap = double.NegativeInfinity; }
        else if (now - lastTap <= DoubleTapSeconds) { Latched = true; lastTap = double.NegativeInfinity; }
        return true;
    }
    public bool Up(int id, double now, bool canceled = false)
    {
        if (pointer != id) return false;
        if (!Latched && !releaseOnly && !canceled && now - downAt <= TapSeconds) lastTap = now;
        else lastTap = double.NegativeInfinity;
        pointer = -1; releaseOnly = false;
        if (canceled) Latched = false;
        return true;
    }
    public void Cancel() { pointer = -1; Latched = false; releaseOnly = false; lastTap = double.NegativeInfinity; }
}
