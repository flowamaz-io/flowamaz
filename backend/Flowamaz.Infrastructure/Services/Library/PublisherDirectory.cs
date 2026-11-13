using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Library;

public sealed class PublisherDirectory(FlowAmazDbContext db, ILogger<PublisherDirectory> log) : IPublisherDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> DisplayNamesAsync(IReadOnlyCollection<string> publisherIds, CancellationToken ct = default)
    {
        log.LogInformation("PublisherDirectory.DisplayNamesAsync entry ids={Count}", publisherIds.Count);
        if (publisherIds.Count == 0) return new Dictionary<string, string>();
        var names = await db.PublisherProfiles.AsNoTracking()
            .Where(p => publisherIds.Contains(p.PublisherId) && p.DisplayName != "")
            .ToDictionaryAsync(p => p.PublisherId, p => p.DisplayName, ct);
        log.LogInformation("PublisherDirectory.DisplayNamesAsync exit found={Count}", names.Count);
        return names;
    }
}
