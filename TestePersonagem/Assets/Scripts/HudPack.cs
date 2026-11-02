using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Tabler Icons v3.42.0, MIT. Sources and license: Assets/ThirdParty/TablerIcons.
public static class HudPack
{
    static readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
    static Font labelFont;
    public static Font LabelFont=>labelFont?labelFont:(labelFont=Resources.Load<Font>("HudMinimal/Barlow-SemiBold")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
    public static Texture2D Texture(string name)
    {
        name=Alias(name);
        if(!textures.TryGetValue(name,out var texture))
        {texture=Resources.Load<Texture2D>("HudMinimal/"+name);textures[name]=texture;}
        return texture;
    }
    public static string Alias(string name)
    {
        switch(name)
        {
            case "menu":return "home";
            case "wallet":return "cash";
            case "list":case "mission":return "missions";
            case "person":return "user";
            case "location":return "map";
            case "reset":return "home";
            default:return name;
        }
    }
    public static Image Image(string name)
    {
        var image=new Image {image=Texture(name),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
        image.AddToClassList("hud-pack-image");return image;
    }
    public static void Skin(VisualElement element,float radius=14)
    {
        element.style.backgroundColor=new Color(.035f,.047f,.063f,.76f);
        element.style.borderTopLeftRadius=element.style.borderTopRightRadius=
            element.style.borderBottomLeftRadius=element.style.borderBottomRightRadius=radius;
        element.style.borderTopWidth=element.style.borderBottomWidth=
            element.style.borderLeftWidth=element.style.borderRightWidth=1;
        var border=new Color(.8f,.87f,.92f,.24f);
        element.style.borderTopColor=element.style.borderBottomColor=
            element.style.borderLeftColor=element.style.borderRightColor=border;
    }
}
