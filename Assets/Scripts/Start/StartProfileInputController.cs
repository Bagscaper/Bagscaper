using UnityEngine;
using UnityEngine.InputSystem;

public sealed class StartProfileInputController : MonoBehaviour
{
    [SerializeField] private StartProfileUIController uiController;
    [SerializeField, Range(0.1f, 0.95f)] private float engageThreshold = 0.7f;
    [SerializeField, Range(0.05f, 0.9f)] private float releaseThreshold = 0.25f;

    private bool verticalInputLatched;
    private InputAction simulatedRightThumbstick;

    private void Reset()
    {
        uiController = GetComponent<StartProfileUIController>();
    }

    private void Awake()
    {
        if (uiController == null)
        {
            uiController = GetComponent<StartProfileUIController>();
        }
    }

    private void OnEnable()
    {
        simulatedRightThumbstick = new InputAction(
            "Simulated Right Thumbstick",
            InputActionType.Value,
            "<XRController>{RightHand}/primary2DAxis");
        simulatedRightThumbstick.Enable();
    }

    private void OnDisable()
    {
        simulatedRightThumbstick?.Disable();
        simulatedRightThumbstick?.Dispose();
        simulatedRightThumbstick = null;
        verticalInputLatched = false;
    }

    private void Update()
    {
        float ovrVertical = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;
        float simulatedVertical = simulatedRightThumbstick != null
            ? simulatedRightThumbstick.ReadValue<Vector2>().y
            : 0f;
        float vertical = Mathf.Abs(simulatedVertical) > Mathf.Abs(ovrVertical)
            ? simulatedVertical
            : ovrVertical;

        if (verticalInputLatched)
        {
            if (Mathf.Abs(vertical) <= releaseThreshold)
            {
                verticalInputLatched = false;
            }

            return;
        }

        if (vertical >= engageThreshold)
        {
            verticalInputLatched = true;
            uiController?.SelectPreviousAge();
        }
        else if (vertical <= -engageThreshold)
        {
            verticalInputLatched = true;
            uiController?.SelectNextAge();
        }
    }
}
