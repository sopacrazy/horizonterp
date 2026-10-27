using System;
using UnityEngine;

[Serializable]
public sealed class MissionDefinition
{
    public string code,category,title,npc,dialog,requires;
    public int order,rewardXp,rewardCashCents;
    public bool implemented;
    public string[] objectives;
}

[Serializable]
public sealed class MissionCatalogData
{
    public MissionDefinition[] missions;
    public MissionDefinition[] daily;
    public MissionDefinition[] weekly;
    public string tutorialClosing;
}

public static class MissionCatalog
{
    static MissionCatalogData data;
    public static MissionCatalogData Data
    {
        get
        {
            if(data!=null)return data;
            var asset=Resources.Load<TextAsset>("Missions/Catalog");
            if(!asset)throw new InvalidOperationException("Catálogo de missões não encontrado.");
            data=JsonUtility.FromJson<MissionCatalogData>(asset.text);
            if(data?.missions==null||data.missions.Length!=17)
                throw new InvalidOperationException("Catálogo de missões incompleto.");
            return data;
        }
    }
}
