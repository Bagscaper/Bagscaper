using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 게임 전체 흐름을 한 곳에서 관리합니다.
///
/// 포함 기능
/// 1. 인트로/스토리 진행
/// 2. JSON 기반 랜덤 아이템 생성
/// 3. 오른손 트리거 한 번으로 잡기, 다시 한 번으로 놓기
/// 4. 가방 진입/이탈 자동 감지
/// 5. 60초 타이머
/// 6. 선택 아이템, 무게, 행동 로그, 최종 점수 계산
///
/// 씬의 GameManager 오브젝트에 붙이세요.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameFlowState
    {
        None,
        Story,
        Playing,
        Result
    }

    [Serializable]
    public class StoryStep
    {
        [Tooltip("관리용 ID입니다. 예: intro_01")]
        public string stepId;

        [TextArea(2, 6)]
        public string message;

        [Tooltip("이 스토리 단계가 시작될 때 실행할 이벤트입니다.")]
        public UnityEvent onEnter;
    }

    [Serializable]
    public class ItemPrefabEntry
    {
        [Tooltip("survival_items.json의 itemId와 정확히 같아야 합니다.")]
        public string itemId;

        public GameObject prefab;

        [Header("스폰 보정")]
        public Vector3 spawnPositionOffset;
        public Vector3 spawnRotationOffset;

        [Header("손에 잡혔을 때 보정")]
        public Vector3 holdLocalPosition;
        public Vector3 holdLocalEulerAngles;
    }

    [Serializable]
    public class SpawnAreaEntry
    {
        [Tooltip("예: table_1, table_2, sofa, floor")]
        public string areaId;

        [Tooltip("이 오브젝트 아래의 자식 Transform을 스폰 포인트로 사용합니다.")]
        public Transform pointsRoot;
    }

    [Header("게임 설정")]
    [SerializeField] private string scenarioId = "Earthquake";
    [SerializeField, Min(1f)] private float gameDurationSeconds = 60f;
    [SerializeField] private bool startOnPlay;

    [Header("플레이어 정보")]
    [SerializeField] private string playerGender = "Female";
    [SerializeField] private int playerAge = 20;

    [Tooltip("성별·나이 규칙으로 계산된 최종 가방 허용 무게입니다.")]
    [SerializeField, Min(0f)] private float maxWeight = 8f;

    [Header("JSON 데이터")]
    [SerializeField] private SurvivalDataLoader dataLoader;

    [Header("스토리")]
    [SerializeField] private GameObject storyRoot;
    [SerializeField] private TMP_Text storyText;
    [SerializeField] private List<StoryStep> storySteps =
        new List<StoryStep>();

    [SerializeField] private UnityEvent onStoryStarted;
    [SerializeField] private UnityEvent onStoryFinished;
    [SerializeField] private UnityEvent onGameplayStarted;
    [SerializeField] private UnityEvent onGameplayEnded;

    [Header("트리거 잡기")]
    [Tooltip("보통 RightControllerAnchor 또는 그 아래 RayOrigin입니다.")]
    [SerializeField] private Transform rayOrigin;

    [Tooltip("잡은 물건이 붙을 위치입니다. RightControllerAnchor 아래 빈 오브젝트를 권장합니다.")]
    [SerializeField] private Transform holdPoint;

    [SerializeField, Min(0.1f)] private float grabDistance = 4f;
    [SerializeField] private LayerMask grabbableLayers;

    [Tooltip("오른손 검지 트리거")]
    [SerializeField] private OVRInput.RawButton triggerButton =
        OVRInput.RawButton.RIndexTrigger;

    [Header("랜덤 아이템 생성")]
    [SerializeField, Min(1)] private int spawnCount = 15;
    [SerializeField] private bool uniqueItemIds = true;
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int fixedSeed = 1234;

    [Tooltip("생성된 아이템을 묶어둘 부모입니다. 비어 있으면 GameManager 아래에 생성됩니다.")]
    [SerializeField] private Transform spawnedItemRoot;

    [SerializeField] private List<ItemPrefabEntry> itemPrefabs =
        new List<ItemPrefabEntry>();

    [SerializeField] private List<SpawnAreaEntry> spawnAreas =
        new List<SpawnAreaEntry>();

    [Tooltip("생성된 모든 아이템에 적용할 레이어 이름입니다.")]
    [SerializeField] private string grabbableLayerName =
        "GrabbableItem";

    [Header("가방 판정")]
    [Tooltip("가방 내부를 감싸는 Trigger Collider를 연결하세요.")]
    [SerializeField] private Collider bagZone;

    [Tooltip("아이템의 중심점이 가방 안에 들어오면 가방에 담긴 것으로 처리합니다.")]
    [SerializeField, Min(0.001f)] private float bagInsideTolerance =
        0.01f;

    public GameFlowState CurrentState { get; private set; }
    public bool IsGameRunning { get; private set; }
    public bool IsGameEnded { get; private set; }

    public int CurrentStoryIndex { get; private set; } = -1;
    public float RemainingTime { get; private set; }
    public float ElapsedTime { get; private set; }
    public float CurrentWeight { get; private set; }

    public SurvivalScoreResult LastScoreResult { get; private set; }
    public SurvivalGameResult LastGameResult { get; private set; }

    public event Action<int, StoryStep> StoryStepChanged;
    public event Action<SurvivalGameResult> GameEnded;

    private readonly Dictionary<string, int> selectedItemQuantities =
        new Dictionary<string, int>();

    private readonly List<GameActionLog> actionLogs =
        new List<GameActionLog>();

    private readonly List<string> availableItemIds =
        new List<string>();

    private readonly List<RuntimeWorldItem> spawnedItems =
        new List<RuntimeWorldItem>();

    private readonly Dictionary<RuntimeWorldItem, bool> bagMembership =
        new Dictionary<RuntimeWorldItem, bool>();

    private RuntimeWorldItem heldItem;

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

        if (storyRoot != null)
        {
            storyRoot.SetActive(false);
        }

        if (startOnPlay)
        {
            StartGame();
        }
    }

    private void Update()
    {
        ProcessTriggerInput();

        if (CurrentState != GameFlowState.Playing ||
            !IsGameRunning)
        {
            return;
        }

        ElapsedTime += Time.deltaTime;
        RemainingTime = Mathf.Max(
            0f,
            gameDurationSeconds - ElapsedTime
        );

        UpdateBagMembership();

        if (RemainingTime <= 0f)
        {
            EndGame();
        }
    }

    /// <summary>
    /// 전체 게임을 처음부터 시작합니다.
    /// 스토리가 있으면 스토리부터 시작하고,
    /// 스토리가 비어 있으면 바로 플레이를 시작합니다.
    /// </summary>
    public void StartGame()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        ReleaseHeldItem();
        ClearSpawnedItems();
        ResetSessionData();

        if (storySteps != null && storySteps.Count > 0)
        {
            BeginStory();
        }
        else
        {
            BeginGameplay();
        }
    }

    /// <summary>
    /// 스토리를 건너뛰고 바로 게임을 시작합니다.
    /// </summary>
    public void StartGameplayImmediately()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        ReleaseHeldItem();
        ClearSpawnedItems();
        ResetSessionData();
        BeginGameplay();
    }

    /// <summary>
    /// 현재 스토리를 한 단계 진행합니다.
    /// 트리거 입력 중 Story 상태일 때 자동 호출됩니다.
    /// </summary>
    public void AdvanceStory()
    {
        if (CurrentState != GameFlowState.Story)
        {
            return;
        }

        CurrentStoryIndex++;

        if (storySteps == null ||
            CurrentStoryIndex >= storySteps.Count)
        {
            FinishStory();
            return;
        }

        StoryStep step = storySteps[CurrentStoryIndex];

        if (storyText != null)
        {
            storyText.text = step != null
                ? step.message
                : string.Empty;
        }

        step?.onEnter?.Invoke();
        StoryStepChanged?.Invoke(CurrentStoryIndex, step);
    }

    /// <summary>
    /// 트리거가 아닌 UI 버튼에서 잡기/놓기 테스트를 할 때 호출할 수 있습니다.
    /// </summary>
    public void ToggleGrab()
    {
        if (CurrentState != GameFlowState.Playing)
        {
            return;
        }

        if (heldItem != null)
        {
            ReleaseHeldItem();
            return;
        }

        TryGrabItem();
    }

    private void BeginStory()
    {
        CurrentState = GameFlowState.Story;
        IsGameRunning = false;
        IsGameEnded = false;
        CurrentStoryIndex = -1;

        if (storyRoot != null)
        {
            storyRoot.SetActive(true);
        }

        onStoryStarted?.Invoke();
        AdvanceStory();
    }

    private void FinishStory()
    {
        if (storyRoot != null)
        {
            storyRoot.SetActive(false);
        }

        onStoryFinished?.Invoke();
        BeginGameplay();
    }

    private void BeginGameplay()
    {
        CurrentState = GameFlowState.Playing;
        IsGameRunning = true;
        IsGameEnded = false;

        ElapsedTime = 0f;
        RemainingTime = gameDurationSeconds;

        SpawnRandomItems();

        onGameplayStarted?.Invoke();

        Debug.Log(
            $"[GameManager] 게임 시작 / 시나리오: {scenarioId} / " +
            $"생성 아이템: {availableItemIds.Count}개"
        );
    }

    private void ProcessTriggerInput()
    {
        if (!OVRInput.GetDown(triggerButton))
        {
            return;
        }

        switch (CurrentState)
        {
            case GameFlowState.Story:
                AdvanceStory();
                break;

            case GameFlowState.Playing:
                ToggleGrab();
                break;
        }
    }

    private void TryGrabItem()
    {
        if (rayOrigin == null || holdPoint == null)
        {
            Debug.LogWarning(
                "[GameManager] Ray Origin 또는 Hold Point가 연결되지 않았습니다."
            );
            return;
        }

        bool didHit = Physics.Raycast(
            rayOrigin.position,
            rayOrigin.forward,
            out RaycastHit hit,
            grabDistance,
            grabbableLayers,
            QueryTriggerInteraction.Ignore
        );

        if (!didHit)
        {
            return;
        }

        RuntimeWorldItem item =
            hit.collider.GetComponentInParent<RuntimeWorldItem>();

        if (item == null || item.IsHeld)
        {
            return;
        }

        SetBagMembership(item, false);

        heldItem = item;
        heldItem.Grab(holdPoint);
    }

    private void ReleaseHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        heldItem.Drop();
        heldItem = null;
    }

    private void SpawnRandomItems()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        Dictionary<string, ItemPrefabEntry> prefabMap =
            BuildPrefabMap();

        Dictionary<string, List<Transform>> freeAreaPoints =
            BuildSpawnPointMap();

        List<SurvivalItemData> candidates =
            BuildSpawnCandidates(
                prefabMap,
                freeAreaPoints
            );

        if (candidates.Count == 0)
        {
            Debug.LogError(
                "[GameManager] 생성 가능한 아이템이 없습니다. " +
                "Item Prefabs와 Spawn Areas를 확인하세요."
            );
            return;
        }

        UnityEngine.Random.State previousState =
            UnityEngine.Random.state;

        if (useFixedSeed)
        {
            UnityEngine.Random.InitState(fixedSeed);
        }

        int createdCount = 0;

        try
        {
            while (createdCount < spawnCount &&
                   candidates.Count > 0)
            {
                int candidateIndex =
                    UnityEngine.Random.Range(
                        0,
                        candidates.Count
                    );

                SurvivalItemData itemData =
                    candidates[candidateIndex];

                List<string> validAreaIds =
                    GetValidAreaIds(
                        itemData,
                        freeAreaPoints
                    );

                if (validAreaIds.Count == 0)
                {
                    candidates.RemoveAt(candidateIndex);
                    continue;
                }

                string selectedAreaId =
                    validAreaIds[
                        UnityEngine.Random.Range(
                            0,
                            validAreaIds.Count
                        )
                    ];

                List<Transform> points =
                    freeAreaPoints[selectedAreaId];

                int pointIndex =
                    UnityEngine.Random.Range(
                        0,
                        points.Count
                    );

                Transform spawnPoint =
                    points[pointIndex];

                points.RemoveAt(pointIndex);

                SpawnOneItem(
                    itemData,
                    prefabMap[itemData.itemId],
                    spawnPoint
                );

                createdCount++;

                if (uniqueItemIds)
                {
                    candidates.RemoveAt(candidateIndex);
                }
            }
        }
        finally
        {
            if (useFixedSeed)
            {
                UnityEngine.Random.state =
                    previousState;
            }
        }

        Debug.Log(
            $"[GameManager] 랜덤 아이템 {createdCount}개 생성 완료"
        );

        if (createdCount < spawnCount)
        {
            Debug.LogWarning(
                $"[GameManager] 요청 수량은 {spawnCount}개지만 " +
                $"{createdCount}개만 생성했습니다. " +
                "스폰 포인트 또는 프리팹 매핑을 늘려주세요."
            );
        }
    }

    private void SpawnOneItem(
        SurvivalItemData itemData,
        ItemPrefabEntry prefabEntry,
        Transform spawnPoint)
    {
        Vector3 spawnPosition =
            spawnPoint.TransformPoint(
                prefabEntry.spawnPositionOffset
            );

        Quaternion spawnRotation =
            spawnPoint.rotation *
            Quaternion.Euler(
                prefabEntry.spawnRotationOffset
            );

        if (randomYaw)
        {
            spawnRotation =
                Quaternion.AngleAxis(
                    UnityEngine.Random.Range(0f, 360f),
                    Vector3.up
                ) *
                spawnRotation;
        }

        Transform parent = spawnedItemRoot != null
            ? spawnedItemRoot
            : transform;

        GameObject instance = Instantiate(
            prefabEntry.prefab,
            spawnPosition,
            spawnRotation,
            parent
        );

        instance.name =
            $"{itemData.itemId}_Spawned";

        int grabbableLayer =
            LayerMask.NameToLayer(
                grabbableLayerName
            );

        if (grabbableLayer >= 0)
        {
            SetLayerRecursively(
                instance,
                grabbableLayer
            );
        }
        else
        {
            Debug.LogWarning(
                $"[GameManager] '{grabbableLayerName}' 레이어가 없습니다."
            );
        }

        Rigidbody body =
            instance.GetComponent<Rigidbody>();

        if (body == null)
        {
            body = instance.AddComponent<Rigidbody>();
        }

        body.mass = Mathf.Max(0.01f, itemData.weight);
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation =
            RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;

        if (instance.GetComponentInChildren<Collider>() == null)
        {
            Debug.LogWarning(
                $"[GameManager] {itemData.itemId} 프리팹에 Collider가 없습니다."
            );
        }

        RuntimeWorldItem runtimeItem =
            instance.GetComponent<RuntimeWorldItem>();

        if (runtimeItem == null)
        {
            runtimeItem =
                instance.AddComponent<RuntimeWorldItem>();
        }

        runtimeItem.Initialize(
            itemData.itemId,
            body,
            prefabEntry.holdLocalPosition,
            prefabEntry.holdLocalEulerAngles
        );

        spawnedItems.Add(runtimeItem);
        bagMembership[runtimeItem] = false;

        RegisterAvailableItem(itemData.itemId);
    }

    private Dictionary<string, ItemPrefabEntry> BuildPrefabMap()
    {
        Dictionary<string, ItemPrefabEntry> result =
            new Dictionary<string, ItemPrefabEntry>();

        foreach (ItemPrefabEntry entry in itemPrefabs)
        {
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.itemId) ||
                entry.prefab == null)
            {
                continue;
            }

            if (result.ContainsKey(entry.itemId))
            {
                Debug.LogWarning(
                    $"[GameManager] 중복 프리팹 itemId: {entry.itemId}"
                );
                continue;
            }

            result.Add(entry.itemId, entry);
        }

        return result;
    }

    private Dictionary<string, List<Transform>>
        BuildSpawnPointMap()
    {
        Dictionary<string, List<Transform>> result =
            new Dictionary<string, List<Transform>>();

        foreach (SpawnAreaEntry area in spawnAreas)
        {
            if (area == null ||
                string.IsNullOrWhiteSpace(area.areaId) ||
                area.pointsRoot == null)
            {
                continue;
            }

            List<Transform> points =
                new List<Transform>();

            Transform[] children =
                area.pointsRoot.GetComponentsInChildren<Transform>(
                    true
                );

            foreach (Transform child in children)
            {
                if (child != area.pointsRoot)
                {
                    points.Add(child);
                }
            }

            if (points.Count == 0)
            {
                Debug.LogWarning(
                    $"[GameManager] {area.areaId} 아래에 스폰 포인트가 없습니다."
                );
            }

            result[area.areaId] = points;
        }

        return result;
    }

    private List<SurvivalItemData> BuildSpawnCandidates(
        Dictionary<string, ItemPrefabEntry> prefabMap,
        Dictionary<string, List<Transform>> freeAreaPoints)
    {
        List<SurvivalItemData> result =
            new List<SurvivalItemData>();

        foreach (
            SurvivalItemData item
            in dataLoader.ItemDatabase.items
        )
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            if (!prefabMap.ContainsKey(item.itemId))
            {
                continue;
            }

            if (GetValidAreaIds(
                    item,
                    freeAreaPoints
                ).Count == 0)
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static List<string> GetValidAreaIds(
        SurvivalItemData item,
        Dictionary<string, List<Transform>> freeAreaPoints)
    {
        List<string> result =
            new List<string>();

        if (item.allowedSpawnAreas == null)
        {
            return result;
        }

        foreach (string areaId in item.allowedSpawnAreas)
        {
            if (string.IsNullOrWhiteSpace(areaId) ||
                !freeAreaPoints.TryGetValue(
                    areaId,
                    out List<Transform> points
                ) ||
                points.Count == 0)
            {
                continue;
            }

            result.Add(areaId);
        }

        return result;
    }

    private void UpdateBagMembership()
    {
        if (bagZone == null)
        {
            return;
        }

        for (int i = spawnedItems.Count - 1; i >= 0; i--)
        {
            RuntimeWorldItem item = spawnedItems[i];

            if (item == null)
            {
                spawnedItems.RemoveAt(i);
                continue;
            }

            bool isInside =
                !item.IsHeld &&
                IsPointInsideCollider(
                    bagZone,
                    item.GetReferencePosition(),
                    bagInsideTolerance
                );

            SetBagMembership(item, isInside);
        }
    }

    private void SetBagMembership(
        RuntimeWorldItem item,
        bool shouldBeInside)
    {
        if (item == null)
        {
            return;
        }

        bagMembership.TryGetValue(
            item,
            out bool wasInside
        );

        if (wasInside == shouldBeInside)
        {
            return;
        }

        bagMembership[item] = shouldBeInside;

        if (shouldBeInside)
        {
            AddItemToBag(item.ItemId);
        }
        else
        {
            RemoveItemFromBag(item.ItemId);
        }
    }

    private static bool IsPointInsideCollider(
        Collider targetCollider,
        Vector3 point,
        float tolerance)
    {
        Vector3 closest =
            targetCollider.ClosestPoint(point);

        return (
            closest - point
        ).sqrMagnitude <= tolerance * tolerance;
    }

    private void ClearSpawnedItems()
    {
        for (int i = spawnedItems.Count - 1; i >= 0; i--)
        {
            RuntimeWorldItem item = spawnedItems[i];

            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        spawnedItems.Clear();
        bagMembership.Clear();
        heldItem = null;
    }

    private void ResetSessionData()
    {
        selectedItemQuantities.Clear();
        actionLogs.Clear();
        availableItemIds.Clear();

        ElapsedTime = 0f;
        RemainingTime = gameDurationSeconds;
        CurrentWeight = 0f;

        LastScoreResult = null;
        LastGameResult = null;

        CurrentStoryIndex = -1;
        CurrentState = GameFlowState.None;
        IsGameRunning = false;
        IsGameEnded = false;
    }

    public void SetPlayerProfile(
        string gender,
        int age,
        float calculatedMaxWeight)
    {
        playerGender = gender;
        playerAge = age;
        maxWeight = Mathf.Max(
            0f,
            calculatedMaxWeight
        );
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

    public bool AddItemToBag(string itemId)
    {
        if (!IsGameRunning)
        {
            return false;
        }

        SurvivalItemData item =
            GetItemOrLogError(itemId);

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

    public bool RemoveItemFromBag(string itemId)
    {
        if (!IsGameRunning)
        {
            return false;
        }

        SurvivalItemData item =
            GetItemOrLogError(itemId);

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
            return false;
        }

        currentQuantity--;

        if (currentQuantity == 0)
        {
            selectedItemQuantities.Remove(itemId);
        }
        else
        {
            selectedItemQuantities[itemId] =
                currentQuantity;
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
        return selectedItemQuantities.TryGetValue(
            itemId,
            out int quantity
        )
            ? quantity
            : 0;
    }

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

        ReleaseHeldItem();
        UpdateBagMembership();

        IsGameRunning = false;
        IsGameEnded = true;
        CurrentState = GameFlowState.Result;

        RecalculateCurrentWeight();
        CalculateFinalScore();

        LastGameResult = BuildGameResult();

        Debug.Log(
            "[GameManager] 게임 종료 결과\n" +
            JsonUtility.ToJson(
                LastGameResult,
                true
            )
        );

        onGameplayEnded?.Invoke();
        GameEnded?.Invoke(LastGameResult);
    }

    public string GetLastGameResultJson()
    {
        return LastGameResult == null
            ? string.Empty
            : JsonUtility.ToJson(
                LastGameResult,
                true
            );
    }

    private void CalculateFinalScore()
    {
        SurvivalScoreInput input =
            new SurvivalScoreInput
            {
                disaster = scenarioId,
                maxWeight = maxWeight,
                selectedItems =
                    BuildScoreSelectedItems(),
                availableItemIds =
                    new List<string>(
                        availableItemIds
                    )
            };

        LastScoreResult =
            SurvivalScoreCalculator.Evaluate(
                input,
                dataLoader.ItemDatabase,
                dataLoader.ScoreRules
            );
    }

    private List<ScoreSelectedItem>
        BuildScoreSelectedItems()
    {
        List<ScoreSelectedItem> result =
            new List<ScoreSelectedItem>();

        foreach (
            KeyValuePair<string, int> selected
            in selectedItemQuantities
        )
        {
            result.Add(
                new ScoreSelectedItem
                {
                    itemId = selected.Key,
                    quantity = selected.Value
                }
            );
        }

        return result;
    }

    private List<SelectedItemResult>
        BuildSelectedItemResults()
    {
        List<SelectedItemResult> result =
            new List<SelectedItemResult>();

        foreach (
            KeyValuePair<string, int> selected
            in selectedItemQuantities
        )
        {
            SurvivalItemData item =
                dataLoader.GetItemById(
                    selected.Key
                );

            if (item == null)
            {
                continue;
            }

            result.Add(
                new SelectedItemResult
                {
                    itemId = item.itemId,
                    itemName = item.itemName,
                    quantity = selected.Value,
                    unitWeight =
                        RoundToTwoDecimals(
                            item.weight
                        ),
                    totalWeight =
                        RoundToTwoDecimals(
                            item.weight *
                            selected.Value
                        ),
                    isTrap = item.isTrap
                }
            );
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

            elapsedTime =
                RoundToTwoDecimals(
                    ElapsedTime
                ),

            totalWeight =
                RoundToTwoDecimals(
                    CurrentWeight
                ),

            maxWeight =
                RoundToTwoDecimals(
                    maxWeight
                ),

            selectedItems =
                BuildSelectedItemResults(),

            actionLogs =
                new List<GameActionLog>(
                    actionLogs
                ),

            availableItemIds =
                new List<string>(
                    availableItemIds
                ),

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
                dataLoader.GetItemById(
                    selected.Key
                );

            if (item == null)
            {
                continue;
            }

            totalWeight +=
                item.weight *
                selected.Value;
        }

        CurrentWeight =
            RoundToTwoDecimals(
                totalWeight
            );
    }

    private void AddActionLog(
        string action,
        SurvivalItemData item,
        int quantityAfterAction)
    {
        actionLogs.Add(
            new GameActionLog
            {
                elapsedTime =
                    RoundToTwoDecimals(
                        ElapsedTime
                    ),

                action = action,
                itemId = item.itemId,
                itemName = item.itemName,
                quantityAfterAction =
                    quantityAfterAction,

                currentWeight =
                    RoundToTwoDecimals(
                        CurrentWeight
                    )
            }
        );
    }

    private SurvivalItemData GetItemOrLogError(
        string itemId)
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
                $"[GameManager] JSON에서 itemId를 찾을 수 없습니다: {itemId}"
            );
        }

        return item;
    }

    private bool EnsureDataLoader()
    {
        if (dataLoader == null)
        {
            dataLoader =
                SurvivalDataLoader.Instance;
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

    private static void SetLayerRecursively(
        GameObject target,
        int layer)
    {
        target.layer = layer;

        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(
                child.gameObject,
                layer
            );
        }
    }

    private static float RoundToTwoDecimals(
        float value)
    {
        return Mathf.Round(
            value * 100f
        ) / 100f;
    }

#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        if (rayOrigin == null)
        {
            return;
        }

        Gizmos.DrawLine(
            rayOrigin.position,
            rayOrigin.position +
            rayOrigin.forward *
            grabDistance
        );
    }

    [ContextMenu("게임 시작 테스트")]
    private void TestStartGame()
    {
        StartGame();
    }

    [ContextMenu("스토리 다음 단계")]
    private void TestNextStory()
    {
        AdvanceStory();
    }

    [ContextMenu("점수 시스템 테스트")]
    private void TestScoreSystem()
    {
        if (!EnsureDataLoader())
        {
            return;
        }

        ResetSessionData();

        ElapsedTime = 20f;
        IsGameRunning = true;
        IsGameEnded = false;
        CurrentState = GameFlowState.Playing;

        AddItemToBag("water");
        AddItemToBag("energy_bar");
        AddItemToBag("first_aid_kit");
        AddItemToBag("flashlight");
        AddItemToBag("game_console");

        EndGame();
    }

