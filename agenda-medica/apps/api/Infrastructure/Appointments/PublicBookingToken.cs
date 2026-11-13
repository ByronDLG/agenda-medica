using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public static class PublicBookingToken
{
    private const int TokenByteLength = 32;
    private const int EncodedTokenLength = 43;

    public static (string Token, byte[] Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteLength));
        return (token, Hash(token));
    }

    public static bool TryHash(string? token, out byte[] tokenHash)
    {
        tokenHash = [];
        if (token is null ||
            token.Length != EncodedTokenLength ||
            token.Any(character => !IsBase64UrlCharacter(character)))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = WebEncoders.Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length != TokenByteLength)
        {
            return false;
        }

        tokenHash = Hash(token);
        return true;
    }

    private static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool IsBase64UrlCharacter(char character) =>
        character is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-' or '_';
}
