using System.Text.RegularExpressions;
using Helpdesk.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Helpdesk.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Carpeta de adjuntos, fuera del webroot (en contenedor: volumen <c>/app/data/attachments</c>).</summary>
    public string AttachmentsPath { get; set; } = "data/attachments";
}

/// <summary>
/// Adjuntos en disco con nombre aleatorio (UUIDv4 sin extensión) repartidos en subcarpetas. El nombre original
/// nunca toca el sistema de archivos, así que no hay recorrido de rutas posible (OWASP File Upload).
/// </summary>
internal sealed partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.Value.AttachmentsPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true);
        await stream.WriteAsync(content, cancellationToken);
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = PathFor(storageKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("El archivo del adjunto no está disponible.");
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = PathFor(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string storageKey)
    {
        if (!KeyPattern().IsMatch(storageKey))
        {
            throw new ArgumentException("Clave de almacenamiento inválida.", nameof(storageKey));
        }

        return Path.Combine(_root, storageKey[..2], storageKey);
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
