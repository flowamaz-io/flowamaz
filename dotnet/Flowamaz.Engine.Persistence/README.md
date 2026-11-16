# Flowamaz.Engine.Persistence

Storage for the Flowamaz workflow runtime (MIT), on EF Core and PostgreSQL.

- **`EngineDbContext`**: the 7 run entities (definitions, versions, instances, node states, variables, the event log, gate decisions) and the conventions every Flowamaz table follows: snake_case names, a soft-delete query filter on every `BaseEntity`, and CreatedAt/UpdatedAt (and CreatedBy/UpdatedBy) stamped on save.
- **Repositories** for each `I…Repository` contract in `Flowamaz.Engine.Runtime`, and `EfUnitOfWork` (`IUnitOfWork`). Workspace-scoped reads always filter on `workspace_id`.
- **AI usage:** `EfAiUsageMeter` writes every AI call to `engine_ai_usage` (registered by `AddFlowamazEnginePersistence`). The Flowamaz product meters into its own table and leaves `engine_ai_usage` out of its model.
- **Migrations** for `EngineDbContext` (`Migrations/`), for a standalone host. Apply them with `FLOWAMAZ_ENGINE_DB="Host=…;Database=…;Username=…;Password=…" dotnet ef database update --project Flowamaz.Engine.Persistence --context EngineDbContext`, or call `Database.MigrateAsync()` at startup. Use `EngineDbContextOptions.ConfigureNpgsql` when you register the context.

Register it in a host with one call (it adds the context, the 7 repositories and the unit of work):

```csharp
services.AddFlowamazEnginePersistence(o => o.UseNpgsql(connectionString, EngineDbContextOptions.ConfigureNpgsql));
```

**Inside a larger application:** derive your context from `EngineDbContext`. Override `ConfigureHostModel` to add your entities (the conventions run after it, so they apply to yours too), `CurrentUserId` to stamp the signed-in user, and `OnSaving` for entities that aren't a `BaseEntity`. Register `EngineDbContext` as your context (`AddScoped<EngineDbContext>(sp => sp.GetRequiredService<YourContext>())`) and call `AddFlowamazEngineRepositories()`, so the repositories share its scope and transactions. Your context keeps its own migrations, covering the engine tables too; never run the engine's migrations against that database. The Flowamaz product does this, and a test checks that both sets of migrations produce the same 7 tables.

The worker claim (`GetPendingForWorkerAsync`) and leases use PostgreSQL SQL (`FOR UPDATE SKIP LOCKED`, a data-modifying CTE), so several workers on several machines can share one database. With EF Core's in-memory provider they fall back to plain queries, done one at a time per process: fine for tests and trying the engine out in one process, not for several workers. Other relational providers (SQL Server, SQLite) aren't supported yet.

Moved code keeps its original namespaces (`Flowamaz.Infrastructure.Persistence.*`).
