using UnityEngine;
using ViveHandTracking;
using System;

public class FistControlledJoystick : MonoBehaviour
{
    [Header("Joystick Components")]
    public Transform handle;
    public Transform basePlate;

    [Header("Movement Settings")]
    public float maxTiltAngle = 30f;
    public float maxHandleDistance = 0.2f; // Khoảng cách di chuyển tối đa CỐ ĐỊNH
    public float returnSpeed = 8f;
    public float smoothFactor = 5f;

    [Header("Tilt Settings")]
    [Tooltip("Tỉ lệ giữa độ nghiêng và độ dịch chuyển (1 = nghiêng đầy đủ, 0.5 = nghiêng một nửa, 2 = nghiêng gấp đôi)")]
    public float tiltScale = 1f; // Thêm biến tỉ lệ độ nghiêng

    [Header("Direction Thresholds")]
    [Range(0.1f, 0.9f)]
    public float directionThreshold = 0.5f; // Ngưỡng xác định hướng
    public float deadZone = 0.1f; // Vùng chết

    [Header("Grab Settings")]
    public float grabDistanceThreshold = 0.3f; // Khoảng cách phát hiện nắm CỐ ĐỊNH
    public float releaseDistanceThreshold = 0.4f;

    [Header("Event System")]
    public bool enableEvents = true;

    // Trạng thái hiện tại
    public float CurrentForward { get; private set; }
    public float CurrentBackward { get; private set; }
    public float CurrentRight { get; private set; }
    public float CurrentLeft { get; private set; }

    private Vector3 initialHandleLocalPosition;
    private Quaternion initialHandleLocalRotation;
    private Vector3 targetHandlePosition;
    private Quaternion targetHandleRotation;

    private bool isGrabbing = false;
    private Vector3 grabOffset;
    private GestureResult grabbingHand = null;

    [SerializeField]
    private GameObject _telemetry;
    private Telemetry telemetry;

    // Biến tạm để tránh gọi event liên tục
    private float lastForward;
    private float lastBackward;
    private float lastRight;
    private float lastLeft;

    //Test
    [Header("Control Settings")]
    public float movementSpeed = 2f; // Tốc độ di chuyển cơ bản
    public bool normalizeDiagonal = true; // Chuẩn hóa di chuyển chéo
    public float diagonalSpeedMultiplier = 0.7f; // Giảm tốc khi di chuyển chéo

    void Start()
    {
        if (handle == null)
        {
            Debug.LogError("Handle is not assigned!");
            return;
        }

        initialHandleLocalPosition = handle.localPosition;
        initialHandleLocalRotation = handle.localRotation;

        targetHandlePosition = initialHandleLocalPosition;
        targetHandleRotation = initialHandleLocalRotation;

        _telemetry = GameObject.FindGameObjectWithTag("Telemetry");
        telemetry = _telemetry.GetComponent<Telemetry>();
    }

    void Update()
    {
        CheckFistGrab(); 
        UpdateHandleMovement();
        CalculateDirectionalInput();
        //setControlJoystick();

        //DebugJoystickState();
    }

    private void DebugJoystickState()
    {
        if (Time.frameCount % 60 == 0) // Mỗi 1 giây
        {
            Debug.Log($"=== JOYSTICK DEBUG ===");
            Debug.Log($"IsGrabbing: {isGrabbing}");
            Debug.Log($"Hand Ready: {IsRightHandReady()}");

            if (isGrabbing && grabbingHand != null)
            {
                Debug.Log($"Hand Position: {grabbingHand.position}");
                Debug.Log($"Handle Local Pos: {handle.localPosition}");
                Debug.Log($"Target Handle Pos: {targetHandlePosition}");

                Vector2 direction = new Vector2(
                    handle.localPosition.x / maxHandleDistance,
                    handle.localPosition.z / maxHandleDistance
                );
                Debug.Log($"Normalized Direction: {direction}");

                Debug.Log($"Forward: {CurrentForward:F2}, Backward: {CurrentBackward:F2}");
                Debug.Log($"Right: {CurrentRight:F2}, Left: {CurrentLeft:F2}");
            }
        }
    }

    private void CheckFistGrab()
    {
        bool rightHandReady = IsRightHandReady();

        if (rightHandReady && !isGrabbing)
        {
            StartGrab(GestureProvider.RightHand);
        }
        else if (!rightHandReady && isGrabbing)
        {
            StopGrab();
        }
        else if (isGrabbing && grabbingHand != null)
        {
            if (!IsRightHandReady())
            {
                StopGrab();
            }
            else
            {
                UpdateGrabPosition(GestureProvider.RightHand);
            }
        }
    }

