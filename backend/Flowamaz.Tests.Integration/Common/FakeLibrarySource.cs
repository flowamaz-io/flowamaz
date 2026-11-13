using Flowamaz.Core.Interfaces.Library;

namespace Flowamaz.Tests.Integration.Common;

/// <summary>Library repositories for tests: a snapshot per source, or an error to make the fetch fail.</summary>
public sealed class FakeLibrarySource : ILibrarySource
{
    private readonly Dictionary<string, LibrarySnapshot> _snapshots = new();
    private readonly Dictionary<string, Exception> _failures = new();

    public bool IsConfigured(string source) => _snapshots.ContainsKey(source) || _failures.ContainsKey(source);

    public Task<LibrarySnapshot> FetchAsync(string source, CancellationToken ct = default)
    {
        lock (_snapshots)
        {
            if (_failures.TryGetValue(source, out var ex)) throw ex;
            return Task.FromResult(_snapshots[source]);
        }
    }

    public void Set(string source, string sha, params LibrarySourceItem[] items)
    {
        lock (_snapshots)
        {
            _failures.Remove(source);
            _snapshots[source] = new LibrarySnapshot($"flowamaz-io/{source}", sha, items);
        }
    }

    public void Fail(string source, string message)
    {
        lock (_snapshots) _failures[source] = new HttpRequestException(message);
    }

    public void Clear()
    {
        lock (_snapshots)
        {
            _snapshots.Clear();
            _failures.Clear();
        }
    }

    public static LibrarySourceItem Folder(string path, params (string Name, string Content)[] files) =>
        new(path, files.ToDictionary(f => f.Name, f => f.Content));
}
