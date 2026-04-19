using UnityEngine;
using UnityEngine.InputSystem;

public class CameraDualModeController : MonoBehaviour
{
    public enum CameraMode
    {
        Orbit,
        Free
    }

    [Header("Объекты")]
    public Transform target;
    public Transform orbitAnchor;

    [Header("Режим")]
    public CameraMode currentMode = CameraMode.Orbit;

    [Header("Orbit")]
    public float orbitKeyboardSpeed = 60f;
    public float zoomSpeed = 2f;

    [Header("Дистанция Orbit")]
    public float distance = 8f;
    public float minDistance = 3f;
    public float maxDistance = 14f;

    [Header("Углы Orbit")]
    public float orbitYaw = 45f;
    public float orbitPitch = 20f;
    public float minOrbitPitch = 5f;
    public float maxOrbitPitch = 60f;

    [Header("Free Camera")]
    public float freeMoveSpeed = 5f;
    public float freeLookSpeed = 0.15f;
    public float freeYaw = 0f;
    public float freePitch = 0f;
    public float minFreePitch = -80f;
    public float maxFreePitch = 80f;

    [Header("Границы помещения")]
    public int minX = -8;
    public int maxX = 8;
    public int minY = 1;
    public int maxY = 9;
    public int minZ = -8;
    public int maxZ = 8;

    [Header("Отступ от стен")]
    public float wallPadding = 0.05f;

