#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class SetupViewerScene : EditorWindow
{
    [MenuItem("Tools/Auto Setup Simulation Viewer")]
    public static void Setup()
    {
        Debug.Log("🚀 [SetupViewerScene] 시작: 씬 자동 구성 중...");

        // 0. 이전 세팅 잔재 및 더미 오브젝트 클린업 (중요: Canvas, GroundPlane을 지우고 재생성하여 오류 해결)
        GameObject oldCanvas = GameObject.Find("Canvas");
        if (oldCanvas != null) DestroyImmediate(oldCanvas);

        GameObject oldGround = GameObject.Find("GroundPlane");
        if (oldGround != null) DestroyImmediate(oldGround);

        GameObject oldES = GameObject.Find("EventSystem");
        if (oldES != null) DestroyImmediate(oldES);

        foreach (var obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.StartsWith("NPC_") || obj.name.StartsWith("Location_") || obj.name.Contains("Prefab") || obj.name == "New Text")
            {
                DestroyImmediate(obj);
            }
        }
        
        // 매니저 중복 제거 (첫 번째 것만 남김)
        void CleanDuplicates(string name)
        {
            var objs = GameObject.FindObjectsOfType<GameObject>();
            int count = 0;
            foreach (var o in objs)
            {
                if (o.name == name)
                {
                    if (count > 0) DestroyImmediate(o);
                    else count++;
                }
            }
        }
        CleanDuplicates("NetworkManager");
        CleanDuplicates("GameManager");
        CleanDuplicates("UIManager");

        // 1. 게임 서버용 매니저 오브젝트 생성 및 컴포넌트 추가
        GameObject nwGo = GameObject.Find("NetworkManager");
        if (nwGo == null) nwGo = new GameObject("NetworkManager");
        if (nwGo.GetComponent<NetworkManager>() == null) nwGo.AddComponent<NetworkManager>();
        if (nwGo.GetComponent<PacketProcessor>() == null) nwGo.AddComponent<PacketProcessor>();

        GameObject gmGo = GameObject.Find("GameManager") ?? new GameObject("GameManager");
        GameManager gm = gmGo.GetComponent<GameManager>() ?? gmGo.AddComponent<GameManager>();

        GameObject uiGo = GameObject.Find("UIManager") ?? new GameObject("UIManager");
        UIManager ui = uiGo.GetComponent<UIManager>() ?? uiGo.AddComponent<UIManager>();

        // 2. 기존 프리팹 로드 및 스크립트 연결
        GameObject npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NpcPrefab.prefab");
        GameObject locPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LocationPrefab.prefab");

        if (npcPrefab != null)
        {
            gm.npcPrefab = npcPrefab;
            
            // NpcController가 없으면 추가
            var npcCtrl = npcPrefab.GetComponent<NpcController>();
            if (npcCtrl == null)
            {
                npcCtrl = npcPrefab.AddComponent<NpcController>();
            }

            // TextMeshPro 캐싱 및 생성
            var tmpros = npcPrefab.GetComponentsInChildren<TextMeshPro>();
            if (tmpros.Length >= 2)
            {
                npcCtrl.nameText = tmpros[0];
                npcCtrl.statusText = tmpros[1];
            }
            else
            {
                if (npcCtrl.nameText == null)
                {
                    GameObject nameTextGo = new GameObject("NameText");
                    nameTextGo.transform.SetParent(npcPrefab.transform, false);
                    nameTextGo.transform.localPosition = new Vector3(0, 1.2f, 0);
                    var t = nameTextGo.AddComponent<TextMeshPro>();
                    t.alignment = TextAlignmentOptions.Center;
                    npcCtrl.nameText = t;
                }
                if (npcCtrl.statusText == null)
                {
                    GameObject statusTextGo = new GameObject("StatusText");
                    statusTextGo.transform.SetParent(npcPrefab.transform, false);
                    statusTextGo.transform.localPosition = new Vector3(0, 0.7f, 0);
                    var t = statusTextGo.AddComponent<TextMeshPro>();
                    t.alignment = TextAlignmentOptions.Center;
                    npcCtrl.statusText = t;
                }
            }
            
            // 폰트 크기 강제 업데이트 (크기 조정)
            if (npcCtrl.nameText != null) npcCtrl.nameText.fontSize = 1.0f;
            if (npcCtrl.statusText != null) npcCtrl.statusText.fontSize = 0.8f;

            PrefabUtility.SavePrefabAsset(npcPrefab);
        }
        else
        {
            Debug.LogError("⚠️ Assets/NpcPrefab.prefab을 찾을 수 없습니다.");
        }

        if (locPrefab != null)
        {
            gm.locationPrefab = locPrefab;
            
            // Location 자식에 텍스트가 없으면 생성
            var tmpro = locPrefab.GetComponentInChildren<TextMeshPro>();
            if (tmpro == null)
            {
                GameObject textGo = new GameObject("LocationNameText");
                textGo.transform.SetParent(locPrefab.transform, false);
                textGo.transform.localPosition = new Vector3(0, 1.5f, 0);
                tmpro = textGo.AddComponent<TextMeshPro>();
                tmpro.alignment = TextAlignmentOptions.Center;
            }
            
            // 폰트 크기 강제 업데이트
            if (tmpro != null) tmpro.fontSize = 1.5f;
            
            PrefabUtility.SavePrefabAsset(locPrefab);
        }
        else
        {
            Debug.LogError("⚠️ Assets/LocationPrefab.prefab을 찾을 수 없습니다.");
        }

        // 카메라 시점 넓게 조정 (NPC 이동을 잘 보기 위해 쿼터뷰로 설정 및 컨트롤러 부착)
        Camera mainCam = Camera.main;
        if (mainCam == null) mainCam = GameObject.FindObjectOfType<Camera>();
        if (mainCam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            mainCam = camGo.AddComponent<Camera>();
        }

        mainCam.orthographic = false; // 3D 퍼스펙티브 강제
        mainCam.transform.position = new Vector3(50f, 150f, 50f);
        mainCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        mainCam.farClipPlane = 2000f; // 원거리 클리핑 방지 (더 늘림)
        
        if (mainCam.GetComponent<CameraController>() == null)
        {
            mainCam.gameObject.AddComponent<CameraController>();
        }

        // 바닥 평면(Ground Plane) 생성 (맵 크기 100x100에 맞춰 10배 스케일)
        GameObject ground = GameObject.Find("GroundPlane");
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "GroundPlane";
            ground.transform.position = new Vector3(50f, -0.1f, 50f);
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100x100 크기
            
            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (darkMat.shader == null) darkMat = new Material(Shader.Find("Standard")); // fallback
                
                // URP는 _BaseColor를 사용하고 레거시는 _Color를 사용하므로 둘 다 세팅
                darkMat.color = new Color(0.2f, 0.2f, 0.2f);
                if (darkMat.HasProperty("_BaseColor")) darkMat.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
                
                renderer.sharedMaterial = darkMat;
            }
        }

        EditorUtility.SetDirty(gm);

        // 3. UI 도화지 (Canvas) 구성
        Canvas canvas = GameObject.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        // 이벤트 시스템 추가
        if (GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // 4. Tick Text (상단 틱 표시판) 생성 및 연결
        GameObject tickTextGo = GameObject.Find("TickText");
        if (tickTextGo == null)
        {
            tickTextGo = new GameObject("TickText");
            tickTextGo.transform.SetParent(canvas.transform, false);
            var text = tickTextGo.AddComponent<TextMeshProUGUI>();
            text.text = "Current Tick: 0";
            text.fontSize = 24;
            
            RectTransform rt = tickTextGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, -20);
            rt.sizeDelta = new Vector2(300, 50);
        }
        ui.tickText = tickTextGo.GetComponent<TextMeshProUGUI>();

        // 5. LogScroll (우측 대화 스크롤창) 생성 및 연결
        GameObject logScrollGo = GameObject.Find("LogScroll");
        if (logScrollGo == null)
        {
            logScrollGo = new GameObject("LogScroll");
            logScrollGo.transform.SetParent(canvas.transform, false);
            ScrollRect sr = logScrollGo.AddComponent<ScrollRect>();
            
            // 백그라운드 이미지
            Image bg = logScrollGo.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.5f);

            // Viewport
            GameObject vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(logScrollGo.transform, false);
            RectTransform vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            vpGo.AddComponent<Image>();
            vpGo.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            GameObject contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            RectTransform contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0, 1);
            contentRt.sizeDelta = new Vector2(0, 300);

            // Log Text
            GameObject logTextGo = new GameObject("LogText");
            logTextGo.transform.SetParent(contentGo.transform, false);
            RectTransform textRt = logTextGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            var text = logTextGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 14;
            text.text = "=== 실시간 시뮬레이션 로그 ===";
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.BottomLeft;

            sr.viewport = vpRt;
            sr.content = contentRt;
            sr.vertical = true;
            sr.horizontal = false;

            // 위치 설정 (우측)
            RectTransform scrollRt = logScrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(1, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.pivot = new Vector2(1, 0.5f);
            scrollRt.anchoredPosition = new Vector2(-20, 0);
            scrollRt.sizeDelta = new Vector2(400, -100);

            ui.logText = text;
            ui.logScrollRect = sr;
        }

        // 6. DetailsPanel (좌측 하단 상세창) 생성 및 연결
        GameObject detailsGo = GameObject.Find("DetailsPanel");
        if (detailsGo == null)
        {
            detailsGo = new GameObject("DetailsPanel");
            detailsGo.transform.SetParent(canvas.transform, false);
            Image img = detailsGo.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            RectTransform rt = detailsGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(20, 20);
            rt.sizeDelta = new Vector2(300, 200);

            // NPC Name Text
            GameObject nameGo = new GameObject("NpcNameText");
            nameGo.transform.SetParent(detailsGo.transform, false);
            var nameText = nameGo.AddComponent<TextMeshProUGUI>();
            nameText.fontSize = 20;
            nameText.text = "선택된 NPC 없음";
            RectTransform nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 1);
            nameRt.anchorMax = new Vector2(1, 1);
            nameRt.pivot = new Vector2(0.5f, 1);
            nameRt.anchoredPosition = new Vector2(0, -15);
            nameRt.sizeDelta = new Vector2(-20, 30);

            // NPC Status Text
            GameObject statusGo = new GameObject("NpcStatusText");
            statusGo.transform.SetParent(detailsGo.transform, false);
            var statusText = statusGo.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = 14;
            statusText.text = "상세 데이터를 보려면 NPC를 클릭하세요.";
            RectTransform statusRt = statusGo.GetComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0, 0);
            statusRt.anchorMax = new Vector2(1, 0);
            statusRt.pivot = new Vector2(0.5f, 0);
            statusRt.anchoredPosition = new Vector2(0, 15);
            statusRt.sizeDelta = new Vector2(-20, 130);

            ui.npcDetailsPanel = detailsGo;
            ui.npcNameText = nameText;
            ui.npcStatusText = statusText;
        }

        // 7. 한글 SDF 폰트 에셋 자동 연결
        TMP_FontAsset malgunSdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/malgun SDF.asset");
        if (malgunSdf != null)
        {
            if (ui.tickText != null) ui.tickText.font = malgunSdf;
            if (ui.logText != null) ui.logText.font = malgunSdf;
            if (ui.npcNameText != null) ui.npcNameText.font = malgunSdf;
            if (ui.npcStatusText != null) ui.npcStatusText.font = malgunSdf;
            
            // 프리팹들도 폰트 갱신
            if (npcPrefab != null)
            {
                var npcCtrl = npcPrefab.GetComponent<NpcController>();
                if (npcCtrl.nameText != null) npcCtrl.nameText.font = malgunSdf;
                if (npcCtrl.statusText != null) npcCtrl.statusText.font = malgunSdf;
                EditorUtility.SetDirty(npcPrefab);
            }
            if (locPrefab != null)
            {
                var locText = locPrefab.GetComponentInChildren<TextMeshPro>();
                if (locText != null) locText.font = malgunSdf;
                EditorUtility.SetDirty(locPrefab);
            }
        }
        else
        {
            Debug.LogWarning("⚠️ Assets/malgun SDF.asset을 찾을 수 없습니다. 1단계에서 폰트 에셋을 먼저 생성해야 폰트가 자동 적용됩니다.");
        }

        EditorUtility.SetDirty(ui);

        // 8. 씬 변경사항 저장
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("🎯 [SetupViewerScene] 구성 완료! 이제 에디터 상단 메뉴나 빌드를 실행할 수 있습니다.");
        EditorUtility.DisplayDialog("완료", "뷰어 씬 자동 세팅이 완벽하게 끝났습니다! Play 버튼을 눌러보세요.", "확인");
    }
}
#endif
