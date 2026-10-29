using System;
using UnityEngine;

// Test-only sample points stored with each editable terrain repair sector.
public sealed class GtaSaTerrainPatchMarker : MonoBehaviour
{
    public string patchName;
    public Vector3[] localSamples = Array.Empty<Vector3>();
}
