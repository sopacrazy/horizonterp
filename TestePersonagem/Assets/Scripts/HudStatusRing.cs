using UnityEngine;
using UnityEngine.UIElements;

// A progress ring around the repository icon; status remains driven by player data.
public sealed class HudStatusRing : VisualElement
{
    readonly Color accent;
    float percent=100;
    public HudStatusRing(Color color)
    {
        accent=color;pickingMode=PickingMode.Ignore;AddToClassList("status-ring");
        generateVisualContent+=Draw;
    }
    public void SetValue(float value)
    {
        value=Mathf.Clamp(value,0,100);
        if(Mathf.Abs(value-percent)<.05f)return;
        percent=value;MarkDirtyRepaint();
    }
    void Draw(MeshGenerationContext context)
    {
        float size=Mathf.Min(contentRect.width,contentRect.height);
        if(size<8)return;
        var center=contentRect.center;
        float radius=size*.5f-6;
        var painter=context.painter2D;
        // Fill and progress share this center instead of separate CSS background geometry.
        painter.fillColor=new Color(.025f,.04f,.06f,.92f);
        painter.BeginPath();
        for(int i=0;i<=80;i++)
        {
            float angle=i/80f*Mathf.PI*2;
            var point=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(radius+3f);
            if(i==0)painter.MoveTo(point);else painter.LineTo(point);
        }
        painter.ClosePath();painter.Fill();
        painter.lineWidth=6;
        void Arc(float fraction,Color color)
        {
            if(fraction<=0)return;
            painter.strokeColor=color;painter.BeginPath();
            int count=Mathf.Max(2,Mathf.CeilToInt(80*fraction));
            for(int i=0;i<=count;i++)
            {
                float angle=(-90+360*fraction*i/count)*Mathf.Deg2Rad;
                var point=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                if(i==0)painter.MoveTo(point);else painter.LineTo(point);
            }
            painter.Stroke();
        }
        Arc(1,new Color(.75f,.83f,.9f,.18f));Arc(percent/100,accent);
    }
}
