using System;
using UnityEngine;

[Serializable] public sealed class GameVersion
{
    public string version,packageId,updateManifestUrl;
    public int versionCode;
    public static GameVersion Current=>JsonUtility.FromJson<GameVersion>(Resources.Load<TextAsset>("GameVersion").text);
}
