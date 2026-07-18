using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// survival_items.json을 로드하고 itemId로 아이템을 조회할 수 있게 해주는 로더입니다.
///
/// 씬의 빈 오브젝트에 붙인 뒤 Inspector의 Survival Items Json에
/// survival_items.json 파일을 연결하세요.
/// </summary>
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

    /// <summary>
    /// Inspector에 연결된 survival_items.json을 다시 로드합니다.
    /// </summary>
    public void LoadData()
    {
        IsLoaded = false;
        ItemDatabase = null;
        itemById.Clear();

        if (survivalItemsJson == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] Survival Items Json이 연결되지 않았습니다."
            );
            return;
        }

        try
        {
            ItemDatabase =
                JsonUtility.FromJson<SurvivalItemDatabase>(
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

        if (ItemDatabase == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] JSON 파싱 결과가 null입니다."
            );
            return;
        }

        if (ItemDatabase.items == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] JSON에 items 배열이 없습니다."
            );
            return;
        }

        foreach (SurvivalItemData item in ItemDatabase.items)
        {
            if (item == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.itemId))
            {
                Debug.LogWarning(
                    "[SurvivalDataLoader] itemId가 비어 있는 아이템을 건너뜁니다."
                );
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

        Debug.Log(
            $"[SurvivalDataLoader] 아이템 {itemById.Count}개 로드 완료"
        );
    }

    /// <summary>
    /// itemId와 일치하는 아이템을 반환합니다.
    /// 찾지 못하면 null을 반환합니다.
    /// </summary>
    public SurvivalItemData GetItemById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        return itemById.TryGetValue(
            itemId,
            out SurvivalItemData item
        )
            ? item
            : null;
    }

    /// <summary>
    /// 특정 itemId가 현재 로드된 데이터에 존재하는지 확인합니다.
    /// </summary>
    public bool ContainsItem(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               itemById.ContainsKey(itemId);
    }

#if UNITY_EDITOR

    [ContextMenu("Reload Survival Item Data")]
    private void ReloadFromContextMenu()
    {
        LoadData();
    }

#endif
}