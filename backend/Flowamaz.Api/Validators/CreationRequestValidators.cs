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
