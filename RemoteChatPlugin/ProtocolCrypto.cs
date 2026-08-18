using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteChatPlugin;

internal static class ProtocolCrypto
{
    private const string Context = "vpetllm-remote-chat/v1";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VPetLLM.RemoteChat.Secret.v1");

    internal static PairingMaterial CreatePairing()
    {
        var room = RandomNumberGenerator.GetBytes(16);
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            return new PairingMaterial(Base64Url(room), Protect(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    internal static byte[] UnprotectSecret(string protectedSecret)
    {
        var encrypted = Convert.FromBase64String(protectedSecret);
        return WindowsDataProtection.Unprotect(encrypted, Entropy);
    }

    internal static DerivedKeys DeriveKeys(byte[] secret, string roomId)
    {
        var salt = Encoding.UTF8.GetBytes(roomId);
        var encryption = Hkdf(secret, salt, Encoding.UTF8.GetBytes($"{Context}/encryption"));
        var authentication = Hkdf(secret, salt, Encoding.UTF8.GetBytes($"{Context}/authentication"));
        try
        {
            using var hmac = new HMACSHA256(authentication);
            var verifier = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{Context}/server-auth"));
            return new DerivedKeys(encryption, Base64Url(verifier));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authentication);
        }
    }

    internal static RelayEnvelope Encrypt(byte[] key, string roomId, object payload)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var messageId = Base64Url(RandomNumberGenerator.GetBytes(16));
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var aad = Encoding.UTF8.GetBytes($"{Context}|{roomId}|{messageId}");
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            var combined = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);
            return new RelayEnvelope
            {
                Type = "relay", Version = 1, RoomId = roomId, MessageId = messageId,
                Nonce = Base64Url(nonce), Ciphertext = Base64Url(combined)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static JsonDocument Decrypt(byte[] key, string roomId, RelayEnvelope envelope)
    {
        var nonce = FromBase64Url(envelope.Nonce);
        var combined = FromBase64Url(envelope.Ciphertext);
        if (nonce.Length != 12 || combined.Length < 17) throw new CryptographicException("Invalid encrypted envelope");
        var ciphertextLength = combined.Length - 16;
        var plaintext = new byte[ciphertextLength];
        var aad = Encoding.UTF8.GetBytes($"{Context}|{roomId}|{envelope.MessageId}");
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, combined.AsSpan(0, ciphertextLength), combined.AsSpan(ciphertextLength, 16), plaintext, aad);
        try
        {
            return JsonDocument.Parse(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static string ExportSecret(string protectedSecret)
    {
        var secret = UnprotectSecret(protectedSecret);
        try { return Base64Url(secret); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private static string Protect(byte[] secret) => Convert.ToBase64String(
        WindowsDataProtection.Protect(secret, Entropy));

    private static byte[] Hkdf(byte[] inputKey, byte[] salt, byte[] info)
    {
        using var extract = new HMACSHA256(salt);
        var prk = extract.ComputeHash(inputKey);
        try
        {
            using var expand = new HMACSHA256(prk);
            var block = new byte[info.Length + 1];
            Buffer.BlockCopy(info, 0, block, 0, info.Length);
            block[^1] = 1;
            return expand.ComputeHash(block);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }

    internal static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(normalized);
    }
}

internal sealed record PairingMaterial(string RoomId, string ProtectedSecret);
internal sealed record DerivedKeys(byte[] EncryptionKey, string Verifier) : IDisposable
{
    public void Dispose() => CryptographicOperations.ZeroMemory(EncryptionKey);
}

internal sealed class RelayEnvelope
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("v")] public int Version { get; set; }
    [JsonPropertyName("room_id")] public string RoomId { get; set; } = "";
    [JsonPropertyName("message_id")] public string MessageId { get; set; } = "";
    [JsonPropertyName("nonce")] public string Nonce { get; set; } = "";
    [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = "";
    [JsonPropertyName("peer_online")] public bool? PeerOnline { get; set; }
}

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false
    };
}
