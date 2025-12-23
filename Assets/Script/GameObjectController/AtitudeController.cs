using UnityEngine;
using ViveHandTracking;
using System;

public class AtitudeController : MonoBehaviour
{
    [Header("Lever Components")]
    public Transform leverHandle; // Phần tay cầm di chuyển
    public Transform leverBase;   // Phần đế cố định
    public Transform leverPivot;  // Điểm xoay cho tay cầm

    [Header("Movement Settings")]
    public float maxForwardAngle = 30f; // Góc nghiêng tối đa về phía trước
    public float maxBackwardAngle = 30f; // Góc nghiêng tối đa về phía sau
    public float rotationSpeed = 5f;
    public float returnSpeed = 8f;

    [Header("Grab Settings")]
    public float grabDistanceThreshold = 0.3f;
    public float releaseDistanceThreshold = 0.4f;

    [Header("Event Settings")]
    [Tooltip("Ngưỡng để xác định trạng thái Bật")]
    public float onThreshold = 0.7f;
    [Tooltip("Ngưỡng để xác định trạng thái Tắt")]
    public float offThreshold = 0.3f;

    [Header("Debug Settings")]
    public bool enableDebug = true;
    public float debugUpdateInterval = 1f; // Giây


    // Trạng thái
    public float CurrentValue { get; private set; }
    public bool IsOn { get; private set; }
    public bool IsGrabbing => isGrabbing;
    public LeverDirection CurrentDirection { get; private set; }

    private Quaternion initialHandleRotation;
    private Quaternion targetHandleRotation;
    private bool isGrabbing = false;
    private Vector3 grabOffset;
    private GestureResult grabbingHand = null;
    private float debugTimer = 0f;
    private LeverDirection previousDirection;
    //private int currentState; // Lưu trạng thái hiện tại dạng int

    // Debug info
    private float lastDistanceToLever = 0f;
    private Vector3 lastHandPosition = Vector3.zero;
    private string lastHandState = "None";

    [SerializeField]
    private Telemetry telemetry;
    [SerializeField]
    private Communication communication;
    [SerializeField]
    private FistControlledJoystick fistJoystick;

    private int state = 0;
    private bool autoMode = false;

    public enum LeverDirection
    {
        Neutral,    // Nằm im (giữa)
        Forward,    // Tiến về phía trước (nghiêng về trước)
        Backward    // Lùi về phía sau (nghiêng về sau)
    }

    void Start()
    {
        if (leverHandle == null)
        {
            Debug.LogError("LeverHandle is not assigned!");
            return;
        }

        if (leverPivot == null)
        {
            leverPivot = leverBase;
        }

        initialHandleRotation = leverHandle.rotation;
        targetHandleRotation = initialHandleRotation;
        CurrentDirection = LeverDirection.Neutral;
        previousDirection = LeverDirection.Neutral;

        if (communication == null)
        {
            // Try to find Communication in parent
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in hierarchy.");
                enabled = false;
                return;
            }
        }

        var communicationObj = GameObject.FindGameObjectWithTag("Communication");
        if (communicationObj == null)
        {
            Debug.LogError("Communication GameObject not found. Please check the tag.");
            enabled = false;
            return;
        }

        // Find Telemetry as a child of Communication
        var telemetryTransform = communicationObj.transform.Find("Telemetry");
        if (telemetryTransform == null)
        {
            Debug.LogError("Telemetry child not found under Communication GameObject.");
            enabled = false;
            return;
        }

        telemetry = telemetryTransform.GetComponent<Telemetry>();
        if (telemetry == null)
        {
            Debug.LogError("Telemetry component not found on Telemetry GameObject.");
            enabled = false;
            return;
        }

        if (fistJoystick == null)
            fistJoystick = FindObjectOfType<FistControlledJoystick>();

        telemetry.VelX = 0f;
        telemetry.VelY = 0f;
        telemetry.VelZ = 0.0f;
        telemetry.VelYawRate = 0f;
        telemetry.VelFrame = 1;
        
