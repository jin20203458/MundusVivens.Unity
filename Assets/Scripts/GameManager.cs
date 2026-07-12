using System.Collections.Generic;
using UnityEngine;
using MundusVivens.Prototype.Protos; // Protobuf 네임스페이스
using LocationInfo = MundusVivens.Prototype.Protos.LocationInfo;
using Vector3 = UnityEngine.Vector3;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Prefabs")]
    public GameObject npcPrefab;      // NPC를 표현할 프리팹 (2D Sprite 혹은 3D Capsule)
    public GameObject locationPrefab; // 랜드마크(마을 광장, 술집 등)를 표현할 프리팹

    [Header("State")]
    public int CurrentTick = 0;

    // NPC ID를 키로 하여 씬에 배치된 NPC 컨트롤러들을 관리합니다.
    private Dictionary<uint, NpcController> _npcDict = new Dictionary<uint, NpcController>();
    
    // 랜드마크 이름 보관용
    private Dictionary<string, GameObject> _locationDict = new Dictionary<string, GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // 서버 접속 직후 한 번만 호출됨
    public void InitializeWorld(IEnumerable<LocationInfo> locations, IEnumerable<NpcSnapshot> initialNpcs)
    {
                // 1. 랜드마크 스폰
        foreach (var loc in locations)
        {
            if (!_locationDict.ContainsKey(loc.Name))
            {
                Vector3 pos = new Vector3(loc.Position.X, loc.Position.Y, loc.Position.Z);
                GameObject go = Instantiate(locationPrefab, pos, Quaternion.identity, this.transform);
                go.name = $"Location_{loc.Name}";
                // 2. 위치에 시각적인 큐브 생성 (위치 표시용)
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(go.transform, false);
                cube.transform.localScale = new Vector3(4f, 0.5f, 4f);
                cube.transform.localPosition = Vector3.zero;
                
                var renderer = cube.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
                    mat.color = new Color(0.2f, 0.6f, 1.0f); // 파란색 큐브
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.2f, 0.6f, 1.0f));
                    renderer.sharedMaterial = mat;
                }

                // 간단히 위치 이름 표시 (자식 객체에 TextMeshPro가 있다고 가정, 없으면 강제 생성)
                var tmpro = go.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmpro == null)
                {
                    GameObject textGo = new GameObject("LocationNameText");
                    textGo.transform.SetParent(go.transform, false);
                    tmpro = textGo.AddComponent<TMPro.TextMeshPro>();
                    tmpro.alignment = TMPro.TextAlignmentOptions.Center;
                }
                tmpro.transform.localPosition = new Vector3(0, 2.0f, 0);
                tmpro.text = loc.Name;
                tmpro.fontSize = 30f; // 쿼터뷰에서 잘 보이도록 폰트 크기 대폭 증가
                tmpro.transform.rotation = Quaternion.Euler(90f, 0, 0); // 완전 탑다운 방향(90도)으로 눕혀서 정면으로 보이게 함

                _locationDict[loc.Name] = go;
            }
        }

        // 2. 초기 NPC 스폰
        foreach (var npc in initialNpcs)
        {
            SpawnOrUpdateNpc(npc);
        }
    }

    // 매 틱마다 불리며 모든 NPC의 상태(좌표/감정) 갱신
    public void UpdateWorldSnapshot(int tick, IEnumerable<NpcSnapshot> npcs)
    {
        CurrentTick = tick;
        
        // 상단 UI나 터미널에 현재 틱 표시 가능
        if (UIManager.Instance != null) UIManager.Instance.UpdateTickDisplay(tick);

        foreach (var npc in npcs)
        {
            SpawnOrUpdateNpc(npc);
        }
    }

    // 단일 NPC 긴급 업데이트 (돌발 행동 등)
    public void UpdateSingleNpc(NpcSnapshot npc)
    {
        SpawnOrUpdateNpc(npc);
    }

    private void SpawnOrUpdateNpc(NpcSnapshot npcData)
    {
        if (!_npcDict.TryGetValue(npcData.NpcId, out NpcController controller))
        {
            // 아직 스폰되지 않은 NPC라면 생성
            Vector3 startPos = new Vector3(npcData.Location.Position.X, npcData.Location.Position.Y, npcData.Location.Position.Z);
            GameObject go = Instantiate(npcPrefab, startPos, Quaternion.identity);
            go.name = $"NPC_{npcData.DisplayName}";
            
            // NPC 시각적 캡슐 생성
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.SetParent(go.transform, false);
            capsule.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            capsule.transform.localPosition = new Vector3(0, 1.5f, 0);
            
            var renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
                
                Color capsuleColor = new Color(1.0f, 0.5f, 0.2f); // 기본: 주황색 (일반 NPC)
                if (npcData.DisplayName.Contains("늑대") || npcData.DisplayName.Contains("고블린") || 
                    npcData.DisplayName.ToLower().Contains("wolf") || npcData.DisplayName.ToLower().Contains("goblin"))
                {
                    capsuleColor = new Color(0.85f, 0.1f, 0.1f); // 빨간색: 몬스터
                }
                else if (npcData.DisplayName.Contains("플레이어") || npcData.DisplayName.ToLower().Contains("player"))
                {
                    capsuleColor = new Color(0.1f, 0.7f, 0.2f); // 초록색: 플레이어 아바타
                }

                mat.color = capsuleColor;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", capsuleColor);
                renderer.sharedMaterial = mat;
            }
            
            controller = go.GetComponent<NpcController>();
            controller.Initialize(npcData.NpcId, npcData.DisplayName);
            
            _npcDict[npcData.NpcId] = controller;
            Debug.Log($"[GameManager] Spawned NPC: {npcData.DisplayName} (ID: {npcData.NpcId}) at Pos: {startPos}");
        }

        // NPC 정보 갱신 (목표 좌표, 상태 등 전달)
        Debug.Log($"[GameManager] Updating NPC: {npcData.DisplayName} (ID: {npcData.NpcId}) -> Pos: ({npcData.Location.Position.X}, {npcData.Location.Position.Y}, {npcData.Location.Position.Z}), Activity: {npcData.Activity}");
        controller.UpdateStateFromServer(npcData);
    }
    
    // UI에서 접근하기 편하게 NPC 이름 조회용 헬퍼 함수
    public string GetNpcName(uint npcId)
    {
        if (_npcDict.TryGetValue(npcId, out var controller))
        {
            return controller.DisplayName;
        }
        return $"Unknown({npcId})";
    }
}
