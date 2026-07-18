using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 로컬 상태와 서버에서 전달받은 세션 정보를 관리합니다.
///
/// API 통신 코드는 GameApiClient에 있습니다. GameManager는 API를 직접 호출하지 않고
/// 아래 이벤트만 발생시킵니다. GameApiClient가 이 이벤트들을 구독해서 실제 HTTP 요청을
/// 보내고, 응답이 오면 이 클래스의 Apply* 메서드를 다시 호출해 상태를 반영합니다.
///
/// - 게임 시작 요청: GameStartRequested → 성공 시 ApplyStartSession(...)
/// - 가방 로그 요청: BagLogRequested → 성공 시 ApplyServerBagState(...)
/// - 최종 결과 요청: GameResultRequested → 성공 시 ApplyFinalResult(...)
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // API 명세서 2. 열거형 정의와 반드시 일치해야 합니다.
    private static readonly string[] ValidGenders =
        { "male", "female", "other" };

    private static readonly string[] ValidAgeGroups =
        { "child", "teen", "age_20_30", "age_40_50", "age_60_plus" };

    private static readonly string[] ValidDisasters =
        {
            "fire", "flood", "typhoon", "wildfire",
            "earthquake", "heatwave", "coldwave"
        };

    [Header("게임 설정")]
    [SerializeField] private string disaster = "earthquake";
    [SerializeField] private float gameDurationSeconds = 60f;
    [SerializeField] private bool startOnPlay;

    [Header("플레이어 정보")]
    [SerializeField] private string playerGender = "female";
    [SerializeField] private string playerAgeGroup = "age_20_30";

    [Header("데이터")]
    [SerializeField] private SurvivalDataLoader dataLoader;

    public bool IsGameRunning { get; private set; }
    public bool IsGameEnded { get; private set; }
    public bool HasSession => !string.IsNullOrWhiteSpace(SessionId);

    public float RemainingTime { get; private set; }
    public float ElapsedTime { get; private set; }

    // /game/start 응답으로 저장할 값
    public string SessionId { get; private set; }
    public int ReferenceBmrKcalDay { get; private set; }
    public int RequiredWater72hMl { get; private set; }
    public float MaxCarryWeightKg { get; private set; }
    public string SessionExpiresAt { get; private set; }

    // 현재 가방 상태 (서버 기준으로 동기화됨)
    public int CurrentItemCount { get; private set; }
    public float CurrentWeightKg { get; private set; }

    // /game/result 응답으로 저장할 값
    public string SurvivalType { get; private set; }
    public string EvaluationNarrative { get; private set; }
    public int SurvivalTimeHours { get; private set; }
    public bool HasFinalResult { get; private set; }

    public event Action GameStarted;
    public event Action BagStateChanged;
    public event Action GameEnded;
    public event Action<string> GameResultRequested;
    public event Action FinalResultReceived;

    // GameApiClient가 구독해서 실제 HTTP 요청을 보내는 이벤트들
    public event Action<GameStartRequest> GameStartRequested;
    public event Action<GameLogRequest> BagLogRequested;

    private readonly Dictionary<string, int> selectedItemQuantities =
        new Dictionary<string, int>();

    private readonly List<LocalGameActionLog> actionLogs =
        new List<LocalGameActionLog>();

    private readonly List<string> availableItemIds =
        new List<string>();

    private LocalGameStateSnapshot lastLocalSnapshot;

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

        // API 연결 없이 에디터에서 바로 테스트하고 싶을 때만 켜세요.
        // 실제 연동에서는 UI에서 RequestGameStart()를 호출해야 합니다.
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
    /// 사용자 선택값을 저장합니다.
    /// API 명세에 맞는 문자열을 전달해야 합니다.
    /// 예: female, age_20_30
    /// </summary>
    public void SetPlayerProfile(
        string gender,
        string ageGroup)
    {
        if (string.IsNullOrWhiteSpace(gender))
        {
            Debug.LogWarning(
                "[GameManager] gender가 비어 있습니다."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(ageGroup))
        {
            Debug.LogWarning(
                "[GameManager] ageGroup이 비어 있습니다."
            );
            return;
        }

        string normalizedGender = gender.Trim().ToLowerInvariant();
        string normalizedAgeGroup = ageGroup.Trim().ToLowerInvariant();

        if (Array.IndexOf(ValidGenders, normalizedGender) < 0)
        {
            Debug.LogWarning(
                $"[GameManager] gender 값이 API 명세서의 열거형과 다릅니다: {normalizedGender}"
            );
        }

        if (Array.IndexOf(ValidAgeGroups, normalizedAgeGroup) < 0)
        {
            Debug.LogWarning(
                $"[GameManager] ageGroup 값이 API 명세서의 열거형과 다릅니다: {normalizedAgeGroup}"
            );
        }

        playerGender = normalizedGender;
        playerAgeGroup = normalizedAgeGroup;
    }

    /// <summary>
    /// 재난 종류를 저장합니다.
    /// 예: earthquake, wildfire
    /// </summary>
    public void SetDisaster(string newDisaster)
    {
        if (string.IsNullOrWhiteSpace(newDisaster))
        {
            Debug.LogWarning(
                "[GameManager] disaster가 비어 있습니다."
            );
            return;
        }

        string normalizedDisaster = newDisaster.Trim().ToLowerInvariant();

        if (Array.IndexOf(ValidDisasters, normalizedDisaster) < 0)
        {
            Debug.LogWarning(
                $"[GameManager] disaster 값이 API 명세서의 열거형과 다릅니다: {normalizedDisaster}"
            );
        }

        disaster = normalizedDisaster;
    }

    /// <summary>
    /// UI의 "게임 시작" 버튼에서 호출합니다.
    /// 로컬에서 바로 시작하지 않고 /game/start 요청을 먼저 보냅니다.
    /// GameApiClient가 GameStartRequested를 구독해 API를 호출하고,
    /// 성공하면 ApplyStartSession(...)을 호출해 실제로 게임이 시작됩니다.
    /// </summary>
    public void RequestGameStart()
    {
        GameStartRequested?.Invoke(new GameStartRequest
        {
            gender = playerGender,
            age_group = playerAgeGroup,
            disaster = disaster
        });
    }

    /// <summary>
    /// /game/start 성공 콜백에서 호출합니다.
    /// 서버가 발급한 세션 정보와 상한선 수치를 저장한 뒤 게임을 시작합니다.
    /// </summary>
    public void ApplyStartSession(
        string sessionId,
        int referenceBmrKcalDay,
        int requiredWater72hMl,
        float maxCarryWeightKg,
        string expiresAt)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogError(
                "[GameManager] 서버가 반환한 session_id가 비어 있습니다."
            );
            return;
        }

        SessionId = sessionId;
        ReferenceBmrKcalDay = referenceBmrKcalDay;
        RequiredWater72hMl = requiredWater72hMl;
        MaxCarryWeightKg = Mathf.Max(0f, maxCarryWeightKg);
        SessionExpiresAt = expiresAt;

        StartGame();
    }

    /// <summary>
    /// 게임 상태를 초기화하고 타이머를 시작합니다.
    /// 실제 연동에서는 RequestGameStart() → ApplyStartSession(...) 경로로만 호출되어야 합니다.
    /// (에디터 테스트(startOnPlay, TestLocalBagState)에서는 API 없이 직접 호출됩니다.)
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

        CurrentItemCount = 0;
        CurrentWeightKg = 0f;

        SurvivalType = string.Empty;
        EvaluationNarrative = string.Empty;
        SurvivalTimeHours = 0;
        HasFinalResult = false;

        lastLocalSnapshot = null;

        IsGameEnded = false;
        IsGameRunning = true;

        Debug.Log(
            $"[GameManager] 게임 시작 | " +
            $"session_id={SessionId} | " +
            $"disaster={disaster}"
        );

        GameStarted?.Invoke();
        BagStateChanged?.Invoke();
    }

    /// <summary>
    /// 랜덤 스포너가 실제로 생성한 아이템을 등록합니다.
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
    /// 로컬 상태를 즉시(낙관적으로) 갱신하고, /game/log INSERT 요청을 발생시킵니다.
    /// 서버 응답이 오면 ApplyServerBagState(...)가 실제 값으로 다시 동기화합니다.
    /// </summary>
    public bool AddItemToBag(string itemId)
    {
        if (!CanModifyBag())
        {
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

        RefreshLocalBagState();

        AddLocalActionLog(
            "INSERT",
            item,
            selectedItemQuantities[itemId]
        );

        BagStateChanged?.Invoke();

        RequestBagLog("INSERT", itemId);

        return true;
    }

    /// <summary>
    /// 아이템을 가방에서 뺐을 때 호출합니다.
    /// 로컬 상태를 즉시(낙관적으로) 갱신하고, /game/log REMOVE 요청을 발생시킵니다.
    /// 서버 응답이 오면 ApplyServerBagState(...)가 실제 값으로 다시 동기화합니다.
    /// </summary>
    public bool RemoveItemFromBag(string itemId)
    {
        if (!CanModifyBag())
        {
            return false;
        }

        SurvivalItemData item = GetItemOrLogError(itemId);

        if (item == null)
        {
            return false;
        }

        if (!selectedItemQuantities.TryGetValue(
                itemId,
                out int currentQuantity
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

        RefreshLocalBagState();

        AddLocalActionLog(
            "REMOVE",
            item,
            currentQuantity
        );

        BagStateChanged?.Invoke();

        RequestBagLog("REMOVE", itemId);

        return true;
    }

    public int GetSelectedQuantity(string itemId)
    {
        return selectedItemQuantities.TryGetValue(
            itemId,
            out int quantity
        )
            ? quantity
            : 0;
    }

    /// <summary>
    /// /game/log 응답을 받은 뒤 호출합니다.
    /// 서버가 내려준 아이템별 개수(items)로 selectedItemQuantities를 통째로 덮어써서
    /// 로컬 상태를 서버 기준으로 동기화합니다. 무게/총 개수는 그 결과로 다시 계산합니다.
    ///
    /// applied=false는 에러가 아니라 "이미 있는 걸 또 넣거나 없는 걸 뺀 경우"이지만,
    /// 이 경우에도 items는 서버 기준 값이므로 그대로 반영합니다.
    /// </summary>
    public void ApplyServerBagState(
        bool applied,
        int itemCount,
        int currentWeightGrams,
        List<GameLogItemQuantity> items)
    {
        if (!applied)
        {
            Debug.LogWarning(
                "[GameManager] 서버에서 가방 행동이 적용되지 않았습니다(applied=false). " +
                "서버 기준 수치로 동기화합니다."
            );
        }

        if (items != null)
        {
            selectedItemQuantities.Clear();

            foreach (GameLogItemQuantity entry in items)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.item_id) ||
                    entry.quantity <= 0)
                {
                    continue;
                }

                selectedItemQuantities[entry.item_id] = entry.quantity;
            }

            // 아이템별 개수를 기준으로 총 개수/무게를 다시 계산합니다.
            RefreshLocalBagState();
        }
        else
        {
            // 구버전 서버 호환용 폴백: items가 없으면 기존처럼 총 개수/무게만 반영합니다.
            Debug.LogWarning(
                "[GameManager] /game/log 응답에 items가 없어 총 개수/무게만 동기화합니다."
            );

            CurrentItemCount = Mathf.Max(0, itemCount);
            CurrentWeightKg = RoundToTwoDecimals(
                Mathf.Max(0, currentWeightGrams) / 1000f
            );
        }

        BagStateChanged?.Invoke();
    }

    /// <summary>
    /// 제한 시간 종료 또는 완료 버튼에서 호출합니다.
    /// 점수는 Unity에서 계산하지 않고 서버 결과를 요청합니다.
    /// </summary>
    public void EndGame()
    {
        if (IsGameEnded)
        {
            return;
        }

        IsGameRunning = false;
        IsGameEnded = true;

        RefreshLocalBagState();
        lastLocalSnapshot = BuildLocalSnapshot();

        Debug.Log(
            "[GameManager] 게임 종료 로컬 상태\n" +
            JsonUtility.ToJson(lastLocalSnapshot, true)
        );

        if (HasSession)
        {
            // GameApiClient가 이 이벤트를 구독해서 /game/result 요청을 보냅니다.
            GameResultRequested?.Invoke(SessionId);
        }
        else
        {
            Debug.LogWarning(
                "[GameManager] session_id가 없어 서버 결과를 요청할 수 없습니다."
            );
        }

        GameEnded?.Invoke();
    }

    /// <summary>
    /// /game/result 성공 콜백에서 호출합니다.
    /// </summary>
    public void ApplyFinalResult(
        string survivalType,
        string evaluationNarrative,
        int survivalTimeHours)
    {
        SurvivalType = survivalType ?? string.Empty;
        EvaluationNarrative =
            evaluationNarrative ?? string.Empty;
        SurvivalTimeHours = Mathf.Clamp(
            survivalTimeHours,
            0,
            72
        );
        HasFinalResult = true;

        Debug.Log(
            "[GameManager] 서버 최종 결과\n" +
            $"생존 유형: {SurvivalType}\n" +
            $"예상 생존 시간: {SurvivalTimeHours}시간\n" +
            $"평가: {EvaluationNarrative}"
        );

        FinalResultReceived?.Invoke();
    }

    /// <summary>
    /// API 연결 전 로컬 상태 확인용 JSON입니다.
    /// 공식 점수 결과가 아닙니다.
    /// </summary>
    public string GetLastLocalSnapshotJson()
    {
        return lastLocalSnapshot == null
            ? string.Empty
            : JsonUtility.ToJson(lastLocalSnapshot, true);
    }

    private bool CanModifyBag()
    {
        if (!IsGameRunning)
        {
            Debug.LogWarning(
                "[GameManager] 게임 진행 중이 아니라 가방을 변경할 수 없습니다."
            );
            return false;
        }

        return EnsureDataLoader();
    }

    /// <summary>
    /// /game/log 요청 이벤트를 발생시킵니다.
    /// action_id는 여기서 한 번만 생성하며, 재시도 시에도 동일한 값을 유지해야 하므로
    /// GameApiClient 쪽에서 재시도할 때는 이 메서드를 다시 호출하지 말고
    /// 넘겨받은 GameLogRequest 객체를 그대로 재사용해야 합니다.
    /// </summary>
    private void RequestBagLog(string action, string itemId)
    {
        if (!HasSession)
        {
            Debug.LogWarning(
                "[GameManager] session_id가 없어 /game/log를 요청할 수 없습니다."
            );
            return;
        }

        BagLogRequested?.Invoke(new GameLogRequest
        {
            session_id = SessionId,
            action_id = Guid.NewGuid().ToString(),
            action = action,
            item_id = itemId,
            occurred_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
    }

    private void RefreshLocalBagState()
    {
        int totalCount = 0;
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

            totalCount += selected.Value;
            totalWeight += item.weight * selected.Value;
        }

        CurrentItemCount = totalCount;
        CurrentWeightKg = RoundToTwoDecimals(totalWeight);
    }

    private void AddLocalActionLog(
        string action,
        SurvivalItemData item,
        int quantityAfterAction)
    {
        actionLogs.Add(new LocalGameActionLog
        {
            elapsed_time = RoundToTwoDecimals(ElapsedTime),
            action = action,
            item_id = item.itemId,
            item_name = item.itemName,
            quantity_after_action = quantityAfterAction,
            current_weight_kg =
                RoundToTwoDecimals(CurrentWeightKg)
        });
    }

    private LocalGameStateSnapshot BuildLocalSnapshot()
    {
        List<LocalSelectedItem> selectedItems =
            new List<LocalSelectedItem>();

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

            selectedItems.Add(new LocalSelectedItem
            {
                item_id = item.itemId,
                item_name = item.itemName,
                quantity = selected.Value,
                unit_weight_kg =
                    RoundToTwoDecimals(item.weight),
                total_weight_kg =
                    RoundToTwoDecimals(
                        item.weight * selected.Value
                    )
            });
        }

        return new LocalGameStateSnapshot
        {
            session_id = SessionId,
            gender = playerGender,
            age_group = playerAgeGroup,
            disaster = disaster,
            elapsed_time = RoundToTwoDecimals(ElapsedTime),
            current_item_count = CurrentItemCount,
            current_weight_kg =
                RoundToTwoDecimals(CurrentWeightKg),
            max_carry_weight_kg =
                RoundToTwoDecimals(MaxCarryWeightKg),
            selected_items = selectedItems,
            action_logs =
                new List<LocalGameActionLog>(actionLogs),
            available_item_ids =
                new List<string>(availableItemIds)
        };
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
                "[GameManager] survival_items.json에서 " +
                $"itemId를 찾을 수 없습니다: {itemId}"
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
                "[GameManager] 아이템 JSON 데이터가 로드되지 않았습니다."
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

    [ContextMenu("로컬 가방 상태 테스트 (API 없이)")]
    private void TestLocalBagState()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        SessionId = "editor-test-session";
        MaxCarryWeightKg = 8f;

        StartGame();

        AddItemToBag("water");
        AddItemToBag("protein_bar");
        AddItemToBag("ad_kit");
        AddItemToBag("flashlight");
        AddItemToBag("game_console");

        EndGame();
    }

#endif

    [Serializable]
    private class LocalGameStateSnapshot
    {
        public string session_id;
        public string gender;
        public string age_group;
        public string disaster;

        public float elapsed_time;
        public int current_item_count;
        public float current_weight_kg;
        public float max_carry_weight_kg;

        public List<LocalSelectedItem> selected_items;
        public List<LocalGameActionLog> action_logs;
        public List<string> available_item_ids;
    }

    [Serializable]
    private class LocalSelectedItem
    {
        public string item_id;
        public string item_name;
        public int quantity;
        public float unit_weight_kg;
        public float total_weight_kg;
    }

    [Serializable]
    private class LocalGameActionLog
    {
        public float elapsed_time;
        public string action;
        public string item_id;
        public string item_name;
        public int quantity_after_action;
        public float current_weight_kg;
    }
}