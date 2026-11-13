namespace Flowamaz.Application.Library;

/// <summary>An organisation publishes to the Library under its slug (the first part of a pin, acme/x@1).</summary>
public static class PublisherIds
{
    public static string ForOrg(string? orgSlug, Guid orgId) =>
        orgSlug is { Length: > 0 } slug ? slug : $"org-{orgId.ToString("N")[..8]}";
}