    private Mouse mouse;
    private Keyboard keyboard;
    private bool inputLocked;
    private bool freeLookDragActive;

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("Не назначен target.");
            enabled = false;
            return;
        }

        mouse = Mouse.current;
        keyboard = Keyboard.current;

        if (mouse == null || keyboard == null)
        {
            Debug.LogError("Mouse или Keyboard не найдены в Input System.");
            enabled = false;
            return;
        }

        if (currentMode == CameraMode.Orbit)
        {
            if (orbitAnchor == null)
            {
                Debug.LogError("Не назначен orbitAnchor.");
                enabled = false;
                return;
            }

            ApplyOrbitFromAnchor();
        }
        else
        {
            transform.position = ClampPositionToRoom(transform.position);
            CaptureFreeAnglesFromCurrentRotation();
        }
    }

    private void Update()
    {
        if (inputLocked)
        {
            freeLookDragActive = false;
            return;
        }

        if (keyboard.qKey.wasPressedThisFrame)
        {
            ToggleMode();
        }

        if (currentMode == CameraMode.Orbit)
        {
            HandleOrbitKeyboardRotation();
            HandleOrbitZoom();
            UpdateOrbitCameraPosition();
        }
        else
        {
            HandleFreeLook();
            HandleFreeMovement();
        }
    }

    private void ToggleMode()
    {
        if (currentMode == CameraMode.Orbit)
        {
            currentMode = CameraMode.Free;
            CaptureFreeAnglesFromCurrentRotation();
        }
        else
        {
            currentMode = CameraMode.Orbit;
            ApplyOrbitFromAnchor();
        }
    }

    private void CaptureFreeAnglesFromCurrentRotation()
    {
        Vector3 euler = transform.eulerAngles;
        freeYaw = euler.y;
        freePitch = NormalizeAngle(euler.x);
    }

    private void ApplyOrbitFromAnchor()
    {
        if (orbitAnchor == null)
            return;

        Vector3 anchorPosition = ClampPositionToRoom(orbitAnchor.position);

        if ((anchorPosition - target.position).sqrMagnitude < 0.0001f)
        {
            anchorPosition = target.position + new Vector3(0f, 2f, -5f);
            anchorPosition = ClampPositionToRoom(anchorPosition);
        }

        transform.position = anchorPosition;
        transform.LookAt(target.position);

        distance = Vector3.Distance(transform.position, target.position);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Vector3 euler = transform.eulerAngles;
        orbitYaw = euler.y;
        orbitPitch = NormalizeAngle(euler.x);
        orbitPitch = Mathf.Clamp(orbitPitch, minOrbitPitch, maxOrbitPitch);

        UpdateOrbitCameraPosition();
    }

    private void HandleOrbitKeyboardRotation()
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed) horizontal += 1f;
        if (keyboard.dKey.isPressed) horizontal -= 1f;
        if (keyboard.wKey.isPressed) vertical -= 1f;
        if (keyboard.sKey.isPressed) vertical += 1f;

        orbitYaw += horizontal * orbitKeyboardSpeed * Time.deltaTime;
        orbitPitch -= vertical * orbitKeyboardSpeed * Time.deltaTime;
        orbitPitch = Mathf.Clamp(orbitPitch, minOrbitPitch, maxOrbitPitch);
    }

    private void HandleOrbitZoom()
    {
        float scrollY = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scrollY) < 0.01f) return;

        distance -= scrollY * zoomSpeed * 0.01f;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private void UpdateOrbitCameraPosition()
    {
        Vector3 center = target.position;

        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 direction = rotation * Vector3.forward;

        // нужна позиция камеры позади её forward, чтобы она смотрела на центр
        direction = -direction;

        float actualDistance = GetAllowedOrbitDistance(center, direction, distance);
        Vector3 newPosition = center + direction * actualDistance;

        transform.position = ClampPositionToRoom(newPosition);
        transform.LookAt(center);
    }

    private float GetAllowedOrbitDistance(Vector3 center, Vector3 direction, float desiredDistance)
    {
        float requestedDistance = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
        float maxAllowedDistance = float.PositiveInfinity;

        ReduceAllowedDistance(ref maxAllowedDistance, direction.x, center.x, minX + wallPadding, maxX - wallPadding);
        ReduceAllowedDistance(ref maxAllowedDistance, direction.y, center.y, minY + wallPadding, maxY - wallPadding);
        ReduceAllowedDistance(ref maxAllowedDistance, direction.z, center.z, minZ + wallPadding, maxZ - wallPadding);

        if (float.IsInfinity(maxAllowedDistance))
            return requestedDistance;

        maxAllowedDistance = Mathf.Max(0f, maxAllowedDistance);
        return Mathf.Min(requestedDistance, maxAllowedDistance);
    }

    private void ReduceAllowedDistance(ref float currentMax, float dirComponent, float centerComponent, float minBound, float maxBound)
    {
        if (Mathf.Abs(dirComponent) < 0.0001f)
            return;

        float distanceToWall = dirComponent > 0f
            ? (maxBound - centerComponent) / dirComponent
            : (minBound - centerComponent) / dirComponent;

        if (distanceToWall >= 0f)
            currentMax = Mathf.Min(currentMax, distanceToWall);
    }

    private void HandleFreeLook()
    {
        if (!mouse.leftButton.isPressed)
        {
            freeLookDragActive = false;
            return;
        }

        if (!freeLookDragActive || mouse.leftButton.wasPressedThisFrame)
        {
            CaptureFreeAnglesFromCurrentRotation();
            freeLookDragActive = true;
            return;
        }

        Vector2 delta = mouse.delta.ReadValue();

        freeYaw += delta.x * freeLookSpeed;
        freePitch -= delta.y * freeLookSpeed;
        freePitch = Mathf.Clamp(freePitch, minFreePitch, maxFreePitch);

        transform.rotation = Quaternion.Euler(freePitch, freeYaw, 0f);
    }

    public void BeginScriptedControl(bool forceFreeMode = true)
    {
        inputLocked = true;
        freeLookDragActive = false;

        if (forceFreeMode)
            currentMode = CameraMode.Free;

        SyncFromCurrentTransform();
    }

    public void EndScriptedControl()
    {
        SyncFromCurrentTransform();
        inputLocked = false;
        freeLookDragActive = false;
    }

    public void SyncFromCurrentTransform()
    {
        if (currentMode == CameraMode.Orbit)
        {
            SyncOrbitStateFromCurrentTransform();
            return;
        }

        CaptureFreeAnglesFromCurrentRotation();
    }

    private void SyncOrbitStateFromCurrentTransform()
    {
        if (target == null)
            return;

        Vector3 offset = transform.position - target.position;

        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = -transform.forward * Mathf.Max(minDistance, distance);
        }

        distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);

        Quaternion lookRotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        Vector3 euler = lookRotation.eulerAngles;

        orbitYaw = euler.y;
        orbitPitch = Mathf.Clamp(NormalizeAngle(euler.x), minOrbitPitch, maxOrbitPitch);
    }

    private void HandleFreeMovement()
    {
        Vector3 move = Vector3.zero;

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        if (keyboard.wKey.isPressed) move += forward;
        if (keyboard.sKey.isPressed) move -= forward;
        if (keyboard.aKey.isPressed) move -= right;
        if (keyboard.dKey.isPressed) move += right;
        if (keyboard.spaceKey.isPressed) move += Vector3.up;
        if (keyboard.leftShiftKey.isPressed) move += Vector3.down;
        if (move == Vector3.zero) return;

        move.Normalize();

        Vector3 newPosition = transform.position + move * freeMoveSpeed * Time.deltaTime;
        transform.position = ClampPositionToRoom(newPosition);
    }

    private Vector3 ClampPositionToRoom(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, minX + wallPadding, maxX - wallPadding);
        pos.y = Mathf.Clamp(pos.y, minY + wallPadding, maxY - wallPadding);
        pos.z = Mathf.Clamp(pos.z, minZ + wallPadding, maxZ - wallPadding);
        return pos;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