#endif
}

/// <summary>
/// GameManager가 런타임에 생성 아이템에 자동으로 붙이는 내부 컴포넌트입니다.
/// 프리팹에 직접 붙일 필요가 없습니다.
/// </summary>
internal sealed class RuntimeWorldItem : MonoBehaviour
{
    public string ItemId { get; private set; }
    public bool IsHeld { get; private set; }

    private Rigidbody body;
    private Transform originalParent;
    private Vector3 holdLocalPosition;
    private Vector3 holdLocalEulerAngles;

    public void Initialize(
        string itemId,
        Rigidbody targetBody,
        Vector3 newHoldLocalPosition,
        Vector3 newHoldLocalEulerAngles)
    {
        ItemId = itemId;
        body = targetBody;
        originalParent = transform.parent;
        holdLocalPosition =
            newHoldLocalPosition;
        holdLocalEulerAngles =
            newHoldLocalEulerAngles;
    }

    public void Grab(Transform targetHoldPoint)
    {
        if (IsHeld || targetHoldPoint == null)
        {
            return;
        }

        originalParent = transform.parent;
        IsHeld = true;

        if (body != null)
        {
            body.linearVelocity =
                Vector3.zero;

            body.angularVelocity =
                Vector3.zero;

            body.useGravity = false;
            body.isKinematic = true;
        }

        transform.SetParent(
            targetHoldPoint,
            false
        );

        transform.localPosition =
            holdLocalPosition;

        transform.localRotation =
            Quaternion.Euler(
                holdLocalEulerAngles
            );
    }

    public void Drop()
    {
        if (!IsHeld)
        {
            return;
        }

        transform.SetParent(
            originalParent,
            true
        );

        IsHeld = false;

        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;

            body.linearVelocity =
                Vector3.zero;

            body.angularVelocity =
                Vector3.zero;
        }
    }

    public Vector3 GetReferencePosition()
    {
        if (body != null)
        {
            return body.worldCenterOfMass;
        }

        return transform.position;
    }
}
