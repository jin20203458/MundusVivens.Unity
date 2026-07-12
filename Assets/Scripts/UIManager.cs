using UnityEngine;
using UnityEngine.UI;
using MundusVivens.Prototype.Protos; // Protobuf 네임스페이스

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Top Panel")]
    public TMPro.TextMeshProUGUI tickText;

    [Header("Log Panel (Scroll View)")]
    public TMPro.TextMeshProUGUI logText;
    public ScrollRect logScrollRect;

    [Header("NPC Details Panel")]
    public GameObject npcDetailsPanel;
    public TMPro.TextMeshProUGUI npcNameText;
    public TMPro.TextMeshProUGUI npcStatusText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        
        // 시작 시 패널 숨김
        if (npcDetailsPanel != null) npcDetailsPanel.SetActive(false);
    }

    public void UpdateTickDisplay(int currentTick)
    {
        if (tickText != null)
        {
            tickText.text = $"Current Tick: {currentTick}";
        }
    }

    public void ShowDialogueEvent(DialogueEventPayload payload)
    {
        string logMessage = "";

        if (payload.IsStarted)
        {
            logMessage = $"<color=yellow>[대화 시작]</color> {payload.NpcAName} 와(과) {payload.NpcBName} 이(가) 대화를 시작했습니다. ({payload.Location.Name})";
        }
        else
        {
            logMessage = $"<color=orange>[대화 종료]</color> {payload.NpcAName} 와(과) {payload.NpcBName} 의 대화 완료.\n<color=white>요약: {payload.Summary}</color>";
            
            // 상세 대사가 있다면 함께 출력
            if (payload.Lines != null && payload.Lines.Count > 0)
            {
                logMessage += "\n<color=silver>--- 대사 로그 ---</color>";
                foreach (var line in payload.Lines)
                {
                    logMessage += $"\n<b>{line.SpeakerName}:</b> {line.Text}";
                }
                logMessage += "\n<color=silver>-----------------</color>";
            }
        }

        AddLog(logMessage);
    }

    public void AddLog(string message)
    {
        if (logText == null) return;

        // 로그 누적
        logText.text += $"\n{message}\n";

        // 스크롤 맨 아래로 자동 이동
        Canvas.ForceUpdateCanvases();
        if (logScrollRect != null)
        {
            logScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    public void ShowNpcDetailsPanel(NpcController npc)
    {
        if (npcDetailsPanel == null) return;

        npcDetailsPanel.SetActive(true);
        if (npcNameText != null) npcNameText.text = npc.DisplayName;
        
        // 현재는 클라이언트가 상세 데이터를 들고있지 않으므로 간단한 안내만 표시
        // 추후 서버로 '상세 정보 조회 패킷'을 요청하여 업데이트하도록 확장할 예정
        if (npcStatusText != null) 
            npcStatusText.text = $"ID: {npc.NpcId}\n더 상세한 기억 및 관계도는 추후 구현됩니다.";
    }

    public void HideNpcDetailsPanel()
    {
        if (npcDetailsPanel != null)
        {
            npcDetailsPanel.SetActive(false);
        }
    }
}
