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
            InitializeMappings();
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
                    break;

                case WorldEvent.EventOneofCase.Dialogue:
                    var dialogue = worldEvent.Dialogue;
                    string nameA = _npcKoreanNames.TryGetValue(dialogue.AgentAId, out var na) ? na : dialogue.AgentAId;
                    string nameB = _npcKoreanNames.TryGetValue(dialogue.AgentBId, out var nb) ? nb : dialogue.AgentBId;

                    if (dialogue.IsStarted)
                    {
                        AddLog($"💬 [{timeStr}] <b>대화 시작</b>: {nameA} ⬌ {nameB} (위치: {dialogue.Location})");
                        ShowDialogueBubble(nameA, nameB, "대화 진행 중...");
                    }
                    else
                    {
                        AddLog($"🔔 [{timeStr}] <b>대화 종료</b>\n 요약: \"{dialogue.Summary}\"");
                        HideDialogueBubble();

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

        private void ShowDialogueBubble(string nameA, string nameB, string text)
        {
            if (dialogueBubbleContainer != null && dialogueBubbleText != null)
            {
                dialogueBubbleContainer.gameObject.SetActive(true);
                dialogueBubbleText.text = $"💬 <b>{nameA} ⬌ {nameB}</b>\n{text}";
            }
        }

        private void HideDialogueBubble()
        {
            if (dialogueBubbleContainer != null)
            {
                dialogueBubbleContainer.gameObject.SetActive(false);
            }
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
    }
}