        telemetry.BaseSV = 90f;
        telemetry.ShoulderSV = 30f;
        telemetry.ElbowSV = 0f;
        telemetry.WristSV = 180f;

        telemetry.TeleVxControl = 0;      // Roll: [-1000, 1000]
        telemetry.TeleVyControl = 0;      // Pitch: [-1000, 1000]
        telemetry.TeleVzControl = 500;      // Throttle: [-1000, 1000]
        telemetry.TeleYawRateControl = 0; // Yaw: [-1000, 1000]

        //Debug.Log($"Góc nghiêng tối đa: Trước {maxForwardAngle}°, Sau {maxBackwardAngle}°");
    }

    void Update()
    {
        CheckLeftHandGrab();
        UpdateHandleMovement();    // Cập nhật vị trí cần gạt
        CalculateLeverValue();     // LUÔN tính toán giá trị
        CheckLeverDirection();     // LUÔN kiểm tra hướng
        setControlAtitude();

        if (enableDebug && Time.frameCount % 60 == 0) // Mỗi giây
        {
            Debug.Log($"Angle: {leverHandle.localEulerAngles.x:F1}°, Value: {CurrentValue:F2}, Direction: {CurrentDirection}");
        }

        //if (enableDebug)
        //{
        //    UpdateDebugInfo();
        //}
    }

    private void CheckLeftHandGrab()
    {
        bool leftHandReady = IsLeftHandReady();

        if (leftHandReady && !isGrabbing)
        {
            StartGrab(GestureProvider.LeftHand);
        }
        else if (!leftHandReady && isGrabbing)
        {
            StopGrab();
        }
        else if (isGrabbing && grabbingHand != null)
        {
            if (!IsLeftHandReady())
            {
                StopGrab();
            }
            else
            {
                UpdateGrabPosition(GestureProvider.LeftHand);
            }
        }
    }

    private bool IsLeftHandReady()
    {
        return IsHandReady(GestureProvider.LeftHand);
    }

    private bool IsHandReady(GestureResult hand)
    {
        if (hand == null)
        {
            lastHandState = "Hand is null";
            return false;
        }

        bool isFist = hand.gesture == GestureType.Fist;
        bool isNear = IsHandNearLever(hand.position);

        lastHandState = $"Gesture: {hand.gesture}, Near: {isNear}";
        lastHandPosition = hand.position;

        return isFist && isNear;
    }

    private bool IsHandNearLever(Vector3 handPosition)
    {
        if (leverHandle == null) return false;

        float distanceToLever = Vector3.Distance(handPosition, leverHandle.position);
        lastDistanceToLever = distanceToLever;

        return distanceToLever <= grabDistanceThreshold;
    }

    private void StartGrab(GestureResult hand)
    {
        if (hand == null) return;

        isGrabbing = true;
        grabbingHand = hand;
        grabOffset = leverPivot.position - hand.position;

        //Debug.Log("Bắt đầu điều khiển cầu dao bằng TAY TRÁI");
        //SendUDPMessage("GRAB_START"); // Gửi message khi bắt đầu nắm
    }

    private void StopGrab()
    {
        isGrabbing = false;
        grabbingHand = null;
        targetHandleRotation = initialHandleRotation;

        Debug.Log("Kết thúc điều khiển cầu dao");
        //SendUDPMessage("GRAB_END"); // Gửi message khi kết thúc nắm
    }

    private void UpdateGrabPosition(GestureResult hand)
    {
        if (hand == null) return;

        Vector3 targetHandPos = hand.position + grabOffset;
        Vector3 directionFromPivot = targetHandPos - leverPivot.position;
        directionFromPivot.y = 0; // Vẫn giữ chiều cao cố định

        if (directionFromPivot.magnitude > 0.01f)
        {
            float distance = directionFromPivot.magnitude;
            float maxDistance = Mathf.Max(maxForwardAngle, maxBackwardAngle) * 0.01f;
            float angle = Mathf.Clamp(distance / maxDistance, 0f, 1f) * Mathf.Max(maxForwardAngle, maxBackwardAngle);

            // SỬA: Dùng trục Z thay vì trục X
            float dotProduct = Vector3.Dot(directionFromPivot.normalized, leverPivot.forward);

            if (dotProduct > 0)
            {
                // Tiến về phía trước - xoay quanh trục X (góc âm)
                targetHandleRotation = initialHandleRotation * Quaternion.Euler(-angle, 0, 0);
            }
            else
            {
                // Lùi về phía sau - xoay quanh trục X (góc dương)
                targetHandleRotation = initialHandleRotation * Quaternion.Euler(angle, 0, 0);
            }
        }
        else
        {
            targetHandleRotation = initialHandleRotation;
        }

        float currentDistance = Vector3.Distance(hand.position, leverHandle.position);
        if (currentDistance > releaseDistanceThreshold)
        {
            Debug.Log($"Khoảng cách vượt ngưỡng: {currentDistance:F2}m > {releaseDistanceThreshold:F2}m");
            StopGrab();
        }
    }

    private void UpdateHandleMovement()
    {
        if (isGrabbing)
        {
            leverHandle.rotation = Quaternion.Lerp(
                leverHandle.rotation,
                targetHandleRotation,
                Time.deltaTime * rotationSpeed
            );
        }
        else
        {
            leverHandle.rotation = Quaternion.Lerp(
                leverHandle.rotation,
                initialHandleRotation,
                Time.deltaTime * returnSpeed
            );
        }

        // LUÔN cập nhật giá trị và hướng, dù đang grab hay không
        CalculateLeverValue();
        CheckLeverDirection();
    }

    private void CalculateLeverValue()
    {
        // Lấy góc xoay hiện tại quanh trục X
        Vector3 currentEuler = leverHandle.localEulerAngles;
        float currentAngle = currentEuler.x;

        // Chuyển đổi góc về khoảng -180 đến 180
        if (currentAngle > 180f) currentAngle -= 360f;

        float newValue = 0f;

        if (currentAngle < 0f)
        {
            // Nghiêng về phía TRƯỚC (góc âm) → Forward
            newValue = Mathf.Clamp(currentAngle / -maxForwardAngle, 0f, 1f);
        }
        else if (currentAngle > 0f)
        {
            // Nghiêng về phía SAU (góc dương) → Backward
            newValue = Mathf.Clamp(currentAngle / -maxBackwardAngle, -1f, 0f);
        }
        // else currentAngle = 0f → newValue = 0f (Neutral)

        if (Mathf.Abs(newValue - CurrentValue) > 0.01f)
        {
            CurrentValue = newValue;
        }
    }

    private void CheckLeverDirection()
    {
        LeverDirection newDirection;

        if (Mathf.Abs(CurrentValue) < 0.3f)
        {
            newDirection = LeverDirection.Neutral;
        }
        else if (CurrentValue >= 0.3f)
        {
            newDirection = LeverDirection.Backward;    // Value > 0.3 → Backward
        }
        else
        {
            newDirection = LeverDirection.Forward;   // Value < 0 → Forward
        }

        if (newDirection != CurrentDirection)
        {
            CurrentDirection = newDirection;
        }
    }

    public void setControlAtitude()
    {

        if (CurrentDirection == LeverDirection.Forward) //&& Mathf.Abs(CurrentValue) > onThreshold
        {
            if (telemetry.TeleVzControl < 700)
            {
                telemetry.TeleVzControl += 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVzControl = 700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
        }
        else if (CurrentDirection == LeverDirection.Backward) //&& Mathf.Abs(CurrentValue) > onThreshold
        {
            if (telemetry.TeleVzControl > -700)
            {
                telemetry.TeleVzControl -= 2;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVzControl = -700;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVxControl = 0;
                telemetry.TeleYawRateControl = 0;
            }
        }
        else if (CurrentDirection == LeverDirection.Neutral)
        {

            if (fistJoystick != null && fistJoystick.enabled)
            {
                if (fistJoystick.CurrentForward == 0 &&
                    fistJoystick.CurrentBackward == 0 &&
                    fistJoystick.CurrentRight == 0 &&
                    fistJoystick.CurrentLeft == 0)
                {
                    telemetry.TeleVxControl = 0;
                    telemetry.TeleVyControl = 0;
                }

                //if (!isGrabbing) return;


                if (fistJoystick.IsMovingRight())  // TRƯỜNG HỢP 1: Sang phai
                {
                    if (telemetry.TeleVyControl < 700)
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl += 2;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl = 700;
                    }
                    Debug.Log($"Đang tiến");
                }


                if (fistJoystick.IsMovingLeft()) // TRƯỜNG HỢP 2: Sang trai
                {
                    if (telemetry.TeleVyControl > -700)
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl -= 2;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 0;
                        telemetry.TeleVyControl = -700;
                    }
                    Debug.Log($"Đang lùi");
                }


                if (fistJoystick.IsMovingForward()) // TRƯỜNG HỢP 3: Tien
                {
                    if (telemetry.TeleVxControl < 700)
                    {
                        telemetry.TeleVxControl += 2;
                        telemetry.TeleVyControl = 0;
                    }
                    else
                    {
                        telemetry.TeleVxControl = 700;
                        telemetry.TeleVyControl = 0;
                    }
                    Debug.Log($"Đang sang phải");
                }


                if (fistJoystick.IsMovingBackward()) // TRƯỜNG HỢP 4: Lui
                {
                    if (telemetry.TeleVxControl > -700)
                    {
                        telemetry.TeleVxControl -= 2;
                        telemetry.TeleVyControl = 0;
                    }
                    else
                    {
                        telemetry.TeleVxControl = -700;
                        telemetry.TeleVyControl = 0;
                    }
                    Debug.Log($"Đang sang trái");
                }

                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }
            else
            {
                telemetry.TeleVxControl = 0;
                telemetry.TeleVyControl = 0;
                telemetry.TeleVzControl = 500;
                telemetry.TeleYawRateControl = 0;
            }

            if (telemetry.TeleSwitchMode == 0) // Manual control
            {
                communication.HandleHoldControl();
            }
            else if (telemetry.TeleSwitchMode == 1) // Altitude control
            {
                communication.HandleHoldAltctlControl();
            }
            else if (telemetry.TeleSwitchMode == 2) // Position control POSCTL
            {
                communication.HandleHoldPosctlControl();
            }
            else if (telemetry.TeleSwitchMode == 3) // Offboard control
            {
                if (telemetry.VelActive == false)
                {
                    communication.HandleOffboardControl();
                }
                else
                {
                    communication.SendVelocityControl();
                }

            }
        }
    }

    //private void UpdateDebugInfo()
    //{
    //    debugTimer += Time.deltaTime;

    //    if (debugTimer >= debugUpdateInterval)
    //    {
    //        debugTimer = 0f;

    //        if (GestureProvider.LeftHand != null)
    //        {
    //            Debug.Log($"\n=== DEBUG INFO ===");
    //            Debug.Log($"Tay trái: {GestureProvider.LeftHand.gesture}");
    //            Debug.Log($"Ngưỡng grab: {grabDistanceThreshold:F2}m");
    //            Debug.Log($"Đang nắm: {isGrabbing}");
    //            Debug.Log($"Giá trị cầu dao: {CurrentValue:F2}");
    //            Debug.Log($"Hướng: {CurrentDirection}");
    //            Debug.Log($"==================\n");
    //        }
    //        else
    //        {
    //            Debug.Log("Không phát hiện tay trái");
    //        }
    //    }
    //}

    public Vector3 GetHandPosition()
    {
        return GestureProvider.LeftHand != null ? GestureProvider.LeftHand.position : Vector3.zero;
    }

    public float GetDistanceToLever()
    {
        return lastDistanceToLever;
    }

    void OnDestroy()
    {

    }
}