using UnityEngine;
using UnityEngine.UIElements;

// Small vector icons stay crisp at any HUD scale, without external image dependencies.
public sealed class HudIcon : VisualElement
{
    public string Kind { get; private set; }
    public Color Ink = new Color(.9f, .96f, .98f);
    public HudIcon(string kind)
    {
        Kind=kind; AddToClassList("icon"); pickingMode=PickingMode.Ignore;
        generateVisualContent += Draw;
    }
    void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;
        p.lineWidth=1.7f;p.strokeColor=Ink;p.fillColor=Ink;
        float size=Mathf.Min(contentRect.width,contentRect.height);
        Vector2 Point(float x,float y)=>new Vector2((contentRect.width-size)/2+x/24f*size,(contentRect.height-size)/2+y/24f*size);
        void Line(params float[] xy)
        {
            p.BeginPath();p.MoveTo(Point(xy[0],xy[1]));
            for(int i=2;i<xy.Length;i+=2)p.LineTo(Point(xy[i],xy[i+1]));
            p.Stroke();
        }
        void Circle(float x,float y,float r)
        {
            p.BeginPath();p.MoveTo(Point(x+r,y));
            for(int i=1;i<=24;i++){float a=i/24f*Mathf.PI*2;p.LineTo(Point(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r));}
            p.Stroke();
        }
        void Box(float x,float y,float w,float h)=>Line(x,y,x+w,y,x+w,y+h,x,y+h,x,y);
        switch(Kind)
        {
            case "admin":
                p.BeginPath();p.MoveTo(Point(3,7));p.LineTo(Point(7,11));p.LineTo(Point(12,3));
                p.LineTo(Point(17,11));p.LineTo(Point(21,7));p.LineTo(Point(19,18));p.LineTo(Point(5,18));p.ClosePath();p.Fill();p.Stroke();
                Line(5,21,19,21);break;
            case "menu": Box(3,3,7,7);Box(14,3,7,7);Box(3,14,7,7);Box(14,14,7,7);break;
            case "bag": Box(4,8,16,13);Line(8,8,8,5,10,3,14,3,16,5,16,8);Line(8,12,8,15);Line(16,12,16,15);break;
            case "wallet": Box(3,6,18,14);Line(3,10,21,10);Box(14,12,7,5);Circle(17,14.5f,.5f);break;
            case "phone": Box(7,2,10,20);Line(10,5,14,5);Circle(12,19,.6f);break;
            case "list": Box(4,3,16,18);Line(8,8,17,8);Line(8,12,17,12);Line(8,16,15,16);break;
            case "person": Circle(12,7,3.5f);Line(4,21,4,18,7,14,17,14,20,18,20,21);break;
            case "map": Line(3,5,9,3,15,6,21,3,21,19,15,22,9,19,3,21,3,5);Line(9,3,9,19);Line(15,6,15,22);break;
            case "run": Circle(16,4,2);Line(4,10,9,8,14,10,17,14,21,14);Line(14,10,11,15,15,20,18,21);Line(11,15,7,20,3,20);break;
            case "jump": Circle(12,4,2);Line(4,4,7,9,12,10,17,9,20,4);Line(12,10,12,15,7,19,3,17);Line(12,15,17,19,21,17);break;
            case "camera": Box(2,7,20,14);Line(7,7,9,3,15,3,17,7);Circle(12,14,4);break;
            case "reset": Line(5,8,5,3,1,7,5,8,9,8);Line(5,5,14,3,20,7,22,13,19,19,13,22,7,20);break;
            case "settings": Circle(12,12,4);Circle(12,12,8);Line(12,1,12,4);Line(12,20,12,23);Line(1,12,4,12);Line(20,12,23,12);break;
            case "help": Circle(12,12,9);Line(8,8,10,5,14,5,17,8,16,11,12,14,12,16);Circle(12,19,.4f);break;
            case "close": Line(5,5,19,19);Line(19,5,5,19);break;
            case "location": Circle(12,10,6);Line(6,11,12,22,18,11);Circle(12,10,1.5f);break;
            case "arrow":
                p.BeginPath();p.MoveTo(Point(12,2));p.LineTo(Point(21,21));p.LineTo(Point(12,17));p.LineTo(Point(3,21));p.ClosePath();p.Fill();break;
        }
    }
    public void SetInk(Color color){Ink=color;MarkDirtyRepaint();}
}
