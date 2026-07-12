using UnityEngine;
using UnityEngine.InputSystem; // 신규 인풋 시스템 네임스페이스

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 40.0f;
    public float zoomSpeed = 5.0f;
    public float minHeight = 20.0f;
    public float maxHeight = 250.0f;
    
    public Vector2 minBounds = new Vector2(-200, -200);
    public Vector2 maxBounds = new Vector2(2200, 2200);

    void Update()
    {
        float h = 0f;
        float v = 0f;

        // 1. 키보드 입력 체크 (New Input System)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h = -1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h = 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v = 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v = -1f;
        }

        // Shift 키를 누르면 5배 가속 (스프린트)
        float currentSpeed = moveSpeed;
        if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
        {
            currentSpeed = moveSpeed * 5.0f;
        }

        Vector3 moveDir = new Vector3(h, 0, v).normalized;
        if (moveDir.magnitude > 0.1f)
        {
            Vector3 targetMove = new Vector3(moveDir.x, 0, moveDir.z) * currentSpeed * Time.deltaTime;
            transform.position += targetMove;
        }

        // 2. 마우스 휠 입력 체크 (New Input System)
        if (Mouse.current != null)
        {
            // New Input System의 마우스 스크롤 y값은 보통 120 단위(또는 마우스 감도마다 다름)이므로 가볍게 보정합니다
            float scroll = Mouse.current.scroll.ReadValue().y * 0.005f; 
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 zoomDelta = transform.forward * scroll * zoomSpeed * 10f;
                Vector3 nextPos = transform.position + zoomDelta;

                if (nextPos.y >= minHeight && nextPos.y <= maxHeight)
                {
                    transform.position = nextPos;
                }
            }
        }

        // 3. XZ 경계 클램프
        Vector3 clampedPos = transform.position;
        clampedPos.x = Mathf.Clamp(clampedPos.x, minBounds.x, maxBounds.x);
        clampedPos.z = Mathf.Clamp(clampedPos.z, minBounds.y, maxBounds.y);
        transform.position = clampedPos;
    }
}
