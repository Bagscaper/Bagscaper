using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class PlayerProfileGameStartRequestBridge : MonoBehaviour
{
    private GameManager subscribedManager;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void Update()
    {
        if (subscribedManager == null)
        {
            TrySubscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public bool ApplySelectionToRequest(GameStartRequest request)
    {
        if (request == null)
        {
            Debug.LogError("[PlayerProfileBridge] 게임 시작 요청 객체가 없습니다.");
            return false;
        }

        if (!PlayerProfileSelection.TryGet(out PlayerProfileSelection.Profile profile))
        {
            Debug.LogError("[PlayerProfileBridge] 확인된 개인정보 선택값이 없습니다.");
            return false;
        }

        request.gender = profile.GenderApiValue;
        request.age_group = profile.AgeGroupApiValue;
        return true;
    }

    private void TrySubscribe()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null || manager == subscribedManager)
        {
            return;
        }

        Unsubscribe();
        subscribedManager = manager;
        subscribedManager.GameStartRequested += ApplySelection;
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.GameStartRequested -= ApplySelection;
            subscribedManager = null;
        }
    }

    private void ApplySelection(GameStartRequest request)
    {
        ApplySelectionToRequest(request);
    }
}
