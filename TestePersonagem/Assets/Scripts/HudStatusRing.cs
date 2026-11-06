using UnityEngine;
using UnityEngine.UIElements;

// A status tile filled from the bottom as the player's level increases.
public sealed class HudStatusRing : VisualElement
{
    readonly VisualElement fill;
    float percent=100;
    public float Value=>percent;
    public HudStatusRing(Color color)
    {
        pickingMode=PickingMode.Ignore;
        AddToClassList("status-ring");
        fill=new VisualElement {pickingMode=PickingMode.Ignore};
        fill.style.position=Position.Absolute;
        fill.style.left=0;
        fill.style.right=0;
        fill.style.bottom=0;
        fill.style.height=Length.Percent(percent);
        fill.style.backgroundColor=color;
        Add(fill);
    }
    public void SetValue(float value)
    {
        value=Mathf.Clamp(value,0,100);
        if(Mathf.Abs(value-percent)<.05f)return;
        percent=value;
        fill.style.height=Length.Percent(percent);
    }
}
