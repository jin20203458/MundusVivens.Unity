using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class PacketItem
{
    public ushort PacketId;
    public byte[] Payload;
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    public string serverIp = "127.0.0.1";
    public int serverPort = 7777; // C++ TcpServer Port

    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private Thread _receiveThread;
    private bool _isRunning = false;

    // 패킷 처리를 메인 스레드로 넘기기 위한 큐
    [System.NonSerialized]
    public ConcurrentQueue<PacketItem> PacketQueue = new ConcurrentQueue<PacketItem>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        ConnectToServer();
    }

    private void ConnectToServer()
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(serverIp, serverPort);
            _stream = _tcpClient.GetStream();
            _isRunning = true;

            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();

            Debug.Log($"[Network] Connected to Server {serverIp}:{serverPort}");
            
            // 뷰어 모드이므로 일단 더미 계정으로 로그인 요청을 보냅니다 (뷰어용 PlayerID)
            SendLoginRequest("viewer_01", "Observer");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Network] Connection failed: {ex.Message}");
        }
    }

    private void ReceiveLoop()
    {
        byte[] headerBuffer = new byte[4];

        while (_isRunning && _tcpClient != null && _tcpClient.Connected)
        {
            try
            {
                // 1. 헤더 4바이트 읽기
                if (!ReadExact(_stream, headerBuffer, 4))
                    break;

                // 2. Big-Endian으로 길이와 패킷 ID 파싱
                ushort packetLength = (ushort)((headerBuffer[0] << 8) | headerBuffer[1]);
                ushort packetId = (ushort)((headerBuffer[2] << 8) | headerBuffer[3]);

                // 페이로드 길이 = 전체 길이 - 헤더(4)
                int payloadLength = packetLength - 4;
                
                byte[] payloadBuffer = new byte[payloadLength];
                if (payloadLength > 0)
                {
                    if (!ReadExact(_stream, payloadBuffer, payloadLength))
                        break;
                }

                // 3. 큐에 적재 (메인 스레드에서 처리)
                PacketQueue.Enqueue(new PacketItem
                {
                    PacketId = packetId,
                    Payload = payloadBuffer
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Network] Receive Error: {ex.Message}");
                break;
            }
        }

        Debug.Log("[Network] Disconnected from server.");
    }

    private bool ReadExact(NetworkStream stream, byte[] buffer, int length)
    {
        int totalRead = 0;
        while (totalRead < length)
        {
            int read = stream.Read(buffer, totalRead, length - totalRead);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }

    // 서버로 패킷 전송 (Protobuf 바이트 배열을 받아 헤더를 붙여 전송)
    public void SendPacket(ushort packetId, byte[] payload)
    {
        if (_tcpClient == null || !_tcpClient.Connected) return;

        ushort packetLength = (ushort)(4 + (payload != null ? payload.Length : 0));
        byte[] buffer = new byte[packetLength];

        // Length (Big-Endian)
        buffer[0] = (byte)(packetLength >> 8);
        buffer[1] = (byte)(packetLength & 0xFF);

        // Packet ID (Big-Endian)
        buffer[2] = (byte)(packetId >> 8);
        buffer[3] = (byte)(packetId & 0xFF);

        if (payload != null && payload.Length > 0)
        {
            Array.Copy(payload, 0, buffer, 4, payload.Length);
        }

        try
        {
            _stream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Network] Send Error: {ex.Message}");
        }
    }

    // 뷰어 접속용 로그인 요청 (CS_LOGIN = 0x0001)
    private void SendLoginRequest(string playerId, string playerName)
    {
        var loginReq = new MundusVivens.Prototype.Protos.LoginRequest
        {
            PlayerId = playerId,
            PlayerName = playerName
        };

        // Google.Protobuf 직렬화
        byte[] payload = Google.Protobuf.MessageExtensions.ToByteArray(loginReq);
        SendPacket(0x0001, payload);
    }

    private void OnApplicationQuit()
    {
        _isRunning = false;
        if (_stream != null) _stream.Close();
        if (_tcpClient != null) _tcpClient.Close();
    }
}
