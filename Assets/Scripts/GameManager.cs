using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 시작, 타이머, 가방 아이템, 행동 로그, 최종 점수를 관리합니다.
/// 씬의 GameManager 오브젝트에 붙이세요.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("게임 설정")]
    [SerializeField] private string scenarioId = "Earthquake";
    [SerializeField] private float gameDurationSeconds = 60f;
    [SerializeField] private bool startOnPlay;

    [Header("플레이어 정보")]
    [SerializeField] private string playerGender = "Female";
    [SerializeField] private int playerAge = 20;

    [Tooltip("성별·나이 규칙으로 계산된 최종 가방 허용 무게를 넣으세요.")]
    [SerializeField] private float maxWeight = 8f;

    [Header("데이터")]
    [SerializeField] private SurvivalDataLoader dataLoader;

    public bool IsGameRunning { get; private set; }
    public bool IsGameEnded { get; private set; }

    public float RemainingTime { get; private set; }
    public float ElapsedTime { get; private set; }
    public float CurrentWeight { get; private set; }

    public SurvivalScoreResult LastScoreResult { get; private set; }
    public SurvivalGameResult LastGameResult { get; private set; }

    public event Action<SurvivalGameResult> GameEnded;

    private readonly Dictionary<string, int> selectedItemQuantities =
        new Dictionary<string, int>();

    private readonly List<GameActionLog> actionLogs =
        new List<GameActionLog>();

    private readonly List<string> availableItemIds =
        new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (dataLoader == null)
        {
            dataLoader = SurvivalDataLoader.Instance;
        }

        if (startOnPlay)
        {
            StartGame();
        }
    }

    private void Update()
    {
        if (!IsGameRunning)
        {
            return;
        }

        ElapsedTime += Time.deltaTime;
        RemainingTime = Mathf.Max(
            0f,
            gameDurationSeconds - ElapsedTime
        );

        if (RemainingTime <= 0f)
        {
            EndGame();
        }
    }

    /// <summary>
    /// 게임을 새로 시작하고 이전 기록을 초기화합니다.
    /// </summary>
    public void StartGame()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        selectedItemQuantities.Clear();
        actionLogs.Clear();
        availableItemIds.Clear();

        ElapsedTime = 0f;
        RemainingTime = gameDurationSeconds;
        CurrentWeight = 0f;

        LastScoreResult = null;
        LastGameResult = null;

        IsGameEnded = false;
        IsGameRunning = true;

        Debug.Log("[GameManager] 게임 시작");
    }

    /// <summary>
    /// 플레이어 정보 화면에서 호출합니다.
    /// maxWeight는 성별·나이 규칙으로 계산한 값을 전달하세요.
    /// </summary>
    public void SetPlayerProfile(
        string gender,
        int age,
        float calculatedMaxWeight)
    {
        playerGender = gender;
        playerAge = age;
        maxWeight = Mathf.Max(0f, calculatedMaxWeight);
    }

    public void SetScenario(string newScenarioId)
    {
        if (string.IsNullOrWhiteSpace(newScenarioId))
        {
            Debug.LogWarning(
                "[GameManager] 시나리오 ID가 비어 있습니다."
            );
            return;
        }

        scenarioId = newScenarioId;
    }

    /// <summary>
    /// 랜덤 스포너가 실제 생성한 아이템을 등록합니다.
    /// 모든 아이템이 등장하는 게임이라면 호출하지 않아도 됩니다.
    /// </summary>
    public void RegisterAvailableItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        if (!availableItemIds.Contains(itemId))
        {
            availableItemIds.Add(itemId);
        }
    }

    /// <summary>
    /// 아이템을 가방에 넣었을 때 호출합니다.
    /// 성공하면 true를 반환합니다.
    /// </summary>
    public bool AddItemToBag(string itemId)
    {
        if (!IsGameRunning)
        {
            Debug.LogWarning(
                "[GameManager] 게임 진행 중이 아니라 아이템을 추가할 수 없습니다."
            );
            return false;
        }

        SurvivalItemData item = GetItemOrLogError(itemId);

        if (item == null)
        {
            return false;
        }

        if (!selectedItemQuantities.ContainsKey(itemId))
        {
            selectedItemQuantities.Add(itemId, 0);
        }

        selectedItemQuantities[itemId]++;

        RecalculateCurrentWeight();

        AddActionLog(
            "ADD",
            item,
            selectedItemQuantities[itemId]
        );

        return true;
    }

    /// <summary>
    /// 아이템을 가방에서 뺐을 때 호출합니다.
    /// 성공하면 true를 반환합니다.
    /// </summary>
    public bool RemoveItemFromBag(string itemId)
    {
        if (!IsGameRunning)
        {
            Debug.LogWarning(
                "[GameManager] 게임 진행 중이 아니라 아이템을 제거할 수 없습니다."
            );
            return false;
        }

        SurvivalItemData item = GetItemOrLogError(itemId);

        if (item == null)
        {
            return false;
        }

        int currentQuantity;

        if (!selectedItemQuantities.TryGetValue(
                itemId,
                out currentQuantity
            ) ||
            currentQuantity <= 0)
        {
            Debug.LogWarning(
                $"[GameManager] 가방에 없는 아이템입니다: {itemId}"
            );
            return false;
        }

        currentQuantity--;

        if (currentQuantity == 0)
        {
            selectedItemQuantities.Remove(itemId);
        }
        else
        {
            selectedItemQuantities[itemId] = currentQuantity;
        }

        RecalculateCurrentWeight();

        AddActionLog(
            "REMOVE",
            item,
            currentQuantity
        );

        return true;
    }

    public int GetSelectedQuantity(string itemId)
    {
        int quantity;

        return selectedItemQuantities.TryGetValue(
            itemId,
            out quantity
        )
            ? quantity
            : 0;
    }

    /// <summary>
    /// 제한 시간 종료 또는 완료 버튼에서 호출합니다.
    /// </summary>
    public void EndGame()
    {
        if (IsGameEnded)
        {
            return;
        }

        if (!EnsureDataLoader())
        {
            return;
        }

        IsGameRunning = false;
        IsGameEnded = true;

        RecalculateCurrentWeight();
        CalculateFinalScore();

        LastGameResult = BuildGameResult();

        Debug.Log(
            "[GameManager] 게임 종료 결과\n" +
            JsonUtility.ToJson(LastGameResult, true)
        );

        GameEnded?.Invoke(LastGameResult);
    }

    public string GetLastGameResultJson()
    {
        return LastGameResult == null
            ? string.Empty
            : JsonUtility.ToJson(LastGameResult, true);
    }

    private void CalculateFinalScore()
    {
        SurvivalScoreInput input = new SurvivalScoreInput
        {
            disaster = scenarioId,
            maxWeight = maxWeight,
            selectedItems = BuildScoreSelectedItems(),
            availableItemIds =
                new List<string>(availableItemIds)
        };

        LastScoreResult = SurvivalScoreCalculator.Evaluate(
            input,
            dataLoader.ItemDatabase,
            dataLoader.ScoreRules
        );
    }

    private List<ScoreSelectedItem> BuildScoreSelectedItems()
    {
        List<ScoreSelectedItem> result =
            new List<ScoreSelectedItem>();

        foreach (
            KeyValuePair<string, int> selected
            in selectedItemQuantities
        )
        {
            result.Add(new ScoreSelectedItem
            {
                itemId = selected.Key,
                quantity = selected.Value
            });
        }

        return result;
    }

    private List<SelectedItemResult> BuildSelectedItemResults()
    {
        List<SelectedItemResult> result =
            new List<SelectedItemResult>();

        foreach (
            KeyValuePair<string, int> selected
            in selectedItemQuantities
        )
        {
            SurvivalItemData item =
                dataLoader.GetItemById(selected.Key);

            if (item == null)
            {
                continue;
            }

            result.Add(new SelectedItemResult
            {
                itemId = item.itemId,
                itemName = item.itemName,
                quantity = selected.Value,
                unitWeight = RoundToTwoDecimals(item.weight),
                totalWeight = RoundToTwoDecimals(
                    item.weight * selected.Value
                ),
                isTrap = item.isTrap
            });
        }

        return result;
    }

    private SurvivalGameResult BuildGameResult()
    {
        return new SurvivalGameResult
        {
            scenarioId = scenarioId,

            playerGender = playerGender,
            playerAge = playerAge,

            elapsedTime = RoundToTwoDecimals(ElapsedTime),
            totalWeight = RoundToTwoDecimals(CurrentWeight),
            maxWeight = RoundToTwoDecimals(maxWeight),

            selectedItems = BuildSelectedItemResults(),
            actionLogs = new List<GameActionLog>(actionLogs),
            availableItemIds =
                new List<string>(availableItemIds),

            scoreResult = LastScoreResult
        };
    }

    private void RecalculateCurrentWeight()
    {
        float totalWeight = 0f;

        foreach (
            KeyValuePair<string, int> selected
            in selectedItemQuantities
        )
        {
            SurvivalItemData item =
                dataLoader.GetItemById(selected.Key);

            if (item == null)
            {
                continue;
            }

            totalWeight += item.weight * selected.Value;
        }

        CurrentWeight = RoundToTwoDecimals(totalWeight);
    }

    private void AddActionLog(
        string action,
        SurvivalItemData item,
        int quantityAfterAction)
    {
        actionLogs.Add(new GameActionLog
        {
            elapsedTime = RoundToTwoDecimals(ElapsedTime),
            action = action,
            itemId = item.itemId,
            itemName = item.itemName,
            quantityAfterAction = quantityAfterAction,
            currentWeight = RoundToTwoDecimals(CurrentWeight)
        });
    }

    private SurvivalItemData GetItemOrLogError(string itemId)
    {
        if (!EnsureDataLoader())
        {
            return null;
        }

        SurvivalItemData item =
            dataLoader.GetItemById(itemId);

        if (item == null)
        {
            Debug.LogError(
                $"[GameManager] JSON에서 itemId를 찾을 수 없습니다: " +
                $"{itemId}"
            );
        }

        return item;
    }

    private bool EnsureDataLoader()
    {
        if (dataLoader == null)
        {
            dataLoader = SurvivalDataLoader.Instance;
        }

        if (dataLoader == null)
        {
            Debug.LogError(
                "[GameManager] SurvivalDataLoader가 씬에 없습니다."
            );
            return false;
        }

        if (!dataLoader.IsLoaded)
        {
            Debug.LogError(
                "[GameManager] JSON 데이터가 로드되지 않았습니다."
            );
            return false;
        }

        return true;
    }

    private static float RoundToTwoDecimals(float value)
    {
        return Mathf.Round(value * 100f) / 100f;
    }

#if UNITY_EDITOR

    [ContextMenu("점수 시스템 테스트")]
    private void TestScoreSystem()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        selectedItemQuantities.Clear();
        actionLogs.Clear();
        availableItemIds.Clear();

        ElapsedTime = 20f;
        IsGameRunning = true;
        IsGameEnded = false;

        AddItemToBag("water");
        AddItemToBag("energy_bar");
        AddItemToBag("first_aid_kit");
        AddItemToBag("flashlight");
        AddItemToBag("game_console");

        EndGame();
    }

#endif
}
