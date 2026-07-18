using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameFlowState
    {
        None,
        Intro,
        WaitingForStart,
        Playing,
        WaitingForResult,
        Result,
        Finished
    }

    [Serializable]
    public class ItemPrefabEntry
    {
        [Tooltip("survival_items.json의 itemId와 정확히 같아야 합니다.")]
        public string itemId;

        [Header("월드 프리팹")]
        public GameObject worldPrefab;
        public Vector3 spawnPositionOffset;
        public Vector3 spawnRotationOffset;

        [Header("손에 잡혔을 때")]
        public Vector3 holdLocalPosition;
        public Vector3 holdLocalEulerAngles;

        [Header("인벤토리 미리보기")]
        public GameObject inventoryPreviewPrefab;
        public Vector3 inventoryLocalPosition;
        public Vector3 inventoryLocalEulerAngles;
        public Vector3 inventoryLocalScale = Vector3.one * 0.08f;
    }

    [Serializable]
    public class SpawnAreaEntry
    {
        public string areaId;
        public Transform pointsRoot;
    }

    [Header("게임 설정")]
    [SerializeField] private string scenarioId = "Earthquake";
    [SerializeField, Min(1f)] private float gameDurationSeconds = 60f;
    [SerializeField] private bool startOnPlay = true;
    [SerializeField] private bool useBackendApi = true;

    [Header("플레이어")]
    [SerializeField] private string playerGender = "Female";
    [SerializeField] private string playerAgeGroup = "20s";
    [SerializeField, Min(0f)] private float maxWeight = 8f;

    [Header("핵심 참조")]
    [SerializeField] private SurvivalDataLoader dataLoader;
    [SerializeField] private BagscapeUIController uiController;
    [SerializeField] private InventoryController inventoryController;

    [Header("트리거 잡기")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private Transform holdPoint;
    [SerializeField, Min(0.1f)] private float grabDistance = 4f;
    [SerializeField] private LayerMask grabbableLayers;

    [Header("랜덤 아이템 생성")]
    [SerializeField, Min(1)] private int spawnCount = 15;
    [SerializeField] private bool uniqueItemIds = true;
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int fixedSeed = 1234;
    [SerializeField] private Transform spawnedItemRoot;
    [SerializeField] private string grabbableLayerName = "GrabbableItem";
    [SerializeField] private List<ItemPrefabEntry> itemPrefabs =
        new List<ItemPrefabEntry>();
    [SerializeField] private List<SpawnAreaEntry> spawnAreas =
        new List<SpawnAreaEntry>();

    [Header("이벤트")]
    [SerializeField] private UnityEvent onGameplayStarted;
    [SerializeField] private UnityEvent onGameplayEnded;
    [SerializeField] private UnityEvent onResultFinished;

    public GameFlowState CurrentState { get; private set; }
    public float RemainingTime { get; private set; }
    public float CurrentWeight { get; private set; }
    public float MaxWeight => maxWeight;
    public bool HasHeldItem => heldItem != null;
    public RuleBasedResult LastRuleResult { get; private set; }

    public event Action<GameStartRequest> GameStartRequested;
    public event Action<GameLogRequest> BagLogRequested;
    public event Action<string> GameResultRequested;

    private readonly Dictionary<string, int> selectedItemQuantities =
        new Dictionary<string, int>();

    private readonly List<RuntimeWorldItem> spawnedItems =
        new List<RuntimeWorldItem>();

    private readonly Dictionary<string, ItemPrefabEntry> prefabMap =
        new Dictionary<string, ItemPrefabEntry>();

    private RuntimeWorldItem heldItem;
    private int introIndex;
    private int resultCardIndex;
    private bool currentResultSuccess;
    private float elapsedTime;
    private string sessionId;

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

        BuildPrefabMap();

        if (startOnPlay)
        {
            StartGame();
        }
    }

    private void Update()
    {
        if (CurrentState != GameFlowState.Playing)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        RemainingTime = Mathf.Max(0f, gameDurationSeconds - elapsedTime);
        RefreshHud();

        if (RemainingTime <= 0f)
        {
            EndGame();
        }
    }

    public void StartGame()
    {
        if (!EnsureReferences())
        {
            return;
        }

        ResetSession();
        BeginIntro();
    }

    private void BeginIntro()
    {
        CurrentState = GameFlowState.Intro;
        introIndex = 0;

        if (uiController.IntroPageCount <= 0)
        {
            FinishIntro();
            return;
        }

        uiController.ShowIntroPage(introIndex);
    }

    public void AdvanceIntro()
    {
        if (CurrentState != GameFlowState.Intro)
        {
            return;
        }

        introIndex++;

        if (introIndex >= uiController.IntroPageCount)
        {
            FinishIntro();
            return;
        }

        uiController.ShowIntroPage(introIndex);
    }

    private void FinishIntro()
    {
        if (useBackendApi)
        {
            CurrentState = GameFlowState.WaitingForStart;

            if (GameStartRequested == null)
            {
                Debug.LogWarning(
                    "[GameManager] GameApiClient가 연결되지 않아 로컬 모드로 시작합니다."
                );
                BeginGameplay();
                return;
            }

            GameStartRequested.Invoke(
                new GameStartRequest
                {
                    gender = playerGender,
                    age_group = playerAgeGroup,
                    disaster = scenarioId
                }
            );
        }
        else
        {
            BeginGameplay();
        }
    }

    public void ApplyStartSession(
        string newSessionId,
        int referenceBmrKcalDay,
        int requiredWater72hMl,
        float maxCarryWeightKg,
        string expiresAt)
    {
        if (CurrentState != GameFlowState.WaitingForStart)
        {
            return;
        }

        sessionId = newSessionId;

        if (maxCarryWeightKg > 0f)
        {
            maxWeight = maxCarryWeightKg;
        }

        BeginGameplay();
    }

    public void ApplyStartRequestFailed(string reason)
    {
        Debug.LogWarning(
            "[GameManager] 게임 시작 API 실패. 로컬 설정으로 진행합니다.\n" + reason
        );

        if (CurrentState == GameFlowState.WaitingForStart)
        {
            BeginGameplay();
        }
    }

    private void BeginGameplay()
    {
        CurrentState = GameFlowState.Playing;
        elapsedTime = 0f;
        RemainingTime = gameDurationSeconds;

        SpawnRandomItems();
        uiController.ShowGameplay();
        inventoryController.CloseInventory();
        RefreshHud();
        onGameplayStarted?.Invoke();
    }

    public void ToggleGrab()
    {
        if (CurrentState != GameFlowState.Playing)
        {
            return;
        }

        if (heldItem != null)
        {
            ReleaseHeldItem();
        }
        else
        {
            TryGrabItem();
        }
    }

    private void TryGrabItem()
    {
        if (rayOrigin == null || holdPoint == null)
        {
            Debug.LogWarning("[GameManager] Ray Origin 또는 Hold Point가 없습니다.");
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

        RuntimeWorldItem target =
            hit.collider.GetComponentInParent<RuntimeWorldItem>();

        if (target == null || target.IsHeld)
        {
            return;
        }

        heldItem = target;
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

    public void StoreHeldItemInInventory()
    {
        if (CurrentState != GameFlowState.Playing || heldItem == null)
        {
            return;
        }

        if (!inventoryController.HasEmptySlot)
        {
            Debug.LogWarning("[GameManager] 인벤토리가 가득 찼습니다.");
            return;
        }

        RuntimeWorldItem targetItem = heldItem;
        SurvivalItemData itemData = dataLoader.GetItemById(targetItem.ItemId);

        if (itemData == null)
        {
            Debug.LogError($"[GameManager] 알 수 없는 itemId: {targetItem.ItemId}");
            return;
        }

        if (!prefabMap.TryGetValue(targetItem.ItemId, out ItemPrefabEntry entry))
        {
            Debug.LogError($"[GameManager] 프리팹 매핑이 없습니다: {targetItem.ItemId}");
            return;
        }

        GameObject previewPrefab = entry.inventoryPreviewPrefab != null
            ? entry.inventoryPreviewPrefab
            : entry.worldPrefab;

        bool stored = inventoryController.AddItem(
            itemData,
            previewPrefab,
            entry.inventoryLocalPosition,
            entry.inventoryLocalEulerAngles,
            entry.inventoryLocalScale
        );

        if (!stored)
        {
            return;
        }

        AddLocalItem(itemData);
        SendBagLog("ADD", targetItem);

        heldItem = null;
        spawnedItems.Remove(targetItem);
        Destroy(targetItem.gameObject);
        RefreshHud();
    }

    private void AddLocalItem(SurvivalItemData item)
    {
        if (!selectedItemQuantities.ContainsKey(item.itemId))
        {
            selectedItemQuantities.Add(item.itemId, 0);
        }

        selectedItemQuantities[item.itemId]++;
        RecalculateCurrentWeight();
    }

    private void SendBagLog(string action, RuntimeWorldItem item)
    {
        if (!useBackendApi || string.IsNullOrWhiteSpace(sessionId) || item == null)
        {
            return;
        }

        BagLogRequested?.Invoke(
            new GameLogRequest
            {
                session_id = sessionId,
                action_id = Guid.NewGuid().ToString(),
                action = action,
                item_instance_id = item.InstanceId,
                item_id = item.ItemId,
                occurred_at = DateTime.UtcNow.ToString("o")
            }
        );
    }

    public void ApplyServerBagState(
        bool applied,
        int itemCount,
        int currentWeightGrams)
    {
        if (!applied)
        {
            Debug.LogWarning("[GameManager] 서버가 중복 가방 액션으로 판단했습니다.");
        }

        if (currentWeightGrams >= 0)
        {
            CurrentWeight = currentWeightGrams / 1000f;
            RefreshHud();
        }
    }

    public void EndGame()
    {
        if (CurrentState != GameFlowState.Playing)
        {
            return;
        }

        ReleaseHeldItem();
        inventoryController.CloseInventory();
        RecalculateCurrentWeight();

        LastRuleResult = RuleBasedEvaluator.Evaluate(
            scenarioId,
            CurrentWeight,
            maxWeight,
            selectedItemQuantities,
            dataLoader
        );

        currentResultSuccess = LastRuleResult.isSuccess;
        onGameplayEnded?.Invoke();

        if (useBackendApi && !string.IsNullOrWhiteSpace(sessionId))
        {
            CurrentState = GameFlowState.WaitingForResult;
            uiController.ShowWaitingForResult();

            if (GameResultRequested == null)
            {
                ApplyResultRequestFailed("GameApiClient가 연결되지 않았습니다.");
                return;
            }

            GameResultRequested.Invoke(sessionId);
        }
        else
        {
            ApplyFinalResult(
                currentResultSuccess ? "SUCCESS" : "FAIL",
                "AI 코멘트를 사용하려면 GameApiClient와 서버 Base URL을 연결하세요.",
                0
            );
        }
    }

    public void ApplyFinalResult(
        string survivalType,
        string aiComment,
        int survivalTimeHours)
    {
        if (LastRuleResult == null)
        {
            LastRuleResult = RuleBasedEvaluator.Evaluate(
                scenarioId,
                CurrentWeight,
                maxWeight,
                selectedItemQuantities,
                dataLoader
            );
        }

        currentResultSuccess = LastRuleResult.isSuccess;
        CurrentState = GameFlowState.Result;
        resultCardIndex = 0;

        string finalAiComment = string.IsNullOrWhiteSpace(aiComment)
            ? "AI 코멘트가 비어 있습니다. 서버의 ai_comment 필드를 확인하세요."
            : aiComment;

        uiController.BeginResult(
            currentResultSuccess,
            LastRuleResult.cardText,
            finalAiComment,
            survivalTimeHours
        );
    }

    public void ApplyResultRequestFailed(string reason)
    {
        Debug.LogWarning(
            "[GameManager] AI 결과 요청 실패. 룰베이스 결과만 표시합니다.\n" + reason
        );

        ApplyFinalResult(
            currentResultSuccess ? "SUCCESS" : "FAIL",
            "AI 코멘트를 불러오지 못했습니다. 네트워크와 서버 설정을 확인하세요.",
            0
        );
    }

    public void AdvanceResultCard()
    {
        if (CurrentState != GameFlowState.Result)
        {
            return;
        }

        resultCardIndex++;

        if (resultCardIndex >= 3)
        {
            CurrentState = GameFlowState.Finished;
            onResultFinished?.Invoke();
            return;
        }

        uiController.ShowResultCard(currentResultSuccess, resultCardIndex);
    }

    private void ResetSession()
    {
        ReleaseHeldItem();
        ClearSpawnedItems();
        selectedItemQuantities.Clear();
        inventoryController.ClearInventory();
        uiController.ResetAll();

        sessionId = string.Empty;
        CurrentWeight = 0f;
        elapsedTime = 0f;
        RemainingTime = gameDurationSeconds;
        introIndex = 0;
        resultCardIndex = 0;
        currentResultSuccess = false;
        LastRuleResult = null;
        CurrentState = GameFlowState.None;
    }

    private void RecalculateCurrentWeight()
    {
        float total = 0f;

        foreach (KeyValuePair<string, int> selected in selectedItemQuantities)
        {
            SurvivalItemData item = dataLoader.GetItemById(selected.Key);
            if (item != null)
            {
                total += item.weight * selected.Value;
            }
        }

        CurrentWeight = Mathf.Round(total * 100f) / 100f;
    }

    private void RefreshHud()
    {
        if (uiController != null)
        {
            uiController.UpdateHud(RemainingTime, CurrentWeight, maxWeight);
        }
    }

    private bool EnsureReferences()
    {
        if (dataLoader == null)
        {
            dataLoader = SurvivalDataLoader.Instance;
        }

        if (dataLoader == null || !dataLoader.IsLoaded)
        {
            Debug.LogError("[GameManager] SurvivalDataLoader가 준비되지 않았습니다.");
            return false;
        }

        if (uiController == null)
        {
            Debug.LogError("[GameManager] BagscapeUIController가 연결되지 않았습니다.");
            return false;
        }

        if (inventoryController == null)
        {
            Debug.LogError("[GameManager] InventoryController가 연결되지 않았습니다.");
            return false;
        }

        return true;
    }

    private void BuildPrefabMap()
    {
        prefabMap.Clear();

        foreach (ItemPrefabEntry entry in itemPrefabs)
        {
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.itemId) ||
                entry.worldPrefab == null)
            {
                continue;
            }

            if (!prefabMap.ContainsKey(entry.itemId))
            {
                prefabMap.Add(entry.itemId, entry);
            }
        }
    }

    private void SpawnRandomItems()
    {
        Dictionary<string, List<Transform>> freePoints = BuildSpawnPointMap();
        List<SurvivalItemData> candidates = BuildSpawnCandidates(freePoints);

        if (candidates.Count == 0)
        {
            Debug.LogError("[GameManager] 생성 가능한 아이템이 없습니다.");
            return;
        }

        UnityEngine.Random.State oldState = UnityEngine.Random.state;
        if (useFixedSeed)
        {
            UnityEngine.Random.InitState(fixedSeed);
        }

        int created = 0;

        try
        {
            while (created < spawnCount && candidates.Count > 0)
            {
                int candidateIndex = UnityEngine.Random.Range(0, candidates.Count);
                SurvivalItemData itemData = candidates[candidateIndex];
                List<string> validAreas = GetValidAreaIds(itemData, freePoints);

                if (validAreas.Count == 0)
                {
                    candidates.RemoveAt(candidateIndex);
                    continue;
                }

                string areaId = validAreas[UnityEngine.Random.Range(0, validAreas.Count)];
                List<Transform> points = freePoints[areaId];
                int pointIndex = UnityEngine.Random.Range(0, points.Count);
                Transform spawnPoint = points[pointIndex];
                points.RemoveAt(pointIndex);

                SpawnOneItem(itemData, prefabMap[itemData.itemId], spawnPoint);
                created++;

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
                UnityEngine.Random.state = oldState;
            }
        }

        Debug.Log($"[GameManager] 랜덤 아이템 {created}개 생성 완료");
    }

    private void SpawnOneItem(
        SurvivalItemData itemData,
        ItemPrefabEntry entry,
        Transform spawnPoint)
    {
        Vector3 position = spawnPoint.TransformPoint(entry.spawnPositionOffset);
        Quaternion rotation =
            spawnPoint.rotation * Quaternion.Euler(entry.spawnRotationOffset);

        if (randomYaw)
        {
            rotation = Quaternion.AngleAxis(
                UnityEngine.Random.Range(0f, 360f),
                Vector3.up
            ) * rotation;
        }

        Transform parent = spawnedItemRoot != null ? spawnedItemRoot : transform;
        GameObject instance = Instantiate(entry.worldPrefab, position, rotation, parent);
        instance.name = $"{itemData.itemId}_Spawned";

        int layer = LayerMask.NameToLayer(grabbableLayerName);
        if (layer >= 0)
        {
            SetLayerRecursively(instance, layer);
        }

        Rigidbody body = instance.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = instance.AddComponent<Rigidbody>();
        }

        body.mass = Mathf.Max(0.01f, itemData.weight);
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (instance.GetComponentInChildren<Collider>() == null)
        {
            Debug.LogWarning($"[GameManager] {itemData.itemId} 프리팹에 Collider가 없습니다.");
        }

        RuntimeWorldItem runtimeItem = instance.GetComponent<RuntimeWorldItem>();
        if (runtimeItem == null)
        {
            runtimeItem = instance.AddComponent<RuntimeWorldItem>();
        }

        runtimeItem.Initialize(
            itemData.itemId,
            body,
            entry.holdLocalPosition,
            entry.holdLocalEulerAngles
        );

        spawnedItems.Add(runtimeItem);
    }

    private Dictionary<string, List<Transform>> BuildSpawnPointMap()
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

            List<Transform> points = new List<Transform>();
            Transform[] children = area.pointsRoot.GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children)
            {
                if (child != area.pointsRoot)
                {
                    points.Add(child);
                }
            }

            result[area.areaId] = points;
        }

        return result;
    }

    private List<SurvivalItemData> BuildSpawnCandidates(
        Dictionary<string, List<Transform>> freePoints)
    {
        List<SurvivalItemData> result = new List<SurvivalItemData>();

        foreach (SurvivalItemData item in dataLoader.ItemDatabase.items)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.itemId) ||
                !prefabMap.ContainsKey(item.itemId))
            {
                continue;
            }

            if (GetValidAreaIds(item, freePoints).Count > 0)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static List<string> GetValidAreaIds(
        SurvivalItemData item,
        Dictionary<string, List<Transform>> freePoints)
    {
        List<string> result = new List<string>();

        if (item.allowedSpawnAreas == null)
        {
            return result;
        }

        foreach (string areaId in item.allowedSpawnAreas)
        {
            if (freePoints.TryGetValue(areaId, out List<Transform> points) &&
                points.Count > 0)
            {
                result.Add(areaId);
            }
        }

        return result;
    }

    private void ClearSpawnedItems()
    {
        foreach (RuntimeWorldItem item in spawnedItems)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        spawnedItems.Clear();
        heldItem = null;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;

        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
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
            rayOrigin.position + rayOrigin.forward * grabDistance
        );
    }

    [ContextMenu("게임 시작 테스트")]
    private void TestStartGame()
    {
        StartGame();
    }

    [ContextMenu("게임 강제 종료 테스트")]
    private void TestEndGame()
    {
        EndGame();
    }
#endif
}
