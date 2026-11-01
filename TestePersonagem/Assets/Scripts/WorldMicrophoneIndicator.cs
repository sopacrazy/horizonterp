using UnityEngine;

// Vector microphone beside the remote player's ID, independent of font glyph support.
public sealed class WorldMicrophoneIndicator : MonoBehaviour
{
    readonly System.Collections.Generic.List<LineRenderer> strokes = new System.Collections.Generic.List<LineRenderer>();
    Material material;
    public static WorldMicrophoneIndicator Create(TextMesh name)
    {
        var root = new GameObject("Microfone do jogador"); root.layer = 2;
        root.transform.SetParent(name.transform, false);
        var icon = root.AddComponent<WorldMicrophoneIndicator>();
        icon.material = new Material(Shader.Find("Sprites/Default"));
        icon.Stroke(new[] {new Vector3(-.033f,.015f),new Vector3(-.033f,.075f),new Vector3(0,.1f),new Vector3(.033f,.075f),new Vector3(.033f,.015f),new Vector3(0,-.007f),new Vector3(-.033f,.015f)});
        icon.Stroke(new[] {new Vector3(-.06f,.025f),new Vector3(-.06f,-.015f),new Vector3(0,-.045f),new Vector3(.06f,-.015f),new Vector3(.06f,.025f)});
        icon.Stroke(new[] {new Vector3(0,-.045f),new Vector3(0,-.08f),new Vector3(-.04f,-.08f),new Vector3(.04f,-.08f)});
        root.SetActive(false); return icon;
    }
    void Stroke(Vector3[] points)
    {
        for (int layer = 0; layer < 2; layer++)
        {
            var obj = new GameObject("Traço"); obj.layer = 2; obj.transform.SetParent(transform, false);
            var line = obj.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.sharedMaterial = material;
            line.positionCount = points.Length; line.SetPositions(points); line.widthMultiplier = layer == 0 ? .026f : .013f;
            line.numCapVertices = 3; line.numCornerVertices = 3; line.sortingOrder = layer == 0 ? 2 : 3;
            line.startColor = line.endColor = layer == 0 ? Color.black : Color.green;
            if (layer == 1) strokes.Add(line);
        }
    }
    public void Refresh(bool active, TextMesh name)
    {
        gameObject.SetActive(active);
        if (!active) return;
        transform.localPosition = new Vector3(name.GetComponent<MeshRenderer>().localBounds.max.x + .11f, 0, -.02f);
        float pulse = .6f + .4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5));
        var color = new Color(.12f * pulse, pulse, .4f * pulse);
        foreach (var line in strokes) line.startColor = line.endColor = color;
    }
    void OnDestroy() { if (material) Destroy(material); }
}
