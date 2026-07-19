using System;
using System.Collections.Generic;

[Serializable]
public class SurvivalItemDatabase
{
    public int version;
    public string description;
    public List<SurvivalItemData> items;
    public List<string> spawnAreas;
}

[Serializable]
public class SurvivalItemData
{
    public string itemId;
    public string itemName;
    public string category;
    public string categoryLabel;

    [UnityEngine.Tooltip("아이템 1개의 무게입니다. 단위는 g입니다.")]
    public int weightGrams;

    public int importance;
    public string duplicateGroup;
    public List<string> usageContexts;
    public bool isTrap;
    public List<string> allowedSpawnAreas;
}
