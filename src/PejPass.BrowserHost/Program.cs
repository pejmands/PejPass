using System.Buffers.Binary;
using System.Text.Json;

namespace PejPass.BrowserHost;

internal static class Program
{
    private const int MaximumMessageLength = 1024 * 1024;
    private const int ProtocolVersion = 1;

    private static int Main()
    {
        try
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();

            var header = new byte[sizeof(int)];
            var firstByte = input.ReadByte();

            if (firstByte < 0)
                return 0;

            header[0] = (byte)firstByte;
            input.ReadExactly(header.AsSpan(1));

            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(header);

            if (messageLength <= 0 || messageLength > MaximumMessageLength)
                return 1;

            var messageBytes = new byte[messageLength];
            input.ReadExactly(messageBytes);

            var response = CreateResponse(messageBytes);
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);

            BinaryPrimitives.WriteInt32LittleEndian(header, responseBytes.Length);
            output.Write(header);
            output.Write(responseBytes);
            output.Flush();

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static HostResponse CreateResponse(byte[] messageBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(messageBytes);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                typeElement.GetString() != "ping" ||
                !root.TryGetProperty("protocolVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var version) ||
                version != ProtocolVersion)
            {
                return new HostResponse("error", "PejPass", ProtocolVersion, "unsupported_request");
            }

            return new HostResponse("pong", "PejPass", ProtocolVersion, null);
        }
        catch (JsonException)
        {
            return new HostResponse("error", "PejPass", ProtocolVersion, "invalid_message");
        }
    }

    private sealed record HostResponse(
        string Type,
        string App,
        int ProtocolVersion,
        string? Code);
}
