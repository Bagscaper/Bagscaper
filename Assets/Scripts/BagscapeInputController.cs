using UnityEngine;

public class BagscapeInputController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private InventoryController inventoryController;

    [Header("오른손 입력")]
    [SerializeField] private OVRInput.RawButton triggerButton =
        OVRInput.RawButton.RIndexTrigger;

    [SerializeField] private OVRInput.RawButton gripButton =
        OVRInput.RawButton.RHandTrigger;

    [SerializeField] private OVRInput.Controller controller =
        OVRInput.Controller.RTouch;

    private void Start()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    private void Update()
    {
        if (gameManager == null)
        {
            return;
        }

        if (OVRInput.GetDown(triggerButton, controller))
        {
            HandleTrigger();
        }

        if (OVRInput.GetDown(gripButton, controller))
        {
            HandleGrip();
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
        }
        else if (inventoryController != null)
        {
            inventoryController.ToggleInventory();
        }
    }
}
