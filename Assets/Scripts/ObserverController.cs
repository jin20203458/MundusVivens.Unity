using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Cysharp.Net.Http;
using MundusVivens.Prototype.Protos;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MundusVivens.Unity
{
    public class ObserverController : MonoBehaviour
    {
        [Header("gRPC Configuration")]
        [SerializeField] private string serverUrl = "http://localhost:5001";
        [SerializeField] private string subscriberId = "Unity_Observer_UI";

        [Header("UI Panels (Location Anchors)")]
        [SerializeField] private RectTransform tavernPanel;
        [SerializeField] private RectTransform churchPanel;
        [SerializeField] private RectTransform squarePanel;

        [Header("NPC Visual Prefabs/References")]
        [SerializeField] private RectTransform kyleIcon;
        [SerializeField] private RectTransform evaIcon;
        [SerializeField] private RectTransform bartIcon;

        [Header("UI Text Displays")]
        [SerializeField] private TextMeshProUGUI tickText;
        [SerializeField] private TextMeshProUGUI logsText;
        [SerializeField] private ScrollRect logScrollRect;
        [SerializeField] private RectTransform dialogueBubbleContainer;
        [SerializeField] private TextMeshProUGUI dialogueBubbleText;

        [Header("Relationship UI Panel")]
        [SerializeField] private TextMeshProUGUI relationshipText;

        private GrpcChannel _channel;
        private MundusVivensGrpc.MundusVivensGrpcClient _client;
        private CancellationTokenSource _cts;

        private readonly Dictionary<string, RectTransform> _npcIcons = new();
        private readonly Dictionary<string, RectTransform> _locationPanels = new();
        private readonly Dictionary<string, string> _npcKoreanNames = new()
        {
            { "npc_kyle", "카일" },
            { "npc_eva", "에바" },
            { "npc_bart", "바르트" }
        };

        // 관계도 캐시 (Key: FromId -> (ToId -> (Liking, Trust)))
        private readonly Dictionary<string, Dictionary<string, (int Liking, int Trust)>> _relationshipCache = new();

        // 🆕 NPC 상태 텍스트 컴포넌트 캐시
        private readonly Dictionary<string, TextMeshProUGUI> _npcStatusTexts = new();

        // IMGUI fallback HUD 전용 데이터
        private readonly List<string> _imguiLogs = new();
        private string _imguiTickText = "Tick: 0";
        private readonly Dictionary<string, string> _npcLocations = new()
        {
            { "npc_kyle", "성당 (Church)" },
            { "npc_eva", "술집 (Tavern)" },
            { "npc_bart", "술집 (Tavern)" }
        };

        private void Start()
        {
            GetOrCreateKoreanFont();
            // 🆕 Inspector에서 UI 요소가 비어있을 경우, 비주얼 대시보드를 런타임에 동적으로 구축합니다.
            if (tavernPanel == null || churchPanel == null || squarePanel == null ||
                kyleIcon == null || evaIcon == null || bartIcon == null ||
                logsText == null || relationshipText == null || tickText == null ||
                dialogueBubbleContainer == null || dialogueBubbleText == null)
            {
                CreateVisualUIProgrammatically();
            }
            
            InitializeMappings();

            // Ensure Korean font support on all referenced text fields
            EnsureKoreanSupport(tickText);
            EnsureKoreanSupport(logsText);
            EnsureKoreanSupport(relationshipText);
            EnsureKoreanSupport(dialogueBubbleText);
            
            // Also call it on each NPC status text
            foreach (var statusText in _npcStatusTexts.Values)
            {
                EnsureKoreanSupport(statusText);
            }

            ConnectToServer();
        }

        private void InitializeMappings()
        {
            // NPC ID -> 아이콘 RectTransform 매핑
            if (kyleIcon != null) _npcIcons["npc_kyle"] = kyleIcon;
            if (evaIcon != null) _npcIcons["npc_eva"] = evaIcon;
            if (bartIcon != null) _npcIcons["npc_bart"] = bartIcon;

            // 공간 구역 ID -> 패널 RectTransform 매핑
            if (tavernPanel != null) _locationPanels["Tavern"] = tavernPanel;
            if (tavernPanel != null) _locationPanels["술집 (Tavern)"] = tavernPanel;
            if (churchPanel != null) _locationPanels["Church"] = churchPanel;
            if (churchPanel != null) _locationPanels["성당 (Church)"] = churchPanel;
            if (squarePanel != null) _locationPanels["Square"] = squarePanel;
            if (squarePanel != null) _locationPanels["광장 (Square)"] = squarePanel;

            // 관계도 캐시 초기화
            foreach (var fromNpc in _npcKoreanNames.Keys)
            {
                _relationshipCache[fromNpc] = new Dictionary<string, (int, int)>();
                foreach (var toNpc in _npcKoreanNames.Keys)
                {
                    if (fromNpc != toNpc)
                    {
                        _relationshipCache[fromNpc][toNpc] = (0, 50); // 기본값: 호감도 0, 신뢰도 50
                    }
                }
            }

            UpdateRelationshipUI();
            
            if (dialogueBubbleContainer != null)
            {
                dialogueBubbleContainer.gameObject.SetActive(false);
            }
        }

        private void ConnectToServer()
        {
            try
            {
                AddLog("🔌 [Observer] 서버 이벤트 스트림에 연결하는 중...");

                var handler = new YetAnotherHttpHandler()
                {
                    Http2Only = true
                };

                _channel = GrpcChannel.ForAddress(serverUrl, new GrpcChannelOptions
                {
                    HttpHandler = handler
                });

                _client = new MundusVivensGrpc.MundusVivensGrpcClient(_channel);
                _cts = new CancellationTokenSource();

                SubscribeEventsAsync(_cts.Token).Forget();
            }
            catch (Exception ex)
            {
                AddLog($"❌ [Observer] 채널 연결 실패: {ex.Message}");
            }
        }

        private async UniTaskVoid SubscribeEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var request = new SubscribeRequest { SubscriberId = subscriberId };
                using var call = _client.SubscribeWorldEvents(request, cancellationToken: cancellationToken);

                AddLog("🟢 [Observer] 스트림 연결 완료. 실시간 월드 모니터링 중...");

                while (await call.ResponseStream.MoveNext(cancellationToken))
                {
                    var worldEvent = call.ResponseStream.Current;
                    
                    // 메인 스레드로 스위칭하여 Unity UI 조작
                    await UniTask.SwitchToMainThread();
                    ProcessWorldEvent(worldEvent);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                AddLog("🔌 [Observer] 서버 스트림 연결이 취소되었습니다.");
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ [Observer] 스트림 연결 유실: {ex.Message}");
            }
        }

        private void ProcessWorldEvent(WorldEvent worldEvent)
        {
            string timeStr = DateTimeOffset.FromUnixTimeMilliseconds(worldEvent.Timestamp).ToLocalTime().ToString("HH:mm:ss");

            switch (worldEvent.EventCase)
            {
                case WorldEvent.EventOneofCase.Tick:
                    var tick = worldEvent.Tick;
                    _imguiTickText = $"Tick: {tick.TickNumber}";
                    if (tickText != null)
                    {
                        tickText.text = $"Tick: {tick.TickNumber}";
                    }
                    AddLog($"⏱️ [{timeStr}] <b>월드 틱 전진</b> ➔ <b>틱 {tick.TickNumber}</b>");
                    break;

                case WorldEvent.EventOneofCase.Movement:
                    var move = worldEvent.Movement;
                    _npcLocations[move.AgentId] = move.ToLocation;
                    AnimateNpcMovement(move.AgentId, move.ToLocation);
                    
                    string npcName = _npcKoreanNames.TryGetValue(move.AgentId, out var name) ? name : move.AgentId;
                    AddLog($"🏃 [{timeStr}] <b>{npcName}</b> 이동: {move.FromLocation} ➔ {move.ToLocation}");
                    UpdateNpcActivityText(move.AgentId, "이동 중...");
                    ResetNpcActivityAfterDelay(move.AgentId, 0.6f).Forget();
                    break;

                case WorldEvent.EventOneofCase.Dialogue:
                    var dialogue = worldEvent.Dialogue;
                    string nameA = _npcKoreanNames.TryGetValue(dialogue.AgentAId, out var na) ? na : dialogue.AgentAId;
                    string nameB = _npcKoreanNames.TryGetValue(dialogue.AgentBId, out var nb) ? nb : dialogue.AgentBId;

                    if (dialogue.IsStarted)
                    {
                        // 🆕 실시간 스트리밍 대사 수신 처리
                        if (dialogue.Lines != null && dialogue.Lines.Count > 0)
                        {
                            var latestLine = dialogue.Lines[dialogue.Lines.Count - 1];
                            string speakerKoreanName = _npcKoreanNames.TryGetValue(latestLine.SpeakerId, out var skName) ? skName : latestLine.SpeakerName;
                            
                            ShowDialogueBubble(latestLine.SpeakerId, speakerKoreanName, latestLine.Text);
                            AddLog($"💬 <b>{speakerKoreanName}</b>: \"{latestLine.Text}\"");
                        }
                        else
                        {
                            AddLog($"💬 [{timeStr}] <b>대화 시작</b>: {nameA} ⬌ {nameB} (위치: {dialogue.Location})");
                            ShowDialogueBubble(dialogue.AgentAId, nameA, "대화 진행 중...");
                        }
                        UpdateNpcActivityText(dialogue.AgentAId, "대화 중...");
                        UpdateNpcActivityText(dialogue.AgentBId, "대화 중...");
                    }
                    else
                    {
                        AddLog($"🔔 [{timeStr}] <b>대화 종료</b>\n 요약: \"{dialogue.Summary}\"");
                        HideDialogueBubble();
                        
                        UpdateNpcActivityText(dialogue.AgentAId, "대기 중");
                        UpdateNpcActivityText(dialogue.AgentBId, "대기 중");

                        // 대본 내용 상세 로깅
                        if (dialogue.Lines != null && dialogue.Lines.Count > 0)
                        {
                            string scriptDetails = "";
                            foreach (var line in dialogue.Lines)
                            {
                                scriptDetails += $"\n    ↳ <b>{line.SpeakerName}</b>: \"{line.Text}\"";
                            }
                            AddLog(scriptDetails);
                        }
                    }
                    break;

                case WorldEvent.EventOneofCase.Gossip:
                    var gossip = worldEvent.Gossip;
                    string spName = _npcKoreanNames.TryGetValue(gossip.SpeakerId, out var sp) ? sp : gossip.SpeakerId;
                    string lsName = _npcKoreanNames.TryGetValue(gossip.ListenerId, out var ls) ? ls : gossip.ListenerId;
                    string subName = _npcKoreanNames.TryGetValue(gossip.SubjectId, out var sub) ? sub : gossip.SubjectId;
                    
                    string mutationMark = gossip.IsMutated ? "<color=red><b>[왜곡/와전 발생!]</b></color> " : "";
                    AddLog($"📢 [{timeStr}] {mutationMark}<b>소문 전파</b> ({spName} ➔ {lsName}): Subject={subName}, 내용=\"{gossip.GossipContent}\"");
                    break;

                case WorldEvent.EventOneofCase.Relationship:
                    var rel = worldEvent.Relationship;
                    UpdateRelationshipCache(rel.FromAgentId, rel.ToAgentId, rel.NewAffinity, rel.NewTrust);
                    
                    string fromName = _npcKoreanNames.TryGetValue(rel.FromAgentId, out var fName) ? fName : rel.FromAgentId;
                    string toName = _npcKoreanNames.TryGetValue(rel.ToAgentId, out var tName) ? tName : rel.ToAgentId;
                    AddLog($"❤️ [{timeStr}] <b>관계 변화</b>: {fromName} ➔ {toName} (호감도: {rel.NewAffinity} ({rel.AffinityDelta:+0;-0;0}), 신뢰도: {rel.NewTrust} ({rel.TrustDelta:+0;-0;0}))");
                    break;
            }
        }

        private void AnimateNpcMovement(string agentId, string toLocation)
        {
            if (!_npcIcons.TryGetValue(agentId, out var icon) || !_locationPanels.TryGetValue(toLocation, out var targetPanel))
            {
                return;
            }

            // 구역 패널 내에서 아이콘들이 겹치지 않게 오프셋 부여
            float offset = 0f;
            if (agentId == "npc_eva") offset = -40f;
            if (agentId == "npc_bart") offset = 40f;

            // 목적지 패널 안으로 부모 변경 및 로컬 위치 리셋
            icon.SetParent(targetPanel, false);
            
            // 단순 이동 보간 애니메이션 (UniTask 활용)
            LerpIconLocalPosition(icon, new Vector2(offset, 0f), 0.5f).Forget();
        }

        private async UniTaskVoid LerpIconLocalPosition(RectTransform icon, Vector2 targetLocalPos, float duration)
        {
            Vector2 startPos = icon.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                if (icon != null)
                {
                    icon.anchoredPosition = Vector2.Lerp(startPos, targetLocalPos, t);
                }
                await UniTask.Yield();
            }

            if (icon != null)
            {
                icon.anchoredPosition = targetLocalPos;
            }
        }

        private void UpdateNpcActivityText(string npcId, string status)
        {
            if (_npcStatusTexts.TryGetValue(npcId, out var tm))
            {
                string name = _npcKoreanNames.TryGetValue(npcId, out var n) ? n : npcId;
                tm.text = $"<b>{name}</b>\n<size=11>{status}</size>";
            }
        }

        private async UniTaskVoid ResetNpcActivityAfterDelay(string npcId, float delaySeconds)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delaySeconds));
            UpdateNpcActivityText(npcId, "대기 중");
        }

        private void ShowDialogueBubble(string speakerId, string speakerName, string text)
        {
            if (dialogueBubbleContainer != null && dialogueBubbleText != null)
            {
                dialogueBubbleContainer.gameObject.SetActive(true);
                dialogueBubbleText.text = $"💬 <b>{speakerName}</b>: {text}";

                if (_npcIcons.TryGetValue(speakerId, out var npcIcon))
                {
                    dialogueBubbleContainer.SetParent(npcIcon, false);
                    dialogueBubbleContainer.anchoredPosition = new Vector2(0f, 90f); // NPC 머리 위 90px 오프셋
                }
            }
        }

        private void HideDialogueBubble()
        {
            if (dialogueBubbleContainer != null)
            {
                dialogueBubbleContainer.gameObject.SetActive(false);
            }
        }

        // 🆕 동적 비주얼 UI 생성 빌더 메서드
        private void CreateVisualUIProgrammatically()
        {
            var korFont = GetOrCreateKoreanFont();
            // 1. Canvas 생성
            GameObject canvasGO = new GameObject("Dynamic_Observer_Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasGO.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(canvasGO);

            // Ensure EventSystem exists for UI raycasting
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                eventSystemGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                DontDestroyOnLoad(eventSystemGO);
                Debug.Log("[ObserverUI] Created EventSystem programmatically.");
            }
            
            // 2. 배경 화면
            GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(canvasGO.transform, false);
            RectTransform bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bgGO.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f); // 어두운 슬레이트 블루
            
            // 3. 구역 패널 배치
            float panelWidth = 260f;
            float panelHeight = 400f;
            float startY = 100f;
            
            churchPanel = CreateLocationPanel(bgGO.transform, "성당 (Church)", new Vector2(-350f, startY), panelWidth, panelHeight, new Color(0.2f, 0.22f, 0.35f, 0.6f));
            squarePanel = CreateLocationPanel(bgGO.transform, "광장 (Square)", new Vector2(0f, startY), panelWidth, panelHeight, new Color(0.2f, 0.28f, 0.28f, 0.6f));
            tavernPanel = CreateLocationPanel(bgGO.transform, "술집 (Tavern)", new Vector2(350f, startY), panelWidth, panelHeight, new Color(0.3f, 0.22f, 0.18f, 0.6f));
            
            // 4. NPC 캐릭터 배지 생성
            kyleIcon = CreateNpcIcon(churchPanel, "npc_kyle", "카일", new Color(0.85f, 0.7f, 0.25f, 1f));
            evaIcon = CreateNpcIcon(tavernPanel, "npc_eva", "에바", new Color(0.8f, 0.4f, 0.6f, 1f));
            bartIcon = CreateNpcIcon(tavernPanel, "npc_bart", "바르트", new Color(0.8f, 0.3f, 0.3f, 1f));
            
            // 5. 틱 수 및 타이틀 텍스트
            GameObject tickGO = new GameObject("TickText", typeof(RectTransform), typeof(TextMeshProUGUI));
            tickGO.transform.SetParent(bgGO.transform, false);
            RectTransform tickRect = tickGO.GetComponent<RectTransform>();
            tickRect.anchorMin = new Vector2(0f, 1f);
            tickRect.anchorMax = new Vector2(0f, 1f);
            tickRect.pivot = new Vector2(0f, 1f);
            tickRect.anchoredPosition = new Vector2(30f, -30f);
            tickRect.sizeDelta = new Vector2(250f, 40f);
            tickText = tickGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) tickText.font = korFont;
            tickText.fontSize = 22;
            tickText.fontStyle = FontStyles.Bold;
            tickText.color = new Color(0.3f, 0.85f, 0.5f, 1f);
            tickText.text = "Tick: 0";
            
            GameObject titleGO = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGO.transform.SetParent(bgGO.transform, false);
            RectTransform titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);
            titleRect.sizeDelta = new Vector2(600f, 40f);
            var titleTxt = titleGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) titleTxt.font = korFont;
            titleTxt.text = "Project Mundus Vivens — Live Observer Dashboard";
            titleTxt.fontSize = 20;
            titleTxt.fontStyle = FontStyles.Bold;
            titleTxt.color = Color.white;
            titleTxt.alignment = TextAlignmentOptions.Center;
            
            // 6. 실시간 로그 영역 (하단 좌측)
            GameObject logsBox = new GameObject("LogsBox", typeof(RectTransform), typeof(Image));
            logsBox.transform.SetParent(bgGO.transform, false);
            RectTransform logsBoxRect = logsBox.GetComponent<RectTransform>();
            logsBoxRect.anchorMin = Vector2.zero;
            logsBoxRect.anchorMax = Vector2.zero;
            logsBoxRect.pivot = Vector2.zero;
            logsBoxRect.anchoredPosition = new Vector2(30f, 30f);
            logsBoxRect.sizeDelta = new Vector2(720f, 220f);
            logsBox.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 0.9f);
            
            GameObject scrollGO = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(logsBox.transform, false);
            RectTransform scrollRectTrans = scrollGO.GetComponent<RectTransform>();
            scrollRectTrans.anchorMin = Vector2.zero;
            scrollRectTrans.anchorMax = Vector2.one;
            scrollRectTrans.offsetMin = new Vector2(10f, 10f);
            scrollRectTrans.offsetMax = new Vector2(-10f, -10f);
            logScrollRect = scrollGO.GetComponent<ScrollRect>();
            
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(scrollGO.transform, false);
            RectTransform contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            logScrollRect.content = contentRect;
            
            GameObject textGO = new GameObject("LogsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(contentGO.transform, false);
            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            logsText = textGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) logsText.font = korFont;
            logsText.fontSize = 13;
            logsText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            logsText.enableWordWrapping = true;
            logsText.text = "=== 실시간 월드 이벤트 로그 ===\n";
            logScrollRect.viewport = scrollRectTrans;
            
            // 7. 실시간 관계도 수치 패널 (하단 우측)
            GameObject relBox = new GameObject("RelationshipBox", typeof(RectTransform), typeof(Image));
            relBox.transform.SetParent(bgGO.transform, false);
            RectTransform relBoxRect = relBox.GetComponent<RectTransform>();
            relBoxRect.anchorMin = new Vector2(1f, 0f);
            relBoxRect.anchorMax = new Vector2(1f, 0f);
            relBoxRect.pivot = new Vector2(1f, 0f);
            relBoxRect.anchoredPosition = new Vector2(-30f, 30f);
            relBoxRect.sizeDelta = new Vector2(460f, 220f);
            relBox.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 0.9f);
            
            GameObject relTextGO = new GameObject("RelationshipText", typeof(RectTransform), typeof(TextMeshProUGUI));
            relTextGO.transform.SetParent(relBox.transform, false);
            RectTransform relTextRect = relTextGO.GetComponent<RectTransform>();
            relTextRect.anchorMin = Vector2.zero;
            relTextRect.anchorMax = Vector2.one;
            relTextRect.offsetMin = new Vector2(10f, 10f);
            relTextRect.offsetMax = new Vector2(-10f, -10f);
            relationshipText = relTextGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) relationshipText.font = korFont;
            relationshipText.fontSize = 12;
            relationshipText.color = Color.white;
            relationshipText.enableWordWrapping = true;
            
            // 8. 말풍선 오버레이 (초기 비활성화)
            GameObject bubbleGO = new GameObject("DialogueBubble", typeof(RectTransform), typeof(Image));
            bubbleGO.transform.SetParent(bgGO.transform, false);
            dialogueBubbleContainer = bubbleGO.GetComponent<RectTransform>();
            dialogueBubbleContainer.sizeDelta = new Vector2(400f, 90f);
            bubbleGO.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.15f, 0.95f);
            
            GameObject bubbleTextGO = new GameObject("BubbleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            bubbleTextGO.transform.SetParent(bubbleGO.transform, false);
            RectTransform bubbleTextRect = bubbleTextGO.GetComponent<RectTransform>();
            bubbleTextRect.anchorMin = Vector2.zero;
            bubbleTextRect.anchorMax = Vector2.one;
            bubbleTextRect.offsetMin = new Vector2(12f, 12f);
            bubbleTextRect.offsetMax = new Vector2(-12f, -12f);
            dialogueBubbleText = bubbleTextGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) dialogueBubbleText.font = korFont;
            dialogueBubbleText.fontSize = 12;
            dialogueBubbleText.color = new Color(0.5f, 0.95f, 0.95f, 1f); // Neon Cyan
            dialogueBubbleText.alignment = TextAlignmentOptions.Center;
            dialogueBubbleText.enableWordWrapping = true;
            
            dialogueBubbleContainer.gameObject.SetActive(false);
        }

        private RectTransform CreateLocationPanel(Transform parent, string title, Vector2 pos, float w, float h, Color color)
        {
            GameObject panelGO = new GameObject("LocationPanel_" + title, typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(parent, false);
            RectTransform rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(w, h);
            panelGO.GetComponent<Image>().color = color;
            
            GameObject labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(panelGO.transform, false);
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 1f);
            labelRT.anchorMax = new Vector2(1f, 1f);
            labelRT.pivot = new Vector2(0.5f, 1f);
            labelRT.anchoredPosition = new Vector2(0f, -10f);
            labelRT.sizeDelta = new Vector2(0f, 35f);
            
            var tm = labelGO.GetComponent<TextMeshProUGUI>();
            var korFont = GetOrCreateKoreanFont();
            if (korFont != null) tm.font = korFont;
            tm.text = $"<b>{title}</b>";
            tm.fontSize = 16;
            tm.color = Color.white;
            tm.alignment = TextAlignmentOptions.Center;
            
            return rt;
        }

        private RectTransform CreateNpcIcon(Transform parent, string npcId, string name, Color badgeColor)
        {
            GameObject iconGO = new GameObject("NpcIcon_" + npcId, typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(parent, false);
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220f, 90f);
            iconGO.GetComponent<Image>().color = badgeColor;
            
            GameObject textGO = new GameObject("TextDisplay", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(iconGO.transform, false);
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 35f); // 버튼 영역 확보를 위해 하단 여백 추가
            textRT.offsetMax = new Vector2(-10f, -5f);
            
            var tm = textGO.GetComponent<TextMeshProUGUI>();
            var korFont = GetOrCreateKoreanFont();
            if (korFont != null) tm.font = korFont;
            tm.text = $"<b>{name}</b>\n<size=11>대기 중</size>";
            tm.alignment = TextAlignmentOptions.Center;
            tm.color = Color.black;
            tm.fontSize = 13;
            
            _npcStatusTexts[npcId] = tm;
            
            // 플레이어 대화 진입 [대화하기] 버튼 추가
            GameObject btnGO = new GameObject("TalkBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(iconGO.transform, false);
            RectTransform btnRT = btnGO.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.5f, 0f);
            btnRT.anchorMax = new Vector2(0.5f, 0f);
            btnRT.pivot = new Vector2(0.5f, 0f);
            btnRT.anchoredPosition = new Vector2(0f, 8f);
            btnRT.sizeDelta = new Vector2(120f, 22f);
            btnGO.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.85f);
            
            GameObject btnTextGO = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnTextGO.transform.SetParent(btnGO.transform, false);
            RectTransform btnTextRT = btnTextGO.GetComponent<RectTransform>();
            btnTextRT.anchorMin = Vector2.zero;
            btnTextRT.anchorMax = Vector2.one;
            btnTextRT.offsetMin = Vector2.zero;
            btnTextRT.offsetMax = Vector2.zero;
            var btnTm = btnTextGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) btnTm.font = korFont;
            btnTm.text = "<b><size=11>대화하기</size></b>";
            btnTm.color = Color.cyan;
            btnTm.alignment = TextAlignmentOptions.Center;
            
            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                var dialogCtrl = FindObjectOfType<PlayerDialogueController>();
                if (dialogCtrl != null)
                {
                    dialogCtrl.StartConversation(npcId);
                }
                else
                {
                    Debug.LogWarning($"[Observer] PlayerDialogueController를 씬에서 찾을 수 없습니다.");
                }
            });
            
            return rt;
        }

        private void UpdateRelationshipCache(string fromId, string toId, int newLiking, int newTrust)
        {
            if (_relationshipCache.ContainsKey(fromId) && _relationshipCache[fromId].ContainsKey(toId))
            {
                _relationshipCache[fromId][toId] = (newLiking, newTrust);
                UpdateRelationshipUI();
            }
        }

        private void UpdateRelationshipUI()
        {
            if (relationshipText == null) return;

            string uiText = "<b>[실시간 사회 관계도 수치]</b>\n\n";

            foreach (var fromId in _relationshipCache.Keys)
            {
                string fromName = _npcKoreanNames[fromId];
                uiText += $"<b>{fromName}</b>의 마음:\n";

                foreach (var toId in _relationshipCache[fromId].Keys)
                {
                    string toName = _npcKoreanNames[toId];
                    var (liking, trust) = _relationshipCache[fromId][toId];
                    
                    // 호감도 컬러링
                    string likingColor = liking > 20 ? "green" : (liking < -20 ? "red" : "white");
                    
                    uiText += $"  ➔ {toName}: 호감도=<color={likingColor}>{liking}</color>, 신뢰도={trust}\n";
                }
                uiText += "\n";
            }

            relationshipText.text = uiText;
        }

        private void AddLog(string text)
        {
            // Remove rich text tags for clean console/imgui text
            string plainText = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
            _imguiLogs.Add(plainText);
            if (_imguiLogs.Count > 30)
            {
                _imguiLogs.RemoveAt(0);
            }

            if (logsText == null) return;

            logsText.text += $"{text}\n";

            // 스크롤 뷰가 최하단으로 자동 스크롤되도록 보정
            if (logScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private Vector2 _imguiScroll = Vector2.zero;

        private void OnGUI()
        {
            if (logsText == null || relationshipText == null)
            {
                // Draw fallback diagnostic UI on the screen
                GUI.Box(new Rect(10, 10, Screen.width - 20, Screen.height - 20), "Project Mundus Vivens — Observer Mode Diagnostic HUD");

                // Tick Display
                GUI.Label(new Rect(20, 40, 200, 20), _imguiTickText, new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });

                // Locations Display
                GUI.Label(new Rect(20, 70, 300, 20), "<b>[NPC Locations]</b>", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
                int yOffset = 90;
                foreach (var kvp in _npcLocations)
                {
                    string npcName = _npcKoreanNames.TryGetValue(kvp.Key, out var name) ? name : kvp.Key;
                    GUI.Label(new Rect(20, yOffset, 300, 20), $"{npcName}: {kvp.Value}");
                    yOffset += 20;
                }

                // Relationships Display
                GUI.Label(new Rect(20, yOffset + 10, 300, 20), "<b>[Relationships]</b>", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
                yOffset += 30;
                foreach (var fromId in _relationshipCache.Keys)
                {
                    string fromName = _npcKoreanNames[fromId];
                    string relStr = $"{fromName} ➔ ";
                    foreach (var toId in _relationshipCache[fromId].Keys)
                    {
                        string toName = _npcKoreanNames[toId];
                        var (liking, trust) = _relationshipCache[fromId][toId];
                        relStr += $"{toName}(호감도:{liking}, 신뢰도:{trust})  ";
                    }
                    GUI.Label(new Rect(20, yOffset, 600, 20), relStr);
                    yOffset += 20;
                }

                // Logs Display
                GUI.Label(new Rect(20, yOffset + 10, 300, 20), "<b>[Live Event Logs]</b>", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
                
                float logBoxY = yOffset + 30;
                float logBoxHeight = Screen.height - logBoxY - 30;
                
                GUILayout.BeginArea(new Rect(20, logBoxY, Screen.width - 40, logBoxHeight));
                _imguiScroll = GUILayout.BeginScrollView(_imguiScroll, GUILayout.Width(Screen.width - 40), GUILayout.Height(logBoxHeight));
                foreach (var log in _imguiLogs)
                {
                    GUILayout.Label(log);
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        private void OnDestroy()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }

            if (_channel != null)
            {
                _channel.Dispose();
            }
        }

        private static TMP_FontAsset _koreanFontAsset;

        private TMP_FontAsset GetOrCreateKoreanFont()
        {
            if (_koreanFontAsset != null) return _koreanFontAsset;

            try
            {
                // 1. Try to load font from Resources (malgun.ttf)
                Font osFont = Resources.Load<Font>("malgun");
                
                // Fallback to OS Font if Resources load fails
                if (osFont == null)
                {
                    osFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 14);
                }
                if (osFont == null)
                {
                    osFont = Font.CreateDynamicFontFromOSFont("Gulim", 14);
                }
                
                if (osFont != null)
                {
                    // 2. Create TMP Font Asset from it
                    _koreanFontAsset = TMP_FontAsset.CreateFontAsset(osFont);
                    if (_koreanFontAsset != null)
                    {
                        _koreanFontAsset.name = "MalgunGothic_SDF_Dynamic";
                        Debug.Log("[ObserverUI] Created dynamic Korean Font Asset.");
                        
                        // 3. Register as fallback to default font so any other elements fallback to it
                        var defaultFont = TMP_Settings.defaultFontAsset;
                        if (defaultFont != null)
                        {
                            if (defaultFont.fallbackFontAssetTable == null)
                            {
                                defaultFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                            }
                            
                            bool alreadyExists = false;
                            foreach (var f in defaultFont.fallbackFontAssetTable)
                            {
                                if (f != null && f.name == _koreanFontAsset.name)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }
                            
                            if (!alreadyExists)
                            {
                                defaultFont.fallbackFontAssetTable.Add(_koreanFontAsset);
                                Debug.Log("[ObserverUI] Registered Korean fallback font to default LiberationSans.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObserverUI] Error setting up Korean font: {ex.Message}");
            }

            return _koreanFontAsset;
        }

        private void EnsureKoreanSupport(TextMeshProUGUI tmp)
        {
            if (tmp == null) return;
            var korFont = GetOrCreateKoreanFont();
            if (korFont == null) return;

            // If the text component uses default LiberationSans SDF (which lacks Korean), override directly
            if (tmp.font == null || tmp.font.name == "LiberationSans SDF")
            {
                tmp.font = korFont;
            }
            else
            {
                // Otherwise add to fallback table
                if (tmp.font.fallbackFontAssetTable == null)
                {
                    tmp.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                }
                if (!tmp.font.fallbackFontAssetTable.Contains(korFont))
                {
                    tmp.font.fallbackFontAssetTable.Add(korFont);
                }
            }
        }
    }
}
