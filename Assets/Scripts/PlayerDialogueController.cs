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
            GetOrCreateKoreanFont();
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

            // 🆕 UI 요소가 비어있을 경우 오버레이 대화창 동적 자동 생성
            if (dialoguePanel == null || inputField == null || chatLogText == null || scrollRect == null || npcNameText == null)
            {
                CreateDialogueUIProgrammatically();
            }

            EnsureKoreanSupport(chatLogText);
            EnsureKoreanSupport(npcNameText);
            if (inputField != null)
            {
                EnsureKoreanSupport(inputField.textComponent as TextMeshProUGUI);
                if (inputField.placeholder is TextMeshProUGUI placeholderTmp)
                {
                    EnsureKoreanSupport(placeholderTmp);
                }
            }

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

        // 🆕 동적 대화 UI 오버레이 생성 빌더
        private void CreateDialogueUIProgrammatically()
        {
            var korFont = GetOrCreateKoreanFont();
            // Find canvas
            GameObject canvasGO = GameObject.Find("Dynamic_Observer_Canvas");
            if (canvasGO == null)
            {
                canvasGO = new GameObject("Dynamic_Dialogue_Canvas");
                Canvas canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGO.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(canvasGO);
            }

            // Ensure EventSystem exists for UI raycasting & input field clicks
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
                Debug.Log("[DialogueUI] Created EventSystem programmatically.");
            }
            
            // 1. Full Screen Overlay to block clicks
            dialoguePanel = new GameObject("Dynamic_DialoguePanel", typeof(RectTransform), typeof(Image));
            dialoguePanel.transform.SetParent(canvasGO.transform, false);
            RectTransform dpRT = dialoguePanel.GetComponent<RectTransform>();
            dpRT.anchorMin = Vector2.zero;
            dpRT.anchorMax = Vector2.one;
            dpRT.offsetMin = Vector2.zero;
            dpRT.offsetMax = Vector2.zero;
            dialoguePanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // 반투명 검은 배경
            
            // 2. 대화 오버레이 창 (가운데 정렬)
            GameObject chatBox = new GameObject("ChatBox", typeof(RectTransform), typeof(Image));
            chatBox.transform.SetParent(dialoguePanel.transform, false);
            RectTransform cbRT = chatBox.GetComponent<RectTransform>();
            cbRT.anchorMin = new Vector2(0.5f, 0.5f);
            cbRT.anchorMax = new Vector2(0.5f, 0.5f);
            cbRT.pivot = new Vector2(0.5f, 0.5f);
            cbRT.anchoredPosition = Vector2.zero;
            cbRT.sizeDelta = new Vector2(600f, 480f);
            chatBox.GetComponent<Image>().color = new Color(0.15f, 0.18f, 0.22f, 1f); // 다크 블루 슬레이트
            
            // 3. NPC 이름 헤더 타이틀
            GameObject headerGO = new GameObject("HeaderTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            headerGO.transform.SetParent(chatBox.transform, false);
            RectTransform headerRT = headerGO.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.anchoredPosition = new Vector2(0f, -15f);
            headerRT.sizeDelta = new Vector2(-40f, 40f);
            npcNameText = headerGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) npcNameText.font = korFont;
            npcNameText.fontSize = 18;
            npcNameText.fontStyle = FontStyles.Bold;
            npcNameText.color = Color.white;
            npcNameText.alignment = TextAlignmentOptions.Center;
            npcNameText.text = "NPC 이름";
            
            // 구분선
            GameObject sepGO = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            sepGO.transform.SetParent(chatBox.transform, false);
            RectTransform sepRT = sepGO.GetComponent<RectTransform>();
            sepRT.anchorMin = new Vector2(0f, 1f);
            sepRT.anchorMax = new Vector2(1f, 1f);
            sepRT.pivot = new Vector2(0.5f, 1f);
            sepRT.anchoredPosition = new Vector2(0f, -50f);
            sepRT.sizeDelta = new Vector2(-40f, 2f);
            sepGO.GetComponent<Image>().color = new Color(0.3f, 0.35f, 0.45f, 0.5f);
            
            // 4. 대화 로그 스크롤 영역
            GameObject scrollGO = new GameObject("ChatScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(chatBox.transform, false);
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = new Vector2(20f, 95f); // 입력칸 공간 확보
            scrollRT.offsetMax = new Vector2(-20f, -60f); // 헤더 공간 확보
            scrollRect = scrollGO.GetComponent<ScrollRect>();
            
            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            scrollRect.viewport = vpRT;
            
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            scrollRect.content = contentRT;
            
            GameObject textGO = new GameObject("ChatLogText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(contentRT.transform, false);
            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            chatLogText = textGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) chatLogText.font = korFont;
            chatLogText.fontSize = 14;
            chatLogText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            chatLogText.enableWordWrapping = true;
            chatLogText.alignment = TextAlignmentOptions.TopLeft;
            chatLogText.text = "";
            
            // 5. 입력 필드 생성
            inputField = CreateInputField(chatBox, new Vector2(-45f, 55f), new Vector2(470f, 32f));
            
            // 6. 전송 버튼
            GameObject sendBtnGO = new GameObject("SendBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            sendBtnGO.transform.SetParent(chatBox.transform, false);
            RectTransform sendBtnRT = sendBtnGO.GetComponent<RectTransform>();
            sendBtnRT.anchorMin = new Vector2(0.5f, 0f);
            sendBtnRT.anchorMax = new Vector2(0.5f, 0f);
            sendBtnRT.pivot = new Vector2(0.5f, 0f);
            sendBtnRT.anchoredPosition = new Vector2(235f, 55f);
            sendBtnRT.sizeDelta = new Vector2(90f, 32f);
            sendBtnGO.GetComponent<Image>().color = new Color(0.08f, 0.5f, 0.65f, 1f);
            
            GameObject sendBtnTxtGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            sendBtnTxtGO.transform.SetParent(sendBtnGO.transform, false);
            RectTransform sbtRT = sendBtnTxtGO.GetComponent<RectTransform>();
            sbtRT.anchorMin = Vector2.zero;
            sbtRT.anchorMax = Vector2.one;
            sbtRT.offsetMin = Vector2.zero;
            sbtRT.offsetMax = Vector2.zero;
            var sbt = sendBtnTxtGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) sbt.font = korFont;
            sbt.text = "<b>전송</b>";
            sbt.color = Color.white;
            sbt.fontSize = 13;
            sbt.alignment = TextAlignmentOptions.Center;
            
            var sendBtn = sendBtnGO.GetComponent<Button>();
            sendBtn.onClick.AddListener(SendPlayerMessage);
            
            // 엔터 입력으로 바로 메세지 전송 바인딩
            inputField.onSubmit.AddListener((val) =>
            {
                if (!string.IsNullOrWhiteSpace(val))
                {
                    SendPlayerMessage();
                }
                inputField.ActivateInputField(); // 재포커스
            });
            
            // 7. 대화 종료 버튼 (하단)
            GameObject endBtnGO = new GameObject("EndBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            endBtnGO.transform.SetParent(chatBox.transform, false);
            RectTransform endBtnRT = endBtnGO.GetComponent<RectTransform>();
            endBtnRT.anchorMin = new Vector2(0.5f, 0f);
            endBtnRT.anchorMax = new Vector2(0.5f, 0f);
            endBtnRT.pivot = new Vector2(0.5f, 0f);
            endBtnRT.anchoredPosition = new Vector2(0f, 12f);
            endBtnRT.sizeDelta = new Vector2(560f, 30f);
            endBtnGO.GetComponent<Image>().color = new Color(0.6f, 0.25f, 0.25f, 1f);
            
            GameObject endBtnTxtGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            endBtnTxtGO.transform.SetParent(endBtnGO.transform, false);
            RectTransform ebtRT = endBtnTxtGO.GetComponent<RectTransform>();
            ebtRT.anchorMin = Vector2.zero;
            ebtRT.anchorMax = Vector2.one;
            ebtRT.offsetMin = Vector2.zero;
            ebtRT.offsetMax = Vector2.zero;
            var ebt = endBtnTxtGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) ebt.font = korFont;
            ebt.text = "<b>대화 종료 및 기록 정산</b>";
            ebt.color = Color.white;
            ebt.fontSize = 13;
            ebt.alignment = TextAlignmentOptions.Center;
            
            var endBtn = endBtnGO.GetComponent<Button>();
            endBtn.onClick.AddListener(EndConversation);
        }

        private TMP_InputField CreateInputField(GameObject parent, Vector2 pos, Vector2 size)
        {
            GameObject inputGO = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(parent.transform, false);
            RectTransform rt = inputGO.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            inputGO.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f, 1f);
            
            GameObject textArea = new GameObject("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform taRT = textArea.GetComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero;
            taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(10f, 2f);
            taRT.offsetMax = new Vector2(-10f, -2f);
            
            GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textArea.transform, false);
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            var textComp = textGO.GetComponent<TextMeshProUGUI>();
            var korFont = GetOrCreateKoreanFont();
            if (korFont != null) textComp.font = korFont;
            textComp.fontSize = 14;
            textComp.color = Color.white;
            textComp.alignment = TextAlignmentOptions.MidlineLeft;
            
            GameObject placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            placeholderGO.transform.SetParent(textArea.transform, false);
            RectTransform placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;
            var placeholderComp = placeholderGO.GetComponent<TextMeshProUGUI>();
            if (korFont != null) placeholderComp.font = korFont;
            placeholderComp.text = "메시지를 입력하세요...";
            placeholderComp.fontSize = 14;
            placeholderComp.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            placeholderComp.fontStyle = FontStyles.Italic;
            placeholderComp.alignment = TextAlignmentOptions.MidlineLeft;
            
            var inputFieldComp = inputGO.GetComponent<TMP_InputField>();
            inputFieldComp.textViewport = taRT;
            inputFieldComp.textComponent = textComp;
            inputFieldComp.placeholder = placeholderComp;
            
            return inputFieldComp;
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
                        Debug.Log("[DialogueUI] Created dynamic Korean Font Asset.");
                        
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
                                Debug.Log("[DialogueUI] Registered Korean fallback font to default LiberationSans.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueUI] Error setting up Korean font: {ex.Message}");
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
