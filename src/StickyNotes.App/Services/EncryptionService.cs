using System.Security.Cryptography;
using System.Text;

namespace StickyNotes.Services;

/// <summary>Criptografia do conteúdo das notas via DPAPI (escopo do usuário atual).</summary>
public class EncryptionService : IEncryptionService
{
    private static readonly byte[] Entropy = "StickyNotes:v1"u8.ToArray();

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        byte[] bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public string Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        byte[] bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(cipherText), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
