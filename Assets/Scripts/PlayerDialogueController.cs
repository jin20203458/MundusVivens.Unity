using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Cysharp.Net.Http;
using MundusVivens.Prototype.Protos;
using UnityEngine;
using TMPro;

namespace MundusVivens.Unity
{
    public class PlayerDialogueController : MonoBehaviour
    {
        [Header("gRPC Configuration")]
        [SerializeField] private string serverUrl = "http://localhost:5001";
        [SerializeField] private string playerId = "player";

        [Header("UI References (Optional - Fallback to IMGUI if null)")]
        [SerializeField] private GameObject dialoguePanel;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TextMeshProUGUI chatLogText;
        [SerializeField] private UnityEngine.UI.ScrollRect scrollRect;
        [SerializeField] private TextMeshProUGUI npcNameText;

        private GrpcChannel _channel;
        private MundusVivensGrpc.MundusVivensGrpcClient _client;
        private string _activeSessionId = string.Empty;
        private string _targetNpcId = string.Empty;
        private string _targetNpcName = string.Empty;

        private readonly List<string> _chatLines = new();
        private bool _isGenerating = false;

        // IMGUI UI 전용 상태
        private bool _showImguiChat = false;
        private string _imguiInputText = "";
        private Vector2 _imguiScrollPos = Vector2.zero;

        private readonly Dictionary<string, string> _npcKoreanNames = new()
        {
            { "npc_kyle", "카일" },
            { "npc_eva", "에바" },
            { "npc_bart", "바르트" }
        };

        private void Start()
        {
            InitializeChannel();
            if (dialoguePanel != null)
            {
                dialoguePanel.SetActive(false);
            }
        }

        private void InitializeChannel()
        {
            try
            {
                var handler = new YetAnotherHttpHandler()
                {
                    Http2Only = true
                };

                _channel = GrpcChannel.ForAddress(serverUrl, new GrpcChannelOptions
                {
                    HttpHandler = handler
                });

                _client = new MundusVivensGrpc.MundusVivensGrpcClient(_channel);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerDialogue] 채널 생성 실패: {ex.Message}");
            }
        }

        public void StartConversation(string npcId)
        {
            _targetNpcId = npcId;
            _targetNpcName = _npcKoreanNames.TryGetValue(npcId, out var name) ? name : npcId;
            _chatLines.Clear();
            _activeSessionId = string.Empty;

            StartConversationAsync().Forget();
        }

        private async UniTaskVoid StartConversationAsync()
        {
            _isGenerating = true;
            AddLine("System", $"{_targetNpcName}와(과) 대화 연결 시도 중...");

            if (dialoguePanel != null)
            {
                dialoguePanel.SetActive(true);
                if (npcNameText != null) npcNameText.text = _targetNpcName;
            }
            else
            {
                _showImguiChat = true;
            }

            try
            {
                var request = new StartPlayerDialogueRequest
                {
                    PlayerId = playerId,
                    NpcId = _targetNpcId
                };

                var response = await _client.StartPlayerDialogueAsync(request).ResponseAsync.AsUniTask();
                
                _isGenerating = false;

                if (response.Success)
                {
                    _activeSessionId = response.SessionId;
                    AddLine(_targetNpcName, response.Greeting);
                }
                else
                {
                    AddLine("System", $"대화 시작 실패: {response.Message}");
                    await UniTask.Delay(3000);
                    CloseUI();
                }
            }
            catch (Exception ex)
            {
                _isGenerating = false;
                AddLine("System", $"대화 시작 중 오류 발생: {ex.Message}");
                Debug.LogError($"[PlayerDialogue] Start failed: {ex}");
                await UniTask.Delay(3000);
                CloseUI();
            }
        }

        public void SendPlayerMessage()
        {
            string text = "";
            if (inputField != null)
            {
                text = inputField.text;
                inputField.text = "";
            }
            else
            {
                text = _imguiInputText;
                _imguiInputText = "";
            }

            if (string.IsNullOrWhiteSpace(text) || _isGenerating || string.IsNullOrEmpty(_activeSessionId))
            {
                return;
            }

            SendPlayerMessageAsync(text).Forget();
        }

        private async UniTaskVoid SendPlayerMessageAsync(string text)
        {
            _isGenerating = true;
            AddLine("나", text);

            try
            {
                var request = new SendPlayerMessageRequest
                {
                    SessionId = _activeSessionId,
                    Message = text
                };

                var response = await _client.SendPlayerMessageAsync(request).ResponseAsync.AsUniTask();
                _isGenerating = false;

                AddLine(_targetNpcName, response.Reply);
            }
            catch (Exception ex)
            {
                _isGenerating = false;
                AddLine("System", $"메시지 전송 실패: {ex.Message}");
                Debug.LogError($"[PlayerDialogue] Send failed: {ex}");
            }
        }

