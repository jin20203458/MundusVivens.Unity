using UnityEngine;
using UnityEngine.UI;
using MundusVivens.Prototype.Protos;

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

    private uint _selectedNpcId;

    private void Awake()
    {
        // 이름에 공백이 있거나 필수 필드가 비어있으면 중복/유효하지 않은 오브젝트로 판단하여 파괴
        if (gameObject.name.Contains(" ") || tickText == null || logText == null)
        {
            Destroy(gameObject);
            return;
        }

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        SetupDetailsScroll();
        if (npcDetailsPanel != null) npcDetailsPanel.SetActive(false);
    }

    public void UpdateTickDisplay(int currentTick)
    {
        if (tickText != null)
            tickText.text = $"Current Tick: {currentTick}";
    }

    public void ShowDialogueEvent(DialogueEventPayload payload)
    {
        string logMessage;

        if (payload.IsStarted)
        {
            logMessage = $"<color=yellow>[대화 시작]</color> {payload.NpcAName} 와(과) {payload.NpcBName} 이(가) 대화를 시작했습니다. ({payload.Location.Name})";
        }
        else
        {
            logMessage = $"<color=orange>[대화 종료]</color> {payload.NpcAName} 와(과) {payload.NpcBName} 의 대화 완료.\n<color=white>요약: {payload.Summary}</color>";

            if (payload.Lines != null && payload.Lines.Count > 0)
            {
                logMessage += "\n<color=silver>--- 대사 로그 ---</color>";
                foreach (var line in payload.Lines)
                    logMessage += $"\n<b>{line.SpeakerName}:</b> {line.Text}";
                logMessage += "\n<color=silver>-----------------</color>";

                GameManager.Instance?.PlayDialogueBubbleSequence(payload.Lines);
            }
        }

        AddLog(logMessage);
    }

    public void AddLog(string message)
    {
        if (logText == null) return;

        logText.text += $"\n{message}\n";

        Canvas.ForceUpdateCanvases();
        if (logScrollRect != null)
            logScrollRect.verticalNormalizedPosition = 0f;
    }

    public void ShowNpcDetailsPanel(NpcController npc)
    {
        if (npcDetailsPanel == null) return;

        npcDetailsPanel.SetActive(true);
        _selectedNpcId = npc.NpcId;

        if (npcNameText != null) npcNameText.text = npc.DisplayName;

        if (npcStatusText != null)
        {
            if (npc.LastSnapshot != null)
            {
                var loc = npc.LastSnapshot.Location;
                npcStatusText.text = $"ID: {npc.NpcId}\n" +
                                     $"위치: {loc.Name} ({loc.Position.X:F1}, {loc.Position.Y:F1}, {loc.Position.Z:F1})\n" +
                                     $"감정: {npc.LastSnapshot.Emotion}\n" +
                                     $"활동: {npc.LastSnapshot.Activity}\n\n" +
                                     $"<color=yellow>기억 및 관계도 데이터를 불러오는 중...</color>";
            }
            else
            {
                npcStatusText.text = $"ID: {npc.NpcId}\n상세 데이터를 불러오는 중입니다...";
            }
        }

        NetworkManager.Instance?.SendGetAgentStatus(npc.NpcId);
    }

    public void UpdateNpcDetails(GetAgentStatusResponse response)
    {
        if (npcNameText == null || npcStatusText == null) return;
        if (!npcDetailsPanel.activeSelf) return;
        if (npcNameText.text != response.Name) return;

        string locName = "알 수 없음";
        string posX = "0.0", posY = "0.0", posZ = "0.0";
        if (response.Location != null)
        {
            locName = response.Location.Name;
            if (response.Location.Position != null)
            {
                posX = response.Location.Position.X.ToString("F1");
                posY = response.Location.Position.Y.ToString("F1");
                posZ = response.Location.Position.Z.ToString("F1");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ID: {_selectedNpcId}");
        sb.AppendLine($"위치: {locName} ({posX}, {posY}, {posZ})");
        sb.AppendLine($"감정: {response.Emotion}");
        sb.AppendLine($"활동: {response.Activity}");
        sb.AppendLine();
        sb.AppendLine("<color=orange><b>[실시간 기억 및 관계망]</b></color>");

        if (response.Memories != null && response.Memories.Count > 0)
        {
            foreach (var memory in response.Memories)
                sb.AppendLine($"- {memory}");
        }
        else
        {
            sb.AppendLine("기억이 없습니다.");
        }

        npcStatusText.text = sb.ToString();
    }

    public void HideNpcDetailsPanel()
    {
        npcDetailsPanel?.SetActive(false);
    }

    // 씬에 ScrollRect가 없는 NPC 상세창에 런타임으로 스크롤뷰를 장착
    private void SetupDetailsScroll()
    {
        if (npcStatusText == null || npcDetailsPanel == null) return;
        if (npcStatusText.GetComponentInParent<ScrollRect>() != null) return;

        // ScrollRect 컨테이너 생성
        var scrollViewGo = new GameObject("DetailsScrollView", typeof(RectTransform), typeof(ScrollRect));
        scrollViewGo.transform.SetParent(npcDetailsPanel.transform, false);

        var scrollRT = scrollViewGo.GetComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.sizeDelta = new Vector2(-20f, -80f);
        scrollRT.anchoredPosition = new Vector2(0f, -25f);

        // 마스킹용 Viewport 생성
        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(scrollViewGo.transform, false);

        var viewportRT = viewportGo.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.sizeDelta = Vector2.zero;

        viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        // npcStatusText를 Viewport 하위로 이동
        var textRT = npcStatusText.GetComponent<RectTransform>();
        textRT.SetParent(viewportRT, false);

        // 텍스트 양에 따라 높이가 자동 확장되도록 ContentSizeFitter 추가
        var fitter = npcStatusText.gameObject.GetComponent<ContentSizeFitter>()
                  ?? npcStatusText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // 앵커를 상단 고정으로 설정
        textRT.anchorMin = new Vector2(0f, 1f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.pivot = new Vector2(0.5f, 1f);
        textRT.anchoredPosition = Vector2.zero;

        npcStatusText.alignment = TMPro.TextAlignmentOptions.TopLeft;

        // ScrollRect와 컨텐츠 연결
        var scrollRect = scrollViewGo.GetComponent<ScrollRect>();
        scrollRect.content = textRT;
        scrollRect.viewport = viewportRT;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
    }
}
