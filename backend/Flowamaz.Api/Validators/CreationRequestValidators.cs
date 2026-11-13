using Flowamaz.Api.Controllers;
using FluentValidation;

namespace Flowamaz.Api.Validators;

/// <summary>Validates the image posted to POST /workflows/visual/preview (same limits as POST /create).</summary>
public sealed class VisualPreviewRequestValidator : AbstractValidator<VisualPreviewRequest>
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimeTypes = ["image/jpeg", "image/png", "image/heic", "application/pdf"];

    public VisualPreviewRequestValidator()
    {
        RuleFor(x => x.ImageBase64)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Provide an imageBase64 value — upload a photo or diagram to analyse.")
            .Must(b => DecodedLength(b) <= MaxImageBytes).WithMessage("Image too large. Maximum is 10MB. Resize the image and try again.")
            .Must(BeBase64).WithMessage("imageBase64 is not valid base64. Re-encode the image and try again.");

        RuleFor(x => x.ImageMimeType)
            .Must(m => m is null || AllowedMimeTypes.Contains(m.ToLowerInvariant()))
            .WithMessage(x => $"Unsupported image type '{x.ImageMimeType}'. Use JPG, PNG, HEIC, or PDF.");
    }

    private static bool BeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true; // NotEmpty reports this case
        var buffer = new byte[DecodedLength(value) + 3];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    private static long DecodedLength(string? value) =>
        string.IsNullOrEmpty(value) ? 0 : (long)value.Length * 3 / 4;
}

/// <summary>Validates POST /workflows/conversation/preview (and the conversation text of POST /create).</summary>
public sealed class ConversationPreviewRequestValidator : AbstractValidator<ConversationPreviewRequest>
{
    /// <summary>About 50k tokens: a long thread fits, a whole mailbox export does not.</summary>
    public const int MaxTextLength = 200_000;
    internal static readonly string[] Sources = ["slack", "teams", "email", "generic"];

    public ConversationPreviewRequestValidator()
    {
        RuleFor(x => x.Text)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Paste a Slack thread, Teams transcript, email chain or meeting notes to extract a workflow from.")
            .MaximumLength(MaxTextLength).WithMessage($"The conversation is too long ({MaxTextLength:N0} characters at most). Paste only the part where the process is discussed.");

        RuleFor(x => x.SourceType)
            .Must(s => s is null || Sources.Contains(s.ToLowerInvariant()))
            .WithMessage(x => $"Unknown source type '{x.SourceType}'. Use slack, teams, email, or generic.");
    }
}
