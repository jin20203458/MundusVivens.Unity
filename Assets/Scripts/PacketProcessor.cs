using System;
using UnityEngine;
using MundusVivens.Prototype.Protos; // Protobuf 네임스페이스
using Google.Protobuf; // Parser를 위한 네임스페이스

public class PacketProcessor : MonoBehaviour
{
    // 패킷 ID 정의 (C++ 서버의 PacketProtocol.h와 일치)
    private const ushort SC_LOGIN_ACK       = 0x1001;  // 로그인 응답 + 초기 맵 정보
    private const ushort SC_WORLD_SNAPSHOT  = 0x1002;  // 전체 NPC 상태 브로드캐스트
    private const ushort SC_DIALOGUE_EVENT  = 0x1004;  // NPC간 대화 이벤트 알림
    private const ushort SC_NPC_REPLY       = 0x1005;  // 플레이어에게 NPC 대사 전달
    private const ushort SC_HEARTBEAT_ACK   = 0x10FF;  // 하트비트 응답 (현재 미사용)

    // TODO: [Phase-Avatar] 플레이어 아바타 모드 구현 시 추가 예정
    // CS_PLAYER_ATTACK (0x0006) 수신 처리 → HandlePlayerAttack()
    // CS_PLAYER_DEFEND (0x0007) 수신 처리 → HandlePlayerDefend()
    // → C++에 해당 패킷 정의 및 SystemPlayer.cpp 핸들러 선행 구현 필요

    private void Update()
    {
        if (NetworkManager.Instance == null) return;

        // 큐에 있는 모든 패킷을 꺼내서 처리 (Main Thread에서 실행되므로 Unity API 호출 안전)
        while (NetworkManager.Instance.PacketQueue.TryDequeue(out PacketItem item))
        {
            ProcessPacket(item);
        }
    }

    private void ProcessPacket(PacketItem item)
    {
        try
        {
            switch (item.PacketId)
            {
                case SC_LOGIN_ACK:
                    var loginRes = LoginResponse.Parser.ParseFrom(item.Payload);
                    HandleLoginAck(loginRes);
                    break;

                case SC_WORLD_SNAPSHOT:
                    var snapshot = WorldSnapshotPayload.Parser.ParseFrom(item.Payload);
                    HandleWorldSnapshot(snapshot);
                    break;

                case SC_DIALOGUE_EVENT:
                    var dialogueEvent = DialogueEventPayload.Parser.ParseFrom(item.Payload);
                    HandleDialogueEvent(dialogueEvent);
                    break;

                case SC_NPC_REPLY:
                    // TODO: Phase 3 - 플레이어 대화 구현 시 NpcReplyPayload 파싱 후 UIManager.ShowNpcReply() 호출
                    break;

                case SC_HEARTBEAT_ACK:
                    // 하트비트 ACK 수신 확인 (연결 유지 확인)
                    break;

                default:
                    Debug.LogWarning($"[PacketProcessor] Unknown Packet ID: {item.PacketId:X4}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PacketProcessor] Error parsing packet {item.PacketId:X4}: {ex.Message}");
        }
    }

    private void HandleLoginAck(LoginResponse response)
    {
        if (!response.Success)
        {
            Debug.LogError($"[Login] Failed: {response.Message}");
            return;
        }

        Debug.Log($"[Login] Success! Initializing world. NPC Count={response.Npcs.Count}, Location Count={response.Locations.Count}");
        
        // GameManager를 통해 초기 NPC들을 스폰합니다.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.InitializeWorld(response.Locations, response.Npcs);
        }
    }

    private void HandleWorldSnapshot(WorldSnapshotPayload payload)
    {
        Debug.Log($"[Network] Received SC_WORLD_SNAPSHOT: Tick={payload.Tick}, NPC Count={payload.Npcs.Count}");
        // 틱별로 모든 NPC의 좌표와 상태를 동기화합니다.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateWorldSnapshot(payload.Tick, payload.Npcs);
        }
    }

    private void HandleDialogueEvent(DialogueEventPayload payload)
    {
        Debug.Log($"[Network] Received SC_DIALOGUE_EVENT: TaskID={payload.TaskId}, IsStarted={payload.IsStarted}");
        // 대화 시작/종료 이벤트를 UI와 NPC 머리 위에 띄워줍니다.
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowDialogueEvent(payload);
        }
    }
}
