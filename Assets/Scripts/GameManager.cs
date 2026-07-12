using System.Collections.Generic;
using UnityEngine;
using MundusVivens.Prototype.Protos;
using LocationInfo = MundusVivens.Prototype.Protos.LocationInfo;
using Vector3 = UnityEngine.Vector3;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Prefabs")]
    public GameObject npcPrefab;
    public GameObject locationPrefab;

    [Header("State")]
    public int CurrentTick = 0;

    private Dictionary<uint, NpcController> _npcDict = new Dictionary<uint, NpcController>();
    private Dictionary<string, GameObject> _locationDict = new Dictionary<string, GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // 서버 접속 직후 한 번만 호출됨
    public void InitializeWorld(IEnumerable<LocationInfo> locations, IEnumerable<NpcSnapshot> initialNpcs)
    {
        foreach (var loc in locations)
        {
            if (_locationDict.ContainsKey(loc.Name)) continue;

            Vector3 pos = new Vector3(loc.Position.X, loc.Position.Y, loc.Position.Z);
            GameObject go = Instantiate(locationPrefab, pos, Quaternion.identity, this.transform);
            go.name = $"Location_{loc.Name}";

            // 위치 표시용 큐브 생성
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform, false);
            cube.transform.localScale = new Vector3(4f, 0.5f, 4f);
            cube.transform.localPosition = Vector3.zero;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
                Color blue = new Color(0.2f, 0.6f, 1.0f);
                mat.color = blue;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", blue);
                renderer.sharedMaterial = mat;
            }

            // 위치 이름 텍스트 (자식에 TextMeshPro가 없으면 동적 생성)
            var tmpro = go.GetComponentInChildren<TMPro.TextMeshPro>();
            if (tmpro == null)
            {
                GameObject textGo = new GameObject("LocationNameText");
                textGo.transform.SetParent(go.transform, false);
                tmpro = textGo.AddComponent<TMPro.TextMeshPro>();
                tmpro.alignment = TMPro.TextAlignmentOptions.Center;
            }
            tmpro.transform.localScale = Vector3.one;
            tmpro.transform.localPosition = new Vector3(0, 0.05f, 0);
            tmpro.text = loc.Name;
            tmpro.fontSize = 3.0f;
            tmpro.enableWordWrapping = false;
            tmpro.color = new Color(0.2f, 0.6f, 1.0f, 0.6f);
            tmpro.transform.rotation = Quaternion.Euler(90f, 0, 0);

            _locationDict[loc.Name] = go;
        }

        foreach (var npc in initialNpcs)
        {
            SpawnOrUpdateNpc(npc);
        }
    }

    // 매 틱마다 호출되어 모든 NPC 상태(좌표/감정) 갱신
    public void UpdateWorldSnapshot(int tick, IEnumerable<NpcSnapshot> npcs)
    {
        CurrentTick = tick;
        UIManager.Instance?.UpdateTickDisplay(tick);

        foreach (var npc in npcs)
        {
            SpawnOrUpdateNpc(npc);
        }
    }

    private void SpawnOrUpdateNpc(NpcSnapshot npcData)
    {
        if (!_npcDict.TryGetValue(npcData.NpcId, out NpcController controller))
        {
            Vector3 startPos = new Vector3(
                npcData.Location.Position.X,
                npcData.Location.Position.Y,
                npcData.Location.Position.Z);

            GameObject go = Instantiate(npcPrefab, startPos, Quaternion.identity);
            go.name = $"NPC_{npcData.DisplayName}";

            // 시각적 캡슐 생성
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.SetParent(go.transform, false);
            capsule.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            capsule.transform.localPosition = new Vector3(0, 1.5f, 0);

            // 자식 콜라이더를 삭제하고 부모(go)에 장착하여 OnMouseDown이 NpcController에서 정상 감지되도록 함
            var childCollider = capsule.GetComponent<Collider>();
            if (childCollider != null) Destroy(childCollider);

            var parentCollider = go.AddComponent<CapsuleCollider>();
            parentCollider.center = new Vector3(0, 1.5f, 0);
            parentCollider.height = 3.0f;
            parentCollider.radius = 0.75f;

            var renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader == null) mat = new Material(Shader.Find("Standard"));

                Color capsuleColor;
                string name = npcData.DisplayName;
                if (name.Contains("늑대") || name.Contains("고블린") ||
                    name.ToLower().Contains("wolf") || name.ToLower().Contains("goblin"))
                    capsuleColor = new Color(0.85f, 0.1f, 0.1f);     // 빨간색: 몬스터
                else if (name.Contains("플레이어") || name.ToLower().Contains("player"))
                    capsuleColor = new Color(0.1f, 0.7f, 0.2f);      // 초록색: 플레이어 아바타
                else
                    capsuleColor = new Color(1.0f, 0.5f, 0.2f);      // 주황색: 일반 NPC

                mat.color = capsuleColor;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", capsuleColor);
                renderer.sharedMaterial = mat;
            }

            controller = go.GetComponent<NpcController>();
            controller.Initialize(npcData.NpcId, npcData.DisplayName);
            _npcDict[npcData.NpcId] = controller;

            Debug.Log($"[GameManager] Spawned NPC: {npcData.DisplayName} (ID: {npcData.NpcId})");
        }

        controller.UpdateStateFromServer(npcData);
    }

    // NPC ID로 이름 조회 헬퍼
    public string GetNpcName(uint npcId)
    {
        return _npcDict.TryGetValue(npcId, out var controller)
            ? controller.DisplayName
            : $"Unknown({npcId})";
    }

    // 3D 뷰어 NPC 머리 위 대사 시퀀스 재생
    public void PlayDialogueBubbleSequence(IList<DialogueLine> lines)
    {
        StartCoroutine(DialogueBubbleCoroutine(lines));
    }

    private System.Collections.IEnumerator DialogueBubbleCoroutine(IList<DialogueLine> lines)
    {
        foreach (var line in lines)
        {
            if (_npcDict.TryGetValue(line.SpeakerId, out var controller))
            {
                controller.ShowSpeechBubble(line.Text, 3.5f);
            }
            yield return new WaitForSeconds(4.0f);
        }
    }
}
