using UnityEngine;

public class BagscapeInputController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private InventoryController inventoryController;

    [Header("오른손 입력")]
    [Tooltip("Quest 컨트롤러의 아날로그 입력값이 이 값 이상이면 눌린 것으로 처리합니다.")]
    [SerializeField, Range(0.1f, 0.95f)] private float pressThreshold = 0.55f;

    [SerializeField] private OVRInput.Controller controller =
        OVRInput.Controller.RTouch;

    [Header("입력 안정화")]
    [Tooltip("Unity Editor에서 Space=트리거, G=그립으로 테스트합니다.")]
    [SerializeField] private bool allowEditorKeyboardTest = true;

    private bool triggerWasPressed;
    private bool gripWasPressed;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        triggerWasPressed = false;
        gripWasPressed = false;
    }

    private void Update()
    {
        ResolveReferences();

        if (gameManager == null)
        {
            return;
        }

        // RawButton보다 RawAxis1D가 Quest/OpenXR 환경에서 그립 입력을 더 안정적으로 읽습니다.
        float triggerValue = OVRInput.Get(
            OVRInput.RawAxis1D.RIndexTrigger,
            controller
        );

        float gripValue = OVRInput.Get(
            OVRInput.RawAxis1D.RHandTrigger,
            controller
        );

        bool triggerPressed = triggerValue >= pressThreshold;
        bool gripPressed = gripValue >= pressThreshold;

        bool triggerDown = triggerPressed && !triggerWasPressed;
        bool gripDown = gripPressed && !gripWasPressed;

#if UNITY_EDITOR
        if (allowEditorKeyboardTest)
        {
            triggerDown |= Input.GetKeyDown(KeyCode.Space);
            gripDown |= Input.GetKeyDown(KeyCode.G);
        }
#endif

        if (triggerDown)
        {
            HandleTrigger();
        }

        if (gripDown)
        {
            HandleGrip();
        }

        triggerWasPressed = triggerPressed;
        gripWasPressed = gripPressed;
    }

    private void ResolveReferences()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (inventoryController == null)
        {
            inventoryController = FindFirstObjectByType<InventoryController>(
                FindObjectsInactive.Include
            );
        }
    }

    private void HandleTrigger()
    {
        switch (gameManager.CurrentState)
        {
            case GameManager.GameFlowState.Intro:
                gameManager.AdvanceIntro();
                break;

            case GameManager.GameFlowState.Playing:
                gameManager.ToggleGrab();
                break;

            case GameManager.GameFlowState.Result:
                gameManager.AdvanceResultCard();
                break;
        }
    }

    private void HandleGrip()
    {
        if (gameManager.CurrentState != GameManager.GameFlowState.Playing)
        {
            return;
        }

        if (gameManager.HasHeldItem)
        {
            gameManager.StoreHeldItemInInventory();
            return;
        }

        if (inventoryController == null)
        {
            Debug.LogError(
                "[BagscapeInput] InventoryController가 연결되지 않았습니다.",
                this
            );
            return;
        }

        inventoryController.ToggleInventory();
    }
}
