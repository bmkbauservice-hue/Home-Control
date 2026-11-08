using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace HomeControl.Api.Blink;

// Prüft, ob eine Kamera wirklich Videodaten liefert. Es wird nichts gespeichert, nur gezählt.
// Blink nutzt dafür das eigene Protokoll "immis" (TLS mit eigenem Rahmenformat, darin MPEG-TS).
// Aufbau wie im Projekt blinkpy (livestream.py), siehe docs/BLINK.md.
public static class BlinkLiveProbe
{
    private const byte VideoFrame = 0x00;
    private const byte KeepAliveFrame = 0x0A;
    private const byte LatencyFrame = 0x12;
    private const byte MpegTsSync = 0x47;
    private const int MaxPayloadBytes = 1024 * 1024;

    public sealed record Server(string Host, int Port, int ClientId, string ConnectionId);

    // Nur Blink-eigene immis-Adressen. Der Rechner verbindet sich nie mit einem frei wählbaren Ziel.
    public static Server? ParseServer(string? server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != "immis" || uri.Port <= 0
            || !uri.Host.EndsWith(".immedia-semi.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lastSegment = uri.AbsolutePath.TrimEnd('/').Split('/')[^1];
        var connectionId = lastSegment.Split("__")[0];
        _ = int.TryParse(HttpUtility.ParseQueryString(uri.Query)["client_id"], out var clientId);
        return connectionId.Length == 0 ? null : new Server(uri.Host, uri.Port, clientId, connectionId);
    }

    // Anmelderahmen, 122 Byte. Das Token-Feld bleibt leer, die Berechtigung steckt in der Connection-Id.
    public static byte[] BuildAuthFrame(string serial, int clientId, string connectionId)
    {
        var frame = new List<byte>(122);
        frame.AddRange([0, 0, 0, 0x28]);
        AddFixedText(frame, serial, 16);
        frame.AddRange(BigEndian(clientId));
        frame.AddRange([0x01, 0x08]);
        frame.AddRange([0, 0, 0, 0x40]);
        frame.AddRange(new byte[64]);
        AddFixedText(frame, connectionId, 16);
        frame.AddRange([0, 0, 0, 1]);
        return frame.ToArray();
    }

    private static void AddFixedText(List<byte> frame, string text, int length)
    {
        frame.AddRange(BigEndian(length));
        var bytes = new byte[length];
        var source = Encoding.UTF8.GetBytes(text);
        Array.Copy(source, bytes, Math.Min(source.Length, length));
        frame.AddRange(bytes);
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    public static bool IsVideoPayload(byte messageType, ReadOnlySpan<byte> payload) =>
        messageType == VideoFrame && payload.Length > 0 && payload[0] == MpegTsSync;

    // Liest wenige Sekunden lang und zählt echte Videopakete. Die Kamera bleibt dabei nur kurz wach.
    public static async Task<(int Frames, long Bytes)> ReadVideoAsync(
        Server server, string serial, TimeSpan duration, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(server.Host, server.Port, timeout.Token);
        // Blink nutzt hier kein öffentlich prüfbares Zertifikat. Übertragen wird nur Video, das nicht gespeichert wird.
        await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = server.Host }, timeout.Token);
        await tls.WriteAsync(BuildAuthFrame(serial, server.ClientId, server.ConnectionId), timeout.Token);

        var sender = SendKeepAliveAsync(tls, timeout.Token);
        var frames = 0;
        long bytes = 0;
        var header = new byte[9];
        try
        {
            while (frames < 30)
            {
                await tls.ReadExactlyAsync(header, timeout.Token);
                var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5));
                if (length is < 0 or > MaxPayloadBytes) break;

                var payload = new byte[length];
                await tls.ReadExactlyAsync(payload, timeout.Token);
                if (IsVideoPayload(header[0], payload))
                {
                    frames++;
                    bytes += length;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or EndOfStreamException or IOException)
        {
            // Zeit abgelaufen oder Verbindung beendet: Es zählt, was bis dahin angekommen ist.
            cancellationToken.ThrowIfCancellationRequested();
        }

        await timeout.CancelAsync();
        try { await sender; }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        return (frames, bytes);
    }

    // Ohne regelmäßige Lebenszeichen beendet die Gegenseite den Strom.
    private static async Task SendKeepAliveAsync(Stream stream, CancellationToken cancellationToken)
    {
        var keepAlive = 0;
        for (var second = 0; !cancellationToken.IsCancellationRequested; second++)
        {
            if (second % 10 == 0)
            {
                await stream.WriteAsync(FrameHeader(KeepAliveFrame, keepAlive++, 0), cancellationToken);
            }
            await stream.WriteAsync(FrameHeader(LatencyFrame, 1000, 24).Concat(new byte[24]).ToArray(), cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static byte[] FrameHeader(byte type, int sequence, int length)
    {
        var header = new byte[9];
        header[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), sequence);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5), length);
        return header;
    }
}