        public void EndConversation()
        {
            if (string.IsNullOrEmpty(_activeSessionId))
            {
                CloseUI();
                return;
            }

            EndConversationAsync().Forget();
        }

        private async UniTaskVoid EndConversationAsync()
        {
            _isGenerating = true;
            AddLine("System", "대화 요약 및 결과 정산 중...");

            try
            {
                var request = new EndPlayerDialogueRequest
                {
                    SessionId = _activeSessionId
                };

                var response = await _client.EndPlayerDialogueAsync(request).ResponseAsync.AsUniTask();
                _isGenerating = false;
                _activeSessionId = string.Empty;

                AddLine("System", $"대화 종료됨.\n요약: \"{response.Summary}\"");
                
                // 3초 대기 후 UI 닫기
                await UniTask.Delay(3000);
                CloseUI();
            }
            catch (Exception ex)
            {
                _isGenerating = false;
                AddLine("System", $"대화 종료 중 오류 발생: {ex.Message}");
                Debug.LogError($"[PlayerDialogue] End failed: {ex}");
                await UniTask.Delay(2000);
                CloseUI();
            }
        }

        private void CloseUI()
        {
            _showImguiChat = false;
            if (dialoguePanel != null)
            {
                dialoguePanel.SetActive(false);
            }
        }

        private void AddLine(string sender, string text)
        {
            string formatted = $"<b>[{sender}]</b>: {text}";
            _chatLines.Add(formatted);

            if (chatLogText != null)
            {
                chatLogText.text = string.Join("\n\n", _chatLines);
                Canvas.ForceUpdateCanvases();
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        private void OnGUI()
        {
            // Fallback IMGUI UI if no panel is registered and conversation is active
            if (dialoguePanel == null && _showImguiChat)
            {
                float width = 450f;
                float height = 400f;
                float x = (Screen.width - width) / 2f;
                float y = (Screen.height - height) / 2f;

                GUI.Box(new Rect(x, y, width, height), $"대화 상대: {_targetNpcName}");

                // Chat lines display
                float chatY = y + 30f;
                float chatHeight = height - 120f;
                GUILayout.BeginArea(new Rect(x + 10f, chatY, width - 20f, chatHeight));
                _imguiScrollPos = GUILayout.BeginScrollView(_imguiScrollPos, GUILayout.Width(width - 20f), GUILayout.Height(chatHeight));
                foreach (var line in _chatLines)
                {
                    // Clean HTML tags for IMGUI rendering
                    string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, "<.*?>", "");
                    GUILayout.Label(cleanLine);
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();

                // Input Box and Send Button
                float inputY = y + height - 80f;
                GUI.Label(new Rect(x + 10f, inputY, 50f, 20f), "입력:");
                _imguiInputText = GUI.TextField(new Rect(x + 60f, inputY, width - 150f, 25f), _imguiInputText);

                if (GUI.Button(new Rect(x + width - 80f, inputY, 70f, 25f), "전송") || 
                    (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
                {
                    if (!string.IsNullOrWhiteSpace(_imguiInputText))
                    {
                        SendPlayerMessage();
                    }
                }

                // End Button
                float endY = y + height - 40f;
                if (GUI.Button(new Rect(x + 10f, endY, width - 20f, 30f), "대화 종료 및 기록 저장"))
                {
                    EndConversation();
                }
            }
            else if (dialoguePanel == null && !_showImguiChat)
            {
                // Draw conversation initiator buttons on the corner
                GUI.Box(new Rect(10, 10, 220, 150), "Mundus Vivens — Participant Trigger");
                
                if (GUI.Button(new Rect(20, 40, 200, 30), "에바(Eva)와 대화하기"))
                {
                    StartConversation("npc_eva");
                }
                if (GUI.Button(new Rect(20, 80, 200, 30), "바르트(Bart)와 대화하기"))
                {
                    StartConversation("npc_bart");
                }
                if (GUI.Button(new Rect(20, 120, 200, 30), "카일(Kyle)과 대화하기"))
                {
                    StartConversation("npc_kyle");
                }
            }
        }

        private void OnDestroy()
        {
            if (_channel != null)
            {
                _channel.Dispose();
            }
        }
    }
}
