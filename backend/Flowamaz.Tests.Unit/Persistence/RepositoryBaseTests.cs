using FluentAssertions;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Tests.Unit.Persistence;

/// <summary>
/// Proves the soft-delete query filter is honoured by the read path. GetByIdAsync uses
/// FirstOrDefaultAsync (not FindAsync) precisely so a soft-deleted row is invisible — FindAsync
/// would hit the change tracker / key lookup and bypass the global filter.
/// </summary>
public class RepositoryBaseTests
{
    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"repo-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task GetByIdAsync_returns_entity_when_not_deleted()
    {
        await using var db = NewDb();
        var ws = new Workspace { OrgId = Guid.NewGuid(), Name = "Live", Slug = "live" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var repo = new RepositoryBase<Workspace>(db);
        (await repo.GetByIdAsync(ws.Id))!.Slug.Should().Be("live");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_soft_deleted_entity()
    {
        await using var db = NewDb();
        var ws = new Workspace { OrgId = Guid.NewGuid(), Name = "Gone", Slug = "gone" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        ws.IsDeleted = true;
        ws.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var repo = new RepositoryBase<Workspace>(db);
        (await repo.GetByIdAsync(ws.Id))
            .Should().BeNull("the global soft-delete query filter must exclude deleted rows");
    }
}
