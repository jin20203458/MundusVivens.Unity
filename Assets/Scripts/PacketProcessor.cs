using System;
using UnityEngine;
using MundusVivens.Prototype.Protos;

public class PacketProcessor : MonoBehaviour
{
    // 패킷 ID 상수 (C++ 서버의 PacketProtocol.h와 동기화)
    private const ushort SC_LOGIN_ACK            = 0x1001;
    private const ushort SC_WORLD_SNAPSHOT       = 0x1002;
    private const ushort SC_DIALOGUE_EVENT       = 0x1004;
    private const ushort SC_NPC_REPLY            = 0x1005;
    private const ushort SC_GET_AGENT_STATUS_ACK = 0x1008;
    private const ushort SC_HEARTBEAT_ACK        = 0x10FF;

    private void Update()
    {
        if (NetworkManager.Instance == null) return;

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
                    HandleLoginAck(LoginResponse.Parser.ParseFrom(item.Payload));
                    break;

                case SC_WORLD_SNAPSHOT:
                    HandleWorldSnapshot(WorldSnapshotPayload.Parser.ParseFrom(item.Payload));
                    break;

                case SC_DIALOGUE_EVENT:
                    HandleDialogueEvent(DialogueEventPayload.Parser.ParseFrom(item.Payload));
                    break;

                case SC_GET_AGENT_STATUS_ACK:
                    HandleGetAgentStatusAck(GetAgentStatusResponse.Parser.ParseFrom(item.Payload));
                    break;

                case SC_NPC_REPLY:
                    // [Phase-Avatar] 플레이어 직접 대화 구현 시 NpcReplyPayload 파싱 후 UIManager.ShowNpcReply() 호출
                    break;

                case SC_HEARTBEAT_ACK:
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

        Debug.Log($"[Login] Success! NPC Count={response.Npcs.Count}, Location Count={response.Locations.Count}");
        GameManager.Instance?.InitializeWorld(response.Locations, response.Npcs);
    }

    private void HandleWorldSnapshot(WorldSnapshotPayload payload)
    {
        GameManager.Instance?.UpdateWorldSnapshot(payload.Tick, payload.Npcs);
    }

    private void HandleDialogueEvent(DialogueEventPayload payload)
    {
        UIManager.Instance?.ShowDialogueEvent(payload);
    }

    private void HandleGetAgentStatusAck(GetAgentStatusResponse response)
    {
        UIManager.Instance?.UpdateNpcDetails(response);
    }
}
