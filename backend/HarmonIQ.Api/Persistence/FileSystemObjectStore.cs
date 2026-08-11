using Microsoft.Extensions.Configuration;

namespace HarmonIQ.Api.Persistence;

/// <summary>
/// Local filesystem stand-in for the designed object store seam. Roots at
/// HARMONIQ_OBJECT_STORE (default "./.harmoniq-local/store"), gitignored. Keys are
/// relative paths (e.g. "reports/{engineVersion}/{subjectId}/{principleSet}.json.gz");
/// callers are responsible for gzip-encoding bodies per the fixed key convention.
/// </summary>
public class FileSystemObjectStore : IObjectStore
{
    private readonly string _root;

    public FileSystemObjectStore(IConfiguration configuration)
    {
        _root = configuration["HARMONIQ_OBJECT_STORE"]
            ?? Environment.GetEnvironmentVariable("HARMONIQ_OBJECT_STORE")
            ?? Path.Combine(".harmoniq-local", "store");
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string key)
    {
        var safeKey = key.Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(_root, safeKey));
        var rootFullPath = Path.GetFullPath(_root);
        if (!fullPath.StartsWith(rootFullPath, StringComparison.Ordinal))
        {
            throw new ArgumentException("Object store key must not escape the store root.", nameof(key));
        }
        return fullPath;
    }

    public async Task<string> PutAsync(string key, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var path = PathFor(key);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(body, ct);
        return UriFor(key);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, ct);
    }

    public string UriFor(string key)
    {
        var safeKey = key.Replace('\\', '/');
        return $"file://{Path.GetFullPath(_root).Replace('\\', '/')}/{safeKey}";
    }
}
