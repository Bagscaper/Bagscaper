using System.Collections.Generic;
using UnityEngine;

public class SurvivalDataLoader : MonoBehaviour
{
    public static SurvivalDataLoader Instance { get; private set; }

    [Header("아이템 데이터")]
    [SerializeField] private TextAsset survivalItemsJson;

    public bool IsLoaded { get; private set; }
    public SurvivalItemDatabase ItemDatabase { get; private set; }

    private readonly Dictionary<string, SurvivalItemData> itemById =
        new Dictionary<string, SurvivalItemData>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadData();
    }

    public void LoadData()
    {
        IsLoaded = false;
        ItemDatabase = null;
        itemById.Clear();

        if (survivalItemsJson == null)
        {
            Debug.LogError("[SurvivalDataLoader] Survival Items Json이 연결되지 않았습니다.");
            return;
        }

        try
        {
            ItemDatabase = JsonUtility.FromJson<SurvivalItemDatabase>(
                survivalItemsJson.text
            );
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "[SurvivalDataLoader] JSON 파싱 중 오류가 발생했습니다.\n" +
                exception
            );
            return;
        }

        if (ItemDatabase == null || ItemDatabase.items == null)
        {
            Debug.LogError("[SurvivalDataLoader] JSON에 items 배열이 없습니다.");
            return;
        }

        foreach (SurvivalItemData item in ItemDatabase.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            if (itemById.ContainsKey(item.itemId))
            {
                Debug.LogWarning(
                    $"[SurvivalDataLoader] 중복 itemId를 건너뜁니다: {item.itemId}"
                );
                continue;
            }

            itemById.Add(item.itemId, item);
        }

        IsLoaded = true;
        Debug.Log($"[SurvivalDataLoader] 아이템 {itemById.Count}개 로드 완료");
    }

    public SurvivalItemData GetItemById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        return itemById.TryGetValue(itemId, out SurvivalItemData item)
            ? item
            : null;
    }

    public bool ContainsItem(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && itemById.ContainsKey(itemId);
    }

#if UNITY_EDITOR
    [ContextMenu("Reload Survival Item Data")]
    private void ReloadFromContextMenu()
    {
        LoadData();
    }
#endif
}
