using Blaze.Core;
using EATDF;
using Microsoft.Extensions.Logging;
using ProtoFire;
using ProtoFire.Frames;
using ProtoFire.Tls;

namespace FIFAServer14;

internal sealed class ServerCallbacks : IBlazeServerCallbacks
{
    private readonly ProtoSSLCertificate _cert;
    private readonly ILogger _log;
    public ServerCallbacks(ProtoSSLCertificate cert, ILogger log) { _cert = cert; _log = log; }

    public ProtoSSLCertificate SelectCertificate(BlazeRpcConnection connection) => _cert;

    public Task<bool> OnConnectedAsync(BlazeRpcConnection connection)
    { _log.LogInformation("client connected"); return Task.FromResult(true); }

    public Task OnAuthenticatedAsync(BlazeRpcConnection connection, bool secure)
    { _log.LogInformation("TLS authenticated (secure={0})", secure); return Task.CompletedTask; }

    public Task OnDisconnectedAsync(BlazeRpcConnection connection)
    { _log.LogInformation("client disconnected"); return Task.CompletedTask; }

    public Task OnErrorAsync(BlazeRpcConnection connection, Exception exception)
    { _log.LogWarning("connection error: {0}", exception.Message); return Task.CompletedTask; }

    public Task OnRpcCommandErrorAsync(BlazeRpcConnection connection, ProtoFirePacket packet, Tdf request, Exception exception)
    { _log.LogWarning("rpc command error: {0}", exception.Message); return Task.CompletedTask; }

    public Task OnUnhandledAsync(BlazeRpcConnection connection, ProtoFirePacket packet, bool corrupted)
    {
        _log.LogWarning("UNHANDLED command 0x{0:x4}::0x{1:x4} (corrupted={2}) bodyLen={3}",
            packet.Frame.Component, packet.Frame.Command, corrupted, packet.Data?.Length ?? 0);
        var data = packet.Data;
        if (data != null && data.Length > 0)
            _log.LogWarning("  body hex: {0}", Convert.ToHexString(data));

        // reply to any unhandled *message* command with an empty success
        // response so the client doesn't sit on it until defaultRequestTimeout (80s) and then report "EA servers unavailable".

        if (!corrupted && packet.Frame.MessageType == MessageType.Message)
        {
            IFireFrame respFrame = packet.Frame.CreateResponseFrame(0); // 0 => Reply (success)
            var respPacket = new ProtoFirePacket(respFrame, Array.Empty<byte>());
            _log.LogWarning("  -> replying empty (fallback) to 0x{0:x4}::0x{1:x4}",
                packet.Frame.Component, packet.Frame.Command);
            return connection.SendAsync(respPacket);
        }
        return Task.CompletedTask;
    }
}
