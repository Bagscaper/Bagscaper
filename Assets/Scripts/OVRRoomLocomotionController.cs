using UnityEngine;

/// <summary>
/// 중력 없이 공중에 떠 있는 OVR 카메라 리그 이동 컨트롤러입니다.
///
/// 왼쪽 스틱 : 바라보는 방향 기준 수평 이동
/// 오른쪽 스틱 : 스냅 턴 또는 부드러운 회전
///
/// 시작할 때의 Y 높이를 그대로 유지하며 바닥 Collider가 없어도 떨어지지 않습니다.
/// OVRCameraRig 루트에 CharacterController와 함께 붙이세요.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class OVRRoomLocomotionController : MonoBehaviour
{
    public enum TurnMode
    {
        Snap,
        Smooth
    }

    [Header("필수 참조")]
    [Tooltip("OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Transform centerEyeAnchor;

    [Header("왼쪽 스틱 이동")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.8f;
    [SerializeField, Range(0f, 0.95f)] private float moveDeadZone = 0.15f;

    [Header("오른쪽 스틱 회전")]
    [SerializeField] private TurnMode turnMode = TurnMode.Snap;
    [SerializeField, Range(5f, 90f)] private float snapTurnAngle = 30f;
    [SerializeField, Min(0f)] private float smoothTurnSpeed = 75f;
    [SerializeField, Range(0.1f, 0.95f)] private float turnActivationThreshold = 0.7f;
    [SerializeField, Range(0f, 0.9f)] private float turnResetThreshold = 0.25f;

    [Header("Character Controller")]
    [Tooltip("Tracking Origin을 Floor Level로 둘 때 0을 사용하세요.")]
    [SerializeField] private float floorOffset = 0f;
    [SerializeField, Min(0.5f)] private float minimumHeight = 1f;
    [SerializeField, Min(1f)] private float maximumHeight = 2.2f;

    private CharacterController characterController;
    private bool snapTurnReady = true;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (centerEyeAnchor == null)
        {
            Debug.LogError(
                "[OVRRoomLocomotionController] Center Eye Anchor가 연결되지 않았습니다.",
                this
            );
        }
    }

    private void Update()
    {
        if (centerEyeAnchor == null || characterController == null)
        {
            return;
        }

        UpdateCapsuleFromHead();
        ProcessMove();
        ProcessTurn();
    }

    private void UpdateCapsuleFromHead()
    {
        Vector3 headLocalPosition = transform.InverseTransformPoint(
            centerEyeAnchor.position
        );

        float trackedHeight = headLocalPosition.y - floorOffset;
        float capsuleHeight = Mathf.Clamp(
            trackedHeight,
            minimumHeight,
            maximumHeight
        );

        characterController.height = capsuleHeight;
        characterController.center = new Vector3(
            headLocalPosition.x,
            floorOffset + capsuleHeight * 0.5f,
            headLocalPosition.z
        );
    }

    private void ProcessMove()
    {
        Vector2 input = OVRInput.Get(
            OVRInput.RawAxis2D.LThumbstick,
            OVRInput.Controller.LTouch
        );

        if (input.magnitude < moveDeadZone)
        {
            input = Vector2.zero;
        }
        else
        {
            input = Vector2.ClampMagnitude(input, 1f);
        }

        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = centerEyeAnchor.right;
        right.y = 0f;
        right.Normalize();

        Vector3 horizontalMotion =
            (forward * input.y + right * input.x) * moveSpeed;

        // 중력과 수직 속도를 전혀 적용하지 않습니다.
        // 따라서 OVRCameraRig는 현재 Y 높이를 그대로 유지합니다.
        characterController.Move(horizontalMotion * Time.deltaTime);
    }

    private void ProcessTurn()
    {
        Vector2 input = OVRInput.Get(
            OVRInput.RawAxis2D.RThumbstick,
            OVRInput.Controller.RTouch
        );

        if (turnMode == TurnMode.Smooth)
        {
            if (Mathf.Abs(input.x) < turnResetThreshold)
            {
                return;
            }

            RotateAroundHead(input.x * smoothTurnSpeed * Time.deltaTime);
            return;
        }

        if (Mathf.Abs(input.x) <= turnResetThreshold)
        {
            snapTurnReady = true;
            return;
        }

        if (!snapTurnReady || Mathf.Abs(input.x) < turnActivationThreshold)
        {
            return;
        }

        snapTurnReady = false;
        RotateAroundHead(Mathf.Sign(input.x) * snapTurnAngle);
    }

    private void RotateAroundHead(float angle)
    {
        if (Mathf.Approximately(angle, 0f))
        {
            return;
        }

        Vector3 headPositionBeforeTurn = centerEyeAnchor.position;

        characterController.enabled = false;
        transform.RotateAround(
            headPositionBeforeTurn,
            Vector3.up,
            angle
        );
        characterController.enabled = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maximumHeight = Mathf.Max(maximumHeight, minimumHeight);
        turnResetThreshold = Mathf.Min(
            turnResetThreshold,
            turnActivationThreshold
        );
    }
#endif
}
