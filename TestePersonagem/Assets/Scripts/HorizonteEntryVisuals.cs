using UnityEngine;
using UnityEngine.UIElements;

internal static class HorizonteEntryVisuals
{
    internal static Texture2D Logo => logo ? logo : logo = Resources.Load<Texture2D>("Startup/RememberedLogo");
    internal static Texture2D City => city ? city : city = Resources.Load<Texture2D>("Characters/garagem");
    internal static Font DisplayFont => displayFont ? displayFont : displayFont = Resources.Load<Font>("HudMinimal/Barlow-SemiBold");
    static Texture2D logo, city;
    static Font displayFont;
    static GUIStyle heading, detail, number;

    internal static Image Image(Texture2D texture, string css, ScaleMode mode = ScaleMode.ScaleToFit)
    {
        var image = new Image { image = texture, scaleMode = mode, pickingMode = PickingMode.Ignore };
        image.AddToClassList(css);
        return image;
    }

    internal static void DrawLoading(string title, string subtitle, float progress, float opacity, bool splash = false)
    {
        var bounds = new Rect(0, 0, Screen.width, Screen.height);
        float unit = Mathf.Min(Screen.width / 1050f, Screen.height / 720f);
        GUI.color = new Color(0, 0, 0, opacity);
        GUI.DrawTexture(bounds, Texture2D.whiteTexture);
        if (!splash && City)
        {
            GUI.color = new Color(.32f, .39f, .51f, opacity);
            GUI.DrawTexture(bounds, City, ScaleMode.ScaleAndCrop);
        }
        GUI.color = new Color(1, 1, 1, opacity);
        float logoWidth = (splash ? 235 : 385) * unit;
        if (Logo) GUI.DrawTexture(new Rect(bounds.center.x - logoWidth / 2,
            bounds.height * (splash ? .5f : .44f) - logoWidth * .31f, logoWidth, logoWidth * .62f), Logo, ScaleMode.ScaleToFit);
        if (heading == null)
        {
            heading = new GUIStyle(GUI.skin.label) { font = DisplayFont, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            heading.normal.textColor = new Color(.9f, .93f, .99f);
            detail = new GUIStyle(heading) { fontStyle = FontStyle.Normal };
            detail.normal.textColor = new Color(.70f, .75f, .83f);
            number = new GUIStyle(heading) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleRight };
        }
        heading.fontSize = Mathf.Max(12, Mathf.RoundToInt(15 * unit));
        detail.fontSize = Mathf.Max(11, Mathf.RoundToInt(13 * unit));
        number.fontSize = Mathf.Max(16, Mathf.RoundToInt(29 * unit));
        if (splash)
        {
            detail.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(0, bounds.height * .91f, bounds.width, 26 * unit), title, detail);
            detail.alignment = TextAnchor.MiddleLeft;
        }
        else
        {
            float width = Mathf.Min(bounds.width * .72f, 640 * unit);
            float x = bounds.center.x - width / 2, y = bounds.height * .87f;
            GUI.Label(new Rect(x, y - 32 * unit, width, 29 * unit), title, heading);
            GUI.Label(new Rect(x, y - 40 * unit, width, 38 * unit),
                progress < 0 ? "AGUARDE" : $"{Mathf.FloorToInt(Mathf.Clamp01(progress) * 100)}%", number);
            GUI.color = new Color(.12f, .15f, .20f, opacity);
            GUI.DrawTexture(new Rect(x, y, width, 6 * unit), Texture2D.whiteTexture);
            if (progress >= 0)
            {
                GUI.color = new Color(1, .78f, .29f, opacity);
                GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(progress), 6 * unit), Texture2D.whiteTexture);
            }
            GUI.color = new Color(1, 1, 1, opacity);
            GUI.Label(new Rect(x, y + 10 * unit, width, 24 * unit), subtitle, detail);
        }
        GUI.color = Color.white;
    }
}
