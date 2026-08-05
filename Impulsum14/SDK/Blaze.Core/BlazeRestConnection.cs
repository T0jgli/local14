using System.Net;
using System.Net.Sockets;

namespace Blaze.Core;

/// <summary>
/// Wraps an HTTP request context for REST endpoints.
/// Implements IBlazeConnection so it can be used in BlazeRpcContext alongside BlazeRpcConnection.
/// </summary>
public class BlazeRestConnection : IBlazeConnection
{
    private readonly TcpClient _client;
    private readonly Stream? _tlsStream;

    public Guid Id { get; } = Guid.NewGuid();
    public EndPoint? RemoteEndPoint { get; }
    public EndPoint? LocalEndPoint { get; }
    public bool Connected => _client.Connected;
    public object? State { get; set; }
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;

    internal Stream Stream { get; }

    internal BlazeRestConnection(TcpClient client)
    {
        _client = client;
        Stream = client.GetStream();
        RemoteEndPoint = client.Client.RemoteEndPoint;
        LocalEndPoint = client.Client.LocalEndPoint;
    }

    internal BlazeRestConnection(TcpClient client, Stream tlsStream)
    {
        _client = client;
        _tlsStream = tlsStream;
        Stream = tlsStream;
        RemoteEndPoint = client.Client.RemoteEndPoint;
        LocalEndPoint = client.Client.LocalEndPoint;
    }

    public void Disconnect()
    {
        _tlsStream?.Dispose();
        _client.Close();
    }

    public override string ToString()
    {
        string protocol = _tlsStream != null ? "HTTPS" : "REST";
        if (RemoteEndPoint != null)
            return $"[{protocol} {RemoteEndPoint}]";

        return $"[{protocol} Conn: {Id}]";
    }
}
