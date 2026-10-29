using UnityEngine;

// Original placement metadata visible when editing a generated interior object.
public sealed class GtaSaInteriorSource : MonoBehaviour
{
    public int sourceModelId, rawInterior, canonicalInterior, sourceIndex;
    public string sourceIpl, modelName;
    public Vector3 gtaPosition;
    public Quaternion gtaRotation;
}
