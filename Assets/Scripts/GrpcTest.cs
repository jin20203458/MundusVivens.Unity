using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using Cysharp.Net.Http;
using MundusVivens.Prototype.Protos;
using UnityEngine;

namespace MundusVivens.Unity
{
    public class GrpcTest : MonoBehaviour
    {
        [Header("gRPC Configuration")]
        [SerializeField] private string serverUrl = "http://localhost:5001"; // C# Backend default port

        private GrpcChannel _channel;
        private MundusVivensGrpc.MundusVivensGrpcClient _client;

        private void Start()
        {
            try
            {
                Debug.Log("[gRPC] Initializing gRPC Channel using YetAnotherHttpHandler...");

                // 1. YetAnotherHttpHandler 설정 (Unity에서 HTTP/2 통신을 지원하기 위해 필수)
                var handler = new YetAnotherHttpHandler()
                {
                    Http2Only = true
                };

                // 2. gRPC 채널 생성
                _channel = GrpcChannel.ForAddress(serverUrl, new GrpcChannelOptions
                {
                    HttpHandler = handler
                });

                // 3. gRPC 클라이언트 스텁 인스턴스 생성
                _client = new MundusVivensGrpc.MundusVivensGrpcClient(_channel);

                Debug.Log("[gRPC] gRPC Client successfully initialized. Ready to test connection.");
                
                // 테스트 호출 시작
                TestConnectionAsync().Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[gRPC] Initialization failed: {ex.Message}");
            }
        }

        private async UniTaskVoid TestConnectionAsync()
        {
            try
            {
                Debug.Log("[gRPC] Sending test request (GetAgentStatus for 'npc_eva')...");

                var request = new GetAgentStatusRequest { AgentId = "npc_eva" };
                
                // UniTask를 사용해 비동기 호출
                var response = await _client.GetAgentStatusAsync(request).ResponseAsync.AsUniTask();

                Debug.Log($"[gRPC] Connection success! Agent Info -> Name: {response.Name}, Location: {response.Location}, Emotion: {response.Emotion}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[gRPC] Test call failed: {ex.Message}. (Note: This is expected if the C# Backend server is not running on {serverUrl})");
            }
        }

        private void OnDestroy()
        {
            if (_channel != null)
            {
                _channel.Dispose();
                Debug.Log("[gRPC] gRPC Channel disposed.");
            }
        }
    }
}
