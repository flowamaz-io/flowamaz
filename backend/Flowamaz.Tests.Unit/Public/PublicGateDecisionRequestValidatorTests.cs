using FluentAssertions;
using Flowamaz.Api.Controllers.Public;
using Flowamaz.Api.Validators;

namespace Flowamaz.Tests.Unit.Public;

/// <summary>
/// Public gate decisions: only "approved"/"rejected" are accepted (a typo must not reject a loan), and
/// the caller text in note / decided_by is bounded and single-line.
/// </summary>
public class PublicGateDecisionRequestValidatorTests
{
    private readonly PublicGateDecisionRequestValidator _validator = new();

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("Approved")]
    [InlineData(" rejected ")]
    public void Accepts_approved_and_rejected(string decision) =>
        _validator.Validate(new PublicGateDecisionRequest(decision, null)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aproved")]
    [InlineData("yes")]
    public void Rejects_a_missing_or_unknown_decision(string? decision)
    {
        var result = _validator.Validate(new PublicGateDecisionRequest(decision, null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("\"approved\" or \"rejected\""));
    }

    [Fact]
    public void Accepts_a_decided_by_name() =>
        _validator.Validate(new PublicGateDecisionRequest("approved", "ok", "Aisha Rahman (Branch Manager)"))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Rejects_a_decided_by_longer_than_200_characters() =>
        _validator.Validate(new PublicGateDecisionRequest("approved", null, new string('a', 201)))
            .Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("'decided_by' is too long"));

    [Fact]
    public void Rejects_a_decided_by_with_line_breaks() =>
        _validator.Validate(new PublicGateDecisionRequest("approved", null, "Aisha\nRahman"))
            .Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("control characters"));

    [Fact]
    public void Rejects_a_note_longer_than_2000_characters() =>
        _validator.Validate(new PublicGateDecisionRequest("rejected", new string('n', 2001)))
            .Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("'note' is too long"));
}
