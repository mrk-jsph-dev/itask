using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ITask.Api.Common.Security;

/// <summary>Creates opaque invite tokens and the SHA-256 hashes stored in their place.</summary>
public static class InviteTokens
{
    public static string Create()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
