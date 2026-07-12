using UnityEngine;
using MundusVivens.Prototype.Protos;
using Vector3 = UnityEngine.Vector3;

public class NpcController : MonoBehaviour
{
    public uint NpcId { get; private set; }
    public string DisplayName { get; private set; }

    [Header("UI / Visuals")]
    public TMPro.TextMeshPro nameText;
    public TMPro.TextMeshPro statusText;

    public NpcSnapshot LastSnapshot { get; private set; }

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;

    private Vector3 _targetPosition;
    private GameObject _activeSpeechBubbleGo;

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
            nameText.transform.localScale = Vector3.one;
            nameText.fontSize = 1.8f;
            nameText.alignment = TMPro.TextAlignmentOptions.Center;
            nameText.transform.localPosition = new Vector3(0, 0.1f, 1.2f);
            nameText.transform.rotation = Quaternion.Euler(90f, 0, 0);
        }
        if (statusText != null)
        {
            statusText.transform.localScale = Vector3.one;
            statusText.fontSize = 1.3f;
            statusText.alignment = TMPro.TextAlignmentOptions.Center;
            statusText.transform.localPosition = new Vector3(0, 0.1f, -1.2f);
            statusText.transform.rotation = Quaternion.Euler(90f, 0, 0);
        }

        _targetPosition = transform.position;
    }

    public void UpdateStateFromServer(NpcSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        _targetPosition = new Vector3(
            snapshot.Location.Position.X,
            snapshot.Location.Position.Y,
            snapshot.Location.Position.Z);

        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (nameText != null)
        {
            float hp = LastSnapshot?.Hp ?? 0f;
            float maxHp = LastSnapshot?.MaxHp ?? 100f;
            string hpString = hp <= 0f
                ? "<color=red>💀 [사망]</color>"
                : $"<color=#00FF66>{hp:F0}</color>/{maxHp:F0}";
            nameText.text = $"{DisplayName} ({hpString})";
        }
        if (statusText != null)
        {
            statusText.text = $"[{LastSnapshot?.Emotion}]\n{LastSnapshot?.Activity}";
        }
    }

    private void Update()
    {
        if (Vector3.Distance(transform.position, _targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, _targetPosition, moveSpeed * Time.deltaTime);
        }
    }

    public void ShowSpeechBubble(string text, float duration)
    {
        if (_activeSpeechBubbleGo != null)
            Destroy(_activeSpeechBubbleGo);

        _activeSpeechBubbleGo = new GameObject("SpeechBubbleText");
        _activeSpeechBubbleGo.transform.SetParent(transform, false);
        _activeSpeechBubbleGo.transform.localPosition = new Vector3(0, 0.2f, 2.5f);
        _activeSpeechBubbleGo.transform.rotation = Quaternion.Euler(90f, 0, 0);
        _activeSpeechBubbleGo.transform.localScale = Vector3.one;

        var tmpro = _activeSpeechBubbleGo.AddComponent<TMPro.TextMeshPro>();
        tmpro.font = nameText != null ? nameText.font : statusText?.font;
        tmpro.rectTransform.sizeDelta = new Vector2(30f, 5f);
        tmpro.enableWordWrapping = true;
        tmpro.alignment = TMPro.TextAlignmentOptions.Center;
        tmpro.fontSize = 1.5f;
        tmpro.color = new Color(1.0f, 0.9f, 0.2f);
        tmpro.text = $"💬 \"{text}\"";

        Destroy(_activeSpeechBubbleGo, duration);
    }

    private void OnMouseDown()
    {
        UIManager.Instance?.ShowNpcDetailsPanel(this);
    }
}
