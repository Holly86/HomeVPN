using System.IO.Pipes;
using System.Text.Json;
namespace HomeVpn.Infrastructure;

public sealed record SetupRequest(string Operation, string? Configuration = null, string? DisplayName = null, string[]? Routes = null, Guid? ProfileId = null, HomeVpn.Models.SplitDnsSettings? SplitDns = null);
public sealed record SetupResponse(bool Success, HomeVpn.Models.VpnProfile? Profile = null, TunnelTestResult? Test = null, string? Error = null);
public static class SetupProtocol
{
    public static async Task SendAsync<T>(PipeStream pipe, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 131072) throw new InvalidDataException("Setup request too large.");
        try
        {
            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
            await pipe.WriteAsync(bytes, token); await pipe.FlushAsync(token);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    public static async Task<T> ReceiveAsync<T>(PipeStream pipe, CancellationToken token)
    {
        byte[] size = new byte[4]; await pipe.ReadExactlyAsync(size, token);
        int length = BitConverter.ToInt32(size);
        if (length < 1 || length > 131072) throw new InvalidDataException("Invalid setup frame.");
        byte[] bytes = new byte[length];
        try { await pipe.ReadExactlyAsync(bytes, token); return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Invalid setup request."); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
