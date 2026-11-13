namespace Flowamaz.Core.Interfaces.Library;

/// <summary>Display names of Library publishers, for "by …" lines (a publisher's profile name wins over the id).</summary>
public interface IPublisherDirectory
{
    /// <summary>Profile display names by publisher id; ids without a profile are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> DisplayNamesAsync(IReadOnlyCollection<string> publisherIds, CancellationToken ct = default);
}
