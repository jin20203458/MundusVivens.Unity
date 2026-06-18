using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Cysharp.Net.Http;
using MundusVivens.Prototype.Protos;
using UnityEngine;

namespace MundusVivens.Unity
{
    public class GrpcObserverTest : MonoBehaviour
    {
        [Header("gRPC Configuration")]
        [SerializeField] private string serverUrl = "http://localhost:5001";
        [SerializeField] private string subscriberId = "Unity_Observer_Console";

        private GrpcChannel _channel;
        private MundusVivensGrpc.MundusVivensGrpcClient _client;
        private CancellationTokenSource _cts;

        private void Start()
        {
            try
            {
                Debug.Log("[Observer] Initializing Observer Connection...");

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

                Debug.Log("[Observer] gRPC Channel initialized. Connecting to event stream...");
                
                // 실시간 스트리밍 이벤트 구독 루프 시작
                SubscribeEventsAsync(_cts.Token).Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Observer] Initialization failed: {ex.Message}");
            }
        }

        private async UniTaskVoid SubscribeEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var request = new SubscribeRequest { SubscriberId = subscriberId };
                
                // 서버 스트리밍 RPC 호출
                using var call = _client.SubscribeWorldEvents(request, cancellationToken: cancellationToken);
                
                Debug.Log("[Observer] Event stream connected. Waiting for server events...");

                // 비동기로 스트림에서 오는 메시지를 순차적으로 읽어들임
                while (await call.ResponseStream.MoveNext(cancellationToken))
                {
                    var worldEvent = call.ResponseStream.Current;
                    ProcessWorldEvent(worldEvent);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Debug.Log("[Observer] Stream subscription was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Observer] Event stream disconnected: {ex.Message}");
            }
        }

        private void ProcessWorldEvent(WorldEvent worldEvent)
        {
            string timeStr = DateTimeOffset.FromUnixTimeMilliseconds(worldEvent.Timestamp).ToLocalTime().ToString("HH:mm:ss");

            switch (worldEvent.EventCase)
            {
                case WorldEvent.EventOneofCase.Tick:
                    var tick = worldEvent.Tick;
                    Debug.Log($"⏱️ <b>[{timeStr}][Tick]</b> 월드 시간 흘러감 -> <b>틱 {tick.TickNumber}</b>");
                    break;

                case WorldEvent.EventOneofCase.Movement:
                    var move = worldEvent.Movement;
                    Debug.Log($"🏃 <b>[{timeStr}][Move]</b> 에이전트 <b>'{move.AgentId}'</b> 이동: {move.FromLocation} ➔ {move.ToLocation}");
                    break;

                case WorldEvent.EventOneofCase.Dialogue:
                    var dialogue = worldEvent.Dialogue;
                    if (dialogue.IsStarted)
                    {
                        Debug.Log($"💬 <b>[{timeStr}][Dialogue]</b> 대화 시작 -> <b>{dialogue.AgentAId} ⬌ {dialogue.AgentBId}</b> (위치: {dialogue.Location}, Job ID: {dialogue.TaskId.Substring(0, 8)})");
                    }
                    else
                    {
                        Debug.Log($"🔔 <b>[{timeStr}][Dialogue]</b> 대화 종료 -> <b>{dialogue.AgentAId} ⬌ {dialogue.AgentBId}</b> (결과 요약: {dialogue.Summary})");
                        foreach (var line in dialogue.Lines)
                        {
                            Debug.Log($"    ↳ <b>{line.SpeakerName}</b>: \"{line.Text}\"");
                        }
                    }
                    break;

                case WorldEvent.EventOneofCase.Gossip:
                    var gossip = worldEvent.Gossip;
                    Debug.Log($"📢 <b>[{timeStr}][Gossip]</b> 소문 전파 -> {gossip.SpeakerId} ➔ {gossip.ListenerId} (대상: {gossip.SubjectId}, 와전 여부: {gossip.IsMutated})");
                    break;

                case WorldEvent.EventOneofCase.Relationship:
                    var rel = worldEvent.Relationship;
                    Debug.Log($"❤️ <b>[{timeStr}][Relationship]</b> 관계 변동 -> {rel.FromAgentId} ➔ {rel.ToAgentId} (호감도: {rel.NewAffinity}({rel.AffinityDelta:+0;-0;0}), 신뢰도: {rel.NewTrust}({rel.TrustDelta:+0;-0;0}))");
                    break;

                default:
                    Debug.LogWarning($"❓ <b>[{timeStr}][Unknown]</b> 알 수 없는 이벤트 수신: {worldEvent.EventCase}");
                    break;
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
            Debug.Log("[Observer] Observer connections cleaned up.");
        }
    }
}
