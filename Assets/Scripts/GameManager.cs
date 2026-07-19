using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public const int MaxItemCount = 16;

    public static GameManager Instance { get; private set; }

    public enum GameFlowState
    {
        None,
        Intro,
        Playing,
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
    [SerializeField, Min(1f)] private float gameDurationSeconds = 100f;
    [SerializeField] private bool startOnPlay = true;

    [Header("가방 설정")]
    [SerializeField, Min(0)] private int maxWeightGrams = 8000;

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
    [SerializeField, Range(1, MaxItemCount)] private int spawnCount = MaxItemCount;
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

    [Tooltip("현재 아이템을 넣으면 최대 무게를 넘을 때 실행됩니다. 경고음이나 UI 애니메이션을 연결할 수 있습니다.")]
    [SerializeField] private UnityEvent onBagWeightRejected;

    public GameFlowState CurrentState { get; private set; }
    public float RemainingTime { get; private set; }
    public int CurrentWeightGrams { get; private set; }
    public int MaxWeightGrams => maxWeightGrams;
    public bool HasHeldItem => heldItem != null;
    public RuleBasedResult LastRuleResult { get; private set; }


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
            Debug.LogWarning(
                $"[GameManager] 인트로 진행 입력을 무시했습니다. 현재 상태: {CurrentState}"
            );
            return;
        }

        int pageCount = uiController != null ? uiController.IntroPageCount : 0;

        // 현재 보이는 페이지가 마지막 페이지라면 다음 트리거에서 바로 게임을 시작합니다.
        if (pageCount <= 0 || introIndex >= pageCount - 1)
        {
            Debug.Log("[GameManager] 마지막 인트로 페이지 확인 → 게임 시작");
            FinishIntro();
            return;
        }

        introIndex++;
        Debug.Log($"[GameManager] 인트로 페이지 이동: {introIndex + 1}/{pageCount}");
        uiController.ShowIntroPage(introIndex);
    }

    private void FinishIntro()
    {
        BeginGameplay();
    }

    /// <summary>
    /// Inspector의 Button OnClick 또는 디버그용으로 인트로에서 즉시 게임을 시작합니다.
    /// </summary>
    public void StartGameplayImmediately()
    {
        if (CurrentState != GameFlowState.Intro)
        {
            return;
        }

        BeginGameplay();
    }

    private void BeginGameplay()
    {
        Debug.Log($"[GameManager] 게임 플레이 시작 - 제한 시간 {gameDurationSeconds:0}초");
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

        if (inventoryController.CurrentItemCount >= MaxItemCount ||
            !inventoryController.HasEmptySlot)
        {
            Debug.LogWarning(
                $"[GameManager] 아이템은 최대 {MaxItemCount}개까지만 넣을 수 있습니다."
            );
            return;
        }

        RuntimeWorldItem targetItem = heldItem;
        SurvivalItemData itemData = dataLoader.GetItemById(targetItem.ItemId);

        if (itemData == null)
        {
            Debug.LogError($"[GameManager] 알 수 없는 itemId: {targetItem.ItemId}");
            return;
        }

        int nextWeightGrams = CurrentWeightGrams + Mathf.Max(0, itemData.weightGrams);

        if (maxWeightGrams > 0 && nextWeightGrams > maxWeightGrams)
        {
            Debug.LogWarning(
                $"[GameManager] 무게 초과로 넣을 수 없습니다. " +
                $"현재 {CurrentWeightGrams:N0}g + 아이템 {itemData.weightGrams:N0}g " +
                $"> 최대 {maxWeightGrams:N0}g"
            );

            onBagWeightRejected?.Invoke();
            return;
        }

        string prefabKey = NormalizeItemId(targetItem.ItemId);

        if (!prefabMap.TryGetValue(prefabKey, out ItemPrefabEntry entry))
        {
            Debug.LogError(
                $"[GameManager] 프리팹 매핑이 없습니다: {targetItem.ItemId} " +
                $"(정규화 키: {prefabKey})"
            );
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
            CurrentWeightGrams,
            maxWeightGrams,
            selectedItemQuantities,
            dataLoader
        );

        currentResultSuccess = LastRuleResult.isSuccess;
        onGameplayEnded?.Invoke();
        ShowRuleBasedResult();
    }

    private void ShowRuleBasedResult()
    {
        if (LastRuleResult == null)
        {
            LastRuleResult = RuleBasedEvaluator.Evaluate(
                scenarioId,
                CurrentWeightGrams,
                maxWeightGrams,
                selectedItemQuantities,
                dataLoader
            );
        }

        currentResultSuccess = LastRuleResult.isSuccess;
        CurrentState = GameFlowState.Result;
        resultCardIndex = 0;

        uiController.BeginResult(
            currentResultSuccess,
            LastRuleResult.cardText
        );
    }

    public void AdvanceResultCard()
    {
        if (CurrentState != GameFlowState.Result)
        {
            return;
        }

        resultCardIndex++;

        int cardCount = uiController != null
            ? uiController.GetResultCardCount(currentResultSuccess)
            : 0;

        if (resultCardIndex >= cardCount)
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

        CurrentWeightGrams = 0;
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
        int totalGrams = 0;

        foreach (KeyValuePair<string, int> selected in selectedItemQuantities)
        {
            SurvivalItemData item = dataLoader.GetItemById(selected.Key);
            if (item != null)
            {
                totalGrams += item.weightGrams * selected.Value;
            }
        }

        CurrentWeightGrams = maxWeightGrams > 0
            ? Mathf.Clamp(totalGrams, 0, maxWeightGrams)
            : Mathf.Max(0, totalGrams);
    }

    private void RefreshHud()
    {
        if (uiController != null)
        {
            uiController.UpdateHud(
                RemainingTime,
                CurrentWeightGrams,
                maxWeightGrams
            );
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

            string normalizedId = NormalizeItemId(entry.itemId);

            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                continue;
            }

            if (prefabMap.ContainsKey(normalizedId))
            {
                Debug.LogWarning(
                    $"[GameManager] 중복 프리팹 itemId를 건너뜁니다: " +
                    $"{entry.itemId} → {normalizedId}"
                );
                continue;
            }

            prefabMap.Add(normalizedId, entry);

            if (!string.Equals(
                    entry.itemId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log(
                    $"[GameManager] 프리팹 itemId 접두어를 자동 보정했습니다: " +
                    $"{entry.itemId} → {normalizedId}"
                );
            }
        }

        Debug.Log($"[GameManager] 프리팹 매핑 {prefabMap.Count}개 준비 완료");
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

        int requestedSpawnCount = Mathf.Clamp(
            spawnCount,
            1,
            MaxItemCount
        );

        int created = 0;

        try
        {
            while (created < requestedSpawnCount && candidates.Count > 0)
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

                string prefabKey = NormalizeItemId(itemData.itemId);
                SpawnOneItem(itemData, prefabMap[prefabKey], spawnPoint);
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

        // Unity Rigidbody.mass는 kg 단위이므로 JSON의 g 값을 물리 계산용으로만 변환합니다.
        body.mass = Mathf.Max(0.01f, itemData.weightGrams / 1000f);
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
            if (item == null || string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            string prefabKey = NormalizeItemId(item.itemId);

            if (!prefabMap.ContainsKey(prefabKey))
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

    private static string NormalizeItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return string.Empty;
        }

        string normalized = itemId.Trim();

        // Inspector 프리팹 목록이 item_water처럼 작성되어 있어도
        // JSON의 water와 같은 ID로 자동 매칭합니다.
        if (normalized.StartsWith(
                "item_",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("item_".Length);
        }

        return normalized;
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
    private void OnValidate()
    {
        spawnCount = Mathf.Clamp(spawnCount, 1, MaxItemCount);
        gameDurationSeconds = Mathf.Max(1f, gameDurationSeconds);
        maxWeightGrams = Mathf.Max(0, maxWeightGrams);
    }

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
