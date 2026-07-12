using UnityEngine;
using MundusVivens.Prototype.Protos; // Protobuf 네임스페이스
using Vector3 = UnityEngine.Vector3;

public class NpcController : MonoBehaviour
{
    public uint NpcId { get; private set; }
    public string DisplayName { get; private set; }

    [Header("UI / Visuals")]
    public TMPro.TextMeshPro nameText;
    public TMPro.TextMeshPro statusText; // 감정 및 활동 표시용

    public NpcSnapshot LastSnapshot { get; private set; }

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;

    // 내부 상태
    private Vector3 _targetPosition;
    private string _currentEmotion;
    private string _currentActivity;
    private float _currentHp = 100f;
    private float _maxHp = 100f;

    public void Initialize(uint npcId, string displayName)
    {
        NpcId = npcId;
        DisplayName = displayName;
        
        // 프리팹 참조가 끊겼을 경우 동적으로 찾기
        if (nameText == null || statusText == null)
        {
            var texts = GetComponentsInChildren<TMPro.TextMeshPro>();
            if (texts.Length > 0) nameText = texts[0];
            if (texts.Length > 1) statusText = texts[1];
        }

        if (nameText != null)
        {
            nameText.fontSize = 1.2f; // 월드 스페이스 단위 (1.2미터 크기)
            nameText.alignment = TMPro.TextAlignmentOptions.Center;
            nameText.transform.localPosition = new Vector3(0, 3.8f, 0); // Y축 배치 조정
            nameText.transform.rotation = UnityEngine.Quaternion.Euler(90f, 0, 0);
        }
        if (statusText != null)
        {
            statusText.fontSize = 0.8f; // 월드 스페이스 단위 (0.8미터 크기)
            statusText.alignment = TMPro.TextAlignmentOptions.Center;
            statusText.transform.localPosition = new Vector3(0, 2.8f, 0); // Y축 배치 조정
            statusText.transform.rotation = UnityEngine.Quaternion.Euler(90f, 0, 0);
        }

        // 스폰 지점을 현재 위치이자 목표 위치로 설정
        _targetPosition = transform.position;
    }

    public void UpdateStateFromServer(NpcSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        Vector3 previousTarget = _targetPosition;
        // 서버가 내려준 좌표를 목표 좌표로 설정
        _targetPosition = new Vector3(snapshot.Location.Position.X, snapshot.Location.Position.Y, snapshot.Location.Position.Z);
        
        _currentEmotion = snapshot.Emotion;
        _currentActivity = snapshot.Activity;
        _currentHp = snapshot.Hp;
        _maxHp = snapshot.MaxHp;

        if (Vector3.Distance(previousTarget, _targetPosition) > 0.01f)
        {
            Debug.Log($"[NpcController] {DisplayName} (ID: {NpcId}) Target changed: {previousTarget} -> {_targetPosition}");
        }

        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (nameText != null)
        {
            string hpString = _currentHp <= 0f ? "<color=red>💀 [사망]</color>" : $"<color=#00FF66>{_currentHp:F0}</color>/{_maxHp:F0}";
            nameText.text = $"{DisplayName} ({hpString})";
        }
        if (statusText != null)
        {
            statusText.text = $"[{_currentEmotion}]\n{_currentActivity}";
        }
    }

    private void Update()
    {
        // 현재 위치에서 목표 위치까지 부드럽게 이동 (Lerp 또는 MoveTowards)
        // 2D/3D 환경에 따라 알맞게 동작합니다.
        float dist = Vector3.Distance(transform.position, _targetPosition);
        if (dist > 0.01f)
        {
            Vector3 prevPos = transform.position;
            transform.position = Vector3.MoveTowards(transform.position, _targetPosition, moveSpeed * Time.deltaTime);
            // 너무 빈번한 로그 방지를 위해 1초마다 혹은 간헐적으로만 출력하도록 설정하거나, 그냥 위치 변경 디버그용으로 남김
            if (Time.frameCount % 60 == 0) // 대략 1초에 한 번
            {
                Debug.Log($"[NpcController] {DisplayName} (ID: {NpcId}) Moving: {prevPos} -> {transform.position} (Target: {_targetPosition}, Dist Remaining: {dist:F2})");
            }
        }
    }

    private GameObject _activeSpeechBubbleGo;

    public void ShowSpeechBubble(string text, float duration)
    {
        // 기존에 출력 중인 말풍선이 있다면 먼저 파괴
        if (_activeSpeechBubbleGo != null)
        {
            Destroy(_activeSpeechBubbleGo);
        }

        // 새 말풍선 오브젝트 생성 (이름 텍스트보다 살짝 높은 Y=5.5 위치)
        _activeSpeechBubbleGo = new GameObject("SpeechBubbleText");
        _activeSpeechBubbleGo.transform.SetParent(transform, false);
        _activeSpeechBubbleGo.transform.localPosition = new Vector3(0, 5.2f, 0); 
        _activeSpeechBubbleGo.transform.rotation = UnityEngine.Quaternion.Euler(90f, 0, 0);

        var tmpro = _activeSpeechBubbleGo.AddComponent<TMPro.TextMeshPro>();
        tmpro.alignment = TMPro.TextAlignmentOptions.Center;
        tmpro.fontSize = 1.0f; // 가독성 최적화 크기
        tmpro.color = new Color(1.0f, 0.9f, 0.2f); // 밝은 연노랑색 대사
        tmpro.text = $"💬 \"{text}\"";

        // 지정된 시간(초) 후에 자동 파괴
        Destroy(_activeSpeechBubbleGo, duration);
    }

    // 마우스 클릭 시 UIManager에 상세 정보 패널을 열도록 이벤트를 보낼 수 있습니다.
    private void OnMouseDown()
    {
        if (UIManager.Instance != null)
        {
            // 클릭한 NPC의 상세 상태창 열기 (플레이어 조작/관전용)
            UIManager.Instance.ShowNpcDetailsPanel(this);
        }
    }
}
