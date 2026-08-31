namespace StickyNotes.Services;

/// <summary>Criptografia do conteúdo das notas (DPAPI, escopo do usuário atual).</summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string? cipherText);
}
