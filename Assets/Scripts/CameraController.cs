using UnityEngine;
using UnityEngine.InputSystem; // 신규 인풋 시스템 네임스페이스

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 40.0f;
    public float zoomSpeed = 15.0f; // 키보드/마우스 공용 줌 감도
    public float minHeight = 5.0f;  // 더 가깝게 줌인 가능하도록 변경
    public float maxHeight = 250.0f;
    
    public Vector2 minBounds = new Vector2(-200, -200);
    public Vector2 maxBounds = new Vector2(2200, 2200);

    void Update()
    {
        float h = 0f;
        float v = 0f;

        // 1. 키보드 이동 입력 체크 (New Input System)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h = -1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h = 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v = 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v = -1f;
        }

        // Shift 키를 누르면 3배 가속 (스프린트)
        float currentSpeed = moveSpeed;
        if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
        {
            currentSpeed = moveSpeed * 3.0f;
        }

        Vector3 moveDir = new Vector3(h, 0, v).normalized;
        if (moveDir.magnitude > 0.1f)
        {
            Vector3 targetMove = new Vector3(moveDir.x, 0, moveDir.z) * currentSpeed * Time.deltaTime;
            transform.position += targetMove;
        }

        // 2. 키보드 줌 입력 체크 (Q / E, PageUp / PageDown, Numpad + / -)
        float keyboardZoom = 0f;
        if (Keyboard.current != null)
        {
            // Q, PageUp, +, = 키로 줌인
            if (Keyboard.current.qKey.isPressed || Keyboard.current.pageUpKey.isPressed || Keyboard.current.numpadPlusKey.isPressed || Keyboard.current.equalsKey.isPressed)
            {
                keyboardZoom = 1.0f;
            }
            // E, PageDown, -, _ 키로 줌아웃
            if (Keyboard.current.eKey.isPressed || Keyboard.current.pageDownKey.isPressed || Keyboard.current.numpadMinusKey.isPressed || Keyboard.current.minusKey.isPressed)
            {
                keyboardZoom = -1.0f;
            }
        }

        if (Mathf.Abs(keyboardZoom) > 0.01f)
        {
            // keyboardZoom = 1.0f 이면 Y가 감소(줌인), -1.0f 이면 Y가 증가(줌아웃)
            float nextY = transform.position.y - keyboardZoom * zoomSpeed * 5.0f * Time.deltaTime;
            nextY = Mathf.Clamp(nextY, minHeight, maxHeight);

            Vector3 nextPos = transform.position;
            nextPos.y = nextY;
            transform.position = nextPos;
        }

        // 3. 마우스 휠 줌 입력 체크 (New Input System)
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y * 0.005f; 
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // scroll > 0 이면 줌인(Y 감소), scroll < 0 이면 줌아웃(Y 증가)
                float nextY = transform.position.y - scroll * zoomSpeed * 1.5f;
                nextY = Mathf.Clamp(nextY, minHeight, maxHeight);

                Vector3 nextPos = transform.position;
                nextPos.y = nextY;
                transform.position = nextPos;
            }
        }

        // 4. XZ 경계 클램프
        Vector3 clampedPos = transform.position;
        clampedPos.x = Mathf.Clamp(clampedPos.x, minBounds.x, maxBounds.x);
        clampedPos.z = Mathf.Clamp(clampedPos.z, minBounds.y, maxBounds.y);
        transform.position = clampedPos;
    }
}
