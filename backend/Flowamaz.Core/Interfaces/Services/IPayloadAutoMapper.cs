using System.Text.Json;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

public interface IPayloadAutoMapper
{
    Task<AutoMapResult> MapAsync(JsonDocument sampleJson, JsonElement targetSchema, Guid workspaceId, CancellationToken ct);
}