    private bool IsRightHandReady()
    {
        return IsHandReady(GestureProvider.RightHand);
    }

    private bool IsHandReady(GestureResult hand)
    {
        if (hand == null) return false;
        return hand.gesture == GestureType.Fist && IsHandNearHandle(hand.position);
    }

    private bool IsHandNearHandle(Vector3 handPosition)
    {
        if (handle == null) return false;

        // KHÔNG SCALE - khoảng cách cố định
        float distanceToHandle = Vector3.Distance(handPosition, handle.position);
        bool isNear = distanceToHandle <= grabDistanceThreshold;

        // Debug khoảng cách
        if (isNear && Time.frameCount % 30 == 0)
        {
            Debug.Log($"Tay phải gần handle: {distanceToHandle:F2}m (Ngưỡng: {grabDistanceThreshold:F2}m)");
        }

        return isNear;
    }

    private void StartGrab(GestureResult hand)
    {
        if (hand == null) return;

        isGrabbing = true;
        grabbingHand = hand;
        grabOffset = handle.position - hand.position;

        // Reset all inputs
        ResetAllInputs();

        //Debug.Log("Bắt đầu điều khiển bằng TAY PHẢI");
        //Debug.Log($"Vị trí handle: {handle.position}");
        //Debug.Log($"Vị trí tay phải: {hand.position}");
        //Debug.Log($"Tilt Scale: {tiltScale}x");
    }

    private void StopGrab()
    {
        isGrabbing = false;
        grabbingHand = null;
        targetHandlePosition = initialHandleLocalPosition;
        targetHandleRotation = initialHandleLocalRotation;

        // Reset all inputs khi thả
        ResetAllInputs();

        Debug.Log("Kết thúc điều khiển - Thả tay phải");
    }

    private void ResetAllInputs()
    {
        CurrentForward = 0;
        CurrentBackward = 0;
        CurrentRight = 0;
        CurrentLeft = 0;
    }

    private void UpdateGrabPosition(GestureResult hand)
    {
        if (hand == null) return;

        Vector3 currentHandPos = hand.position + grabOffset;
        Vector3 localDirection = basePlate.InverseTransformPoint(currentHandPos) - initialHandleLocalPosition;

        // KHÔNG SCALE - sử dụng maxHandleDistance cố định
        Vector2 horizontalDirection = new Vector2(localDirection.x, localDirection.z);
        horizontalDirection = Vector2.ClampMagnitude(horizontalDirection, maxHandleDistance);

        targetHandlePosition = new Vector3(
            horizontalDirection.x,
            initialHandleLocalPosition.y,
            horizontalDirection.y
        );

        UpdateTargetRotation(horizontalDirection);

        // Kiểm tra nếu tay di chuyển quá xa (KHÔNG SCALE)
        float currentDistance = Vector3.Distance(hand.position, handle.position);
        if (currentDistance > releaseDistanceThreshold)
        {
            StopGrab();
        }
    }

    private void UpdateTargetRotation(Vector2 direction)
    {
        if (direction.magnitude > 0.01f)
        {
            // Áp dụng tỉ lệ độ nghiêng
            float scaledMaxTiltAngle = maxTiltAngle * tiltScale;

            // SỬA LẠI: Nghiêng đúng hướng
            // - Trục X: nghiêng về phía trước/sau (dựa trên direction.y)
            // - Trục Z: nghiêng trái/phải (dựa trên direction.x)
            float tiltX = Mathf.Clamp(-direction.y / maxHandleDistance * scaledMaxTiltAngle, -scaledMaxTiltAngle, scaledMaxTiltAngle);
            float tiltZ = Mathf.Clamp(-direction.x / maxHandleDistance * scaledMaxTiltAngle, -scaledMaxTiltAngle, scaledMaxTiltAngle);
            targetHandleRotation = Quaternion.Euler(tiltX, 0, tiltZ);

        }
        else
        {
            targetHandleRotation = initialHandleLocalRotation;
        }
    }

    private void UpdateHandleMovement()
    {
        handle.localPosition = Vector3.Lerp(handle.localPosition, targetHandlePosition, Time.deltaTime * smoothFactor);
        handle.localRotation = Quaternion.Slerp(handle.localRotation, targetHandleRotation, Time.deltaTime * smoothFactor);
    }

