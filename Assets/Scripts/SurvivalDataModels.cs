using System;
using System.Collections.Generic;

/// <summary>
/// survival_items.json의 최상위 구조입니다.
/// </summary>
[Serializable]
public class SurvivalItemDatabase
{
    public int version;
    public string description;
    public List<SurvivalItemData> items;
    public List<string> spawnAreas;
}

/// <summary>
/// survival_items.json의 개별 아이템 데이터입니다.
/// JSON 필드명과 동일하게 작성되어 있습니다.
/// </summary>
[Serializable]
public class SurvivalItemData
{
    public string itemId;
    public string itemName;

    public string category;
    public string categoryLabel;

    /// <summary>
    /// 아이템 1개의 무게입니다. 단위는 kg입니다.
    /// </summary>
    public float weight;

    public int importance;
    public string duplicateGroup;
    public List<string> usageContexts;
    public bool isTrap;
    public List<string> allowedSpawnAreas;
}