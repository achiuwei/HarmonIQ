namespace HarmonIQ.Api.Persistence;

/// <summary>
/// Filesystem-backed (locally) blob store for report bodies. Key convention (fixed,
/// used verbatim by the analysis pipeline and API tasks):
/// "reports/{engineVersion}/{subjectId}/{principleSet}.json.gz" — bodies are gzipped
/// UTF-8 JSON.
/// </summary>
public interface IObjectStore
{
    Task<string> PutAsync(string key, ReadOnlyMemory<byte> body, CancellationToken ct);
    Task<byte[]?> GetAsync(string key, CancellationToken ct);
    string UriFor(string key);
}