    private void CalculateDirectionalInput()
    {
        if (!isGrabbing) return;

        // Lấy input direction (KHÔNG SCALE)
        Vector2 direction = new Vector2(
            handle.localPosition.x / maxHandleDistance,
            handle.localPosition.z / maxHandleDistance
        );

        float magnitude = direction.magnitude;

        // Áp dụng deadzone
        if (magnitude < deadZone)
        {
            direction = Vector2.zero;
            magnitude = 0;
        }
        else
        {
            direction = direction.normalized * ((magnitude - deadZone) / (1 - deadZone));
            magnitude = direction.magnitude;
        }

        // Tính toán 4 hướng riêng biệt
        CalculateForwardInput(direction.y, magnitude);
        CalculateBackwardInput(direction.y, magnitude);
        CalculateRightInput(direction.x, magnitude);
        CalculateLeftInput(direction.x, magnitude);

        // Debug input
        if (Time.frameCount % 30 == 0 && magnitude > 0.1f)
        {
            Debug.Log($"Input từ tay phải: ({direction.x:F2}, {direction.y:F2})");
            Debug.Log($"Tilt Scale: {tiltScale}x - Max Tilt: {maxTiltAngle * tiltScale}°");
        }
    }

    private void CalculateForwardInput(float yInput, float magnitude)
    {
        if (yInput > directionThreshold)
        {
            CurrentForward = Mathf.Clamp01((yInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentForward = 0;
        }
    }

    private void CalculateBackwardInput(float yInput, float magnitude)
    {
        if (yInput < -directionThreshold)
        {
            CurrentBackward = Mathf.Clamp01((-yInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentBackward = 0;
        }
    }

    private void CalculateRightInput(float xInput, float magnitude)
    {
        if (xInput > directionThreshold)
        {
            CurrentRight = Mathf.Clamp01((xInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentRight = 0;
        }
    }

    private void CalculateLeftInput(float xInput, float magnitude)
    {
        if (xInput < -directionThreshold)
        {
            CurrentLeft = Mathf.Clamp01((-xInput - directionThreshold) / (1 - directionThreshold)) * magnitude;
        }
        else
        {
            CurrentLeft = 0;
        }
    }

    private void setControlJoystick()
    {
        if (CurrentForward == 0 &&
            CurrentBackward == 0 &&
            CurrentRight == 0 &&
            CurrentLeft == 0)
        {
            // Reset tất cả giá trị trước
            telemetry.TeleVxControl = 0;
            telemetry.TeleVyControl = 0;
            telemetry.TeleVzControl = 500;
            telemetry.TeleYawRateControl = 0;
        }

        //if (!isGrabbing) return;


        if (IsMovingForward())  // TRƯỜNG HỢP 1: TIẾN
        {
            if (telemetry.TeleVyControl < 700)
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl += 2;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = 700;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            Debug.Log($"Đang tiến");
        }


        if (IsMovingBackward()) // TRƯỜNG HỢP 2: LÙI
        {
            if (telemetry.TeleVyControl > -700)
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl -= 2;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = -700;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            Debug.Log($"Đang lùi");
        }


        if (IsMovingRight()) // TRƯỜNG HỢP 3: PHẢI
        {
            if (telemetry.TeleVxControl < 700)
            {
                telemetry.TeleVxControl += 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            Debug.Log($"Đang sang phải");
        }


        if (IsMovingLeft()) // TRƯỜNG HỢP 4: TRÁI
        {
            if (telemetry.TeleVxControl > -700)
            {
                telemetry.TeleVxControl -= 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = -700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            Debug.Log($"Đang sang trái");
        }

        // Debug tổng hợp
        if (Time.frameCount % 60 == 0 && (IsMovingForward() || IsMovingBackward() || IsMovingRight() || IsMovingLeft()))
        {
            string directions = "";
            if (IsMovingForward()) directions += "TIẾN ";
            if (IsMovingBackward()) directions += "LÙI ";
            if (IsMovingRight()) directions += "PHẢI ";
            if (IsMovingLeft()) directions += "TRÁI ";
            Debug.Log($"Hướng đang kích hoạt: {directions}");
        }
    }

    // API để các script khác sử dụng
    public bool IsMovingForward() => CurrentForward > 0;
    public bool IsMovingBackward() => CurrentBackward > 0;
    public bool IsMovingRight() => CurrentLeft > 0;
    public bool IsMovingLeft() => CurrentRight > 0;

    public Vector2 GetCombinedInput()
    {
        return new Vector2(
            CurrentRight > 0 ? CurrentRight : -CurrentLeft,
            CurrentForward > 0 ? CurrentForward : -CurrentBackward
        );
    }
}