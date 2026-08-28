using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class SorkenDoorTestPlayer : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4.2f;
    [SerializeField] private float sprintSpeed = 6.2f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float gravity = -22f;

    private CharacterController _controller;
    private Camera _camera;
    private float _pitch;
    private float _verticalSpeed;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _camera = GetComponentInChildren<Camera>();
        LockCursor(true);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            LockCursor(Cursor.lockState != CursorLockMode.Locked);

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, look.x, 0f);
            _pitch = Mathf.Clamp(_pitch - look.y, -80f, 80f);
            if (_camera != null) _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        float speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : moveSpeed;
        Vector3 horizontal = (transform.forward * input.y + transform.right * input.x) * speed;

        if (_controller.isGrounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
        _verticalSpeed += gravity * Time.deltaTime;
        horizontal.y = _verticalSpeed;
        _controller.Move(horizontal * Time.deltaTime);
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
