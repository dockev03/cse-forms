using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CseForms.Application.Pdf;
using Microsoft.Extensions.Options;

namespace CseForms.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Dossier des fichiers, hors de la racine web (volume Docker en production).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Clé AES-256 encodée en base64 (32 octets), fournie par variable d'environnement ou secret Docker.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    public static bool IsValidKey(string? key)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(key) && Convert.FromBase64String(key).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Fichiers chiffrés en AES-256-GCM. Format : <c>CSF1</c>, nonce (12 octets), tag (16 octets), contenu chiffré.
/// L'identifiant du fichier est authentifié avec le contenu : un fichier échangé avec un autre est refusé.
/// </summary>
internal sealed partial class EncryptedFileVault : IFileVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "CSF1"u8.ToArray();

    private readonly string _root;
    private readonly byte[] _key;

    public EncryptedFileVault(IOptions<StorageOptions> options)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.Path) || !StorageOptions.IsValidKey(value.EncryptionKey))
        {
            throw new InvalidOperationException("Stockage mal configuré : Storage__Path et Storage__EncryptionKey (32 octets en base64) sont obligatoires.");
        }

        _root = System.IO.Path.GetFullPath(value.Path);
        _key = Convert.FromBase64String(value.EncryptionKey);
    }

    public async Task<StoredFile> WriteAsync(Guid fileId, Guid submissionId, byte[] content, CancellationToken cancellationToken)
    {
        var relative = $"{submissionId:N}/{fileId:N}.bin";
        var path = FullPath(relative);
        var directory = System.IO.Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[content.Length];
        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, content, cipher, tag, fileId.ToByteArray());
        }

        // Écriture dans un fichier temporaire puis renommage : jamais de fichier à moitié écrit.
        var temporary = $"{path}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(temporary, options))
        {
            await stream.WriteAsync(Magic, cancellationToken);
            await stream.WriteAsync(nonce, cancellationToken);
            await stream.WriteAsync(tag, cancellationToken);
            await stream.WriteAsync(cipher, cancellationToken);
        }

        File.Move(temporary, path);

        return new StoredFile(relative, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length);
    }

    public async Task<byte[]> ReadAsync(string storagePath, Guid fileId, CancellationToken cancellationToken)
    {
        var data = await File.ReadAllBytesAsync(FullPath(storagePath), cancellationToken);
        var header = Magic.Length + NonceSize + TagSize;
        if (data.Length < header || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("Format de fichier inconnu.");
        }

        var plain = new byte[data.Length - header];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            data.AsSpan(Magic.Length, NonceSize),
            data.AsSpan(header),
            data.AsSpan(Magic.Length + NonceSize, TagSize),
            plain,
            fileId.ToByteArray());
        return plain;
    }

    public void Delete(string storagePath)
    {
        var path = FullPath(storagePath);
        File.Delete(path);

        var directory = System.IO.Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>Seuls les chemins produits par ce stockage sont acceptés : aucune sortie du dossier possible.</summary>
    private string FullPath(string relative)
    {
        if (!RelativePathPattern().IsMatch(relative))
        {
            throw new ArgumentException("Chemin de stockage invalide.", nameof(relative));
        }

        return System.IO.Path.Combine(_root, relative);
    }

    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}\\.bin$")]
    private static partial Regex RelativePathPattern();
}
