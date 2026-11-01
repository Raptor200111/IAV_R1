using UnityEngine;
using UnityEngine.InputSystem;

public class CameraPOV : MonoBehaviour
{
    [SerializeField] float mouseSensitivity = 0.15f;
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float pitchMin = -89f;
    [SerializeField] float pitchMax = 89f;

    float yaw;
    float pitch;

    void Start()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
        if (pitch > 180f)
            pitch -= 360f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        Vector2 look = Mouse.current.delta.ReadValue();
        yaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // WASD en plano XZ (W = delante según yaw de la cámara, sin subir/bajar)
        float forward = 0f;
        if (Keyboard.current.wKey.isPressed)
            forward += 1f;
        if (Keyboard.current.sKey.isPressed)
            forward -= 1f;

        float strafe = 0f;
        if (Keyboard.current.dKey.isPressed)
            strafe += 1f;
        if (Keyboard.current.aKey.isPressed)
            strafe -= 1f;

        Vector3 forwardXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Vector3 rightXZ = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (forwardXZ.sqrMagnitude > 0.0001f)
            forwardXZ.Normalize();
        if (rightXZ.sqrMagnitude > 0.0001f)
            rightXZ.Normalize();

        // Espacio / Shift: vertical mundo
        float vertical = 0f;
        if (Keyboard.current.spaceKey.isPressed)
            vertical += 1f;
        if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
            vertical -= 1f;

        Vector3 motion = forwardXZ * forward + rightXZ * strafe + Vector3.up * vertical;
        transform.position += motion * (moveSpeed * Time.deltaTime);
    }
}
