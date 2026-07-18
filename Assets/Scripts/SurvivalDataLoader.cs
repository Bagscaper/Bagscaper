using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아이템 JSON과 점수 규칙 JSON을 읽어 C# 데이터로 변환합니다.
/// 이 스크립트만 빈 GameObject에 붙이면 됩니다.
/// </summary>
public class SurvivalDataLoader : MonoBehaviour
{
    public static SurvivalDataLoader Instance { get; private set; }

    [Header("JSON 파일")]
    [SerializeField] private TextAsset itemDatabaseJson;
    [SerializeField] private TextAsset scoreRulesJson;

    public SurvivalItemDatabase ItemDatabase { get; private set; }
    public SurvivalScoreRuleDatabase ScoreRules { get; private set; }

    public bool IsLoaded =>
        ItemDatabase != null &&
        ItemDatabase.items != null &&
        ScoreRules != null &&
        ScoreRules.itemRules != null;

    private Dictionary<string, SurvivalItemData> itemMap;

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
        if (itemDatabaseJson == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] survival_items.json이 연결되지 않았습니다."
            );
            return;
        }

        if (scoreRulesJson == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] survival_score_rules.json이 연결되지 않았습니다."
            );
            return;
        }

        ItemDatabase = JsonUtility.FromJson<SurvivalItemDatabase>(
            itemDatabaseJson.text
        );

        ScoreRules = JsonUtility.FromJson<SurvivalScoreRuleDatabase>(
            scoreRulesJson.text
        );

        if (ItemDatabase == null || ItemDatabase.items == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] 아이템 JSON을 읽지 못했습니다."
            );
            return;
        }

        if (ScoreRules == null || ScoreRules.itemRules == null)
        {
            Debug.LogError(
                "[SurvivalDataLoader] 점수 규칙 JSON을 읽지 못했습니다."
            );
            return;
        }

        BuildItemMap();
        ValidateData();

        Debug.Log(
            $"[SurvivalDataLoader] 아이템 {ItemDatabase.items.Count}개, " +
            $"점수 규칙 {ScoreRules.itemRules.Count}개 로드 완료"
        );
    }

    public SurvivalItemData GetItemById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || itemMap == null)
        {
            return null;
        }

        SurvivalItemData item;
        return itemMap.TryGetValue(itemId, out item) ? item : null;
    }

    public bool ContainsItem(string itemId)
    {
        return GetItemById(itemId) != null;
    }

    private void BuildItemMap()
    {
        itemMap = new Dictionary<string, SurvivalItemData>();

        foreach (SurvivalItemData item in ItemDatabase.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            if (itemMap.ContainsKey(item.itemId))
            {
                Debug.LogWarning(
                    $"[SurvivalDataLoader] 중복 itemId: {item.itemId}"
                );
                continue;
            }

            itemMap.Add(item.itemId, item);
        }
    }

    private void ValidateData()
    {
        int scoreGroupTotal = 0;

        if (ScoreRules.scoreGroups != null)
        {
            foreach (ScoreGroupRule group in ScoreRules.scoreGroups)
            {
                scoreGroupTotal += group.maxScore;
            }
        }

        if (scoreGroupTotal != 100)
        {
            Debug.LogWarning(
                $"[SurvivalDataLoader] 점수 그룹의 합이 100점이 아닙니다: " +
                $"{scoreGroupTotal}"
            );
        }

        foreach (ItemScoreRule rule in ScoreRules.itemRules)
        {
            if (!ContainsItem(rule.itemId))
            {
                Debug.LogWarning(
                    $"[SurvivalDataLoader] 아이템 JSON에 없는 점수 규칙 ID: " +
                    $"{rule.itemId}"
                );
            }
        }
    }
}
