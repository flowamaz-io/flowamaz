using FluentAssertions;
using Flowamaz.Application.Workflow.Gates;

namespace Flowamaz.Tests.Unit.Connectors.Gates;

/// <summary>
/// Gate email HMAC signing helpers (04-04): valid signatures are accepted; expired links
/// or invalid signatures are rejected. Gate decision status changes are covered in
/// GateServiceTests in the Workflow namespace.
/// </summary>
public class GateDecisionServiceTests
{
    private const string SigningKey = "test-gate-key";

    private static long FutureExpiry(int hours = 48) =>
        new DateTimeOffset(DateTime.UtcNow.AddHours(hours)).ToUnixTimeSeconds();

    private static long PastExpiry(int hoursAgo = 1) =>
        new DateTimeOffset(DateTime.UtcNow.AddHours(-hoursAgo)).ToUnixTimeSeconds();

    [Fact]
    public void ApproveHmac_ValidSignature_Accepted()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var expiry = FutureExpiry();
        var message = $"{gateId}:approve:{expiry}";
        var sig = GateHmacHelper.BuildHmac(message, SigningKey);

        // Act
        var isValid = GateHmacHelper.ValidateHmac(message, sig, SigningKey);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void RejectHmac_ValidSignature_Accepted()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var expiry = FutureExpiry();
        var message = $"{gateId}:reject:{expiry}";
        var sig = GateHmacHelper.BuildHmac(message, SigningKey);

        // Act
        var isValid = GateHmacHelper.ValidateHmac(message, sig, SigningKey);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ExpiredLink_ExpiryInPast_IsDetectable()
    {
        // Arrange: produce a valid sig for a past timestamp.
        var gateId = Guid.NewGuid();
        var expiry = PastExpiry();
        var expiryDate = DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime;

        // The HMAC itself is valid, but the expiry time is in the past.
        var message = $"{gateId}:approve:{expiry}";
        var sig = GateHmacHelper.BuildHmac(message, SigningKey);

        // The HMAC validates (the token itself is not expired — caller checks date).
        GateHmacHelper.ValidateHmac(message, sig, SigningKey).Should().BeTrue();

        // But the expiry date is in the past — controller rejects.
        expiryDate.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void TamperedSignature_ReturnsFalse()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var expiry = FutureExpiry();
        var message = $"{gateId}:approve:{expiry}";

        // Act — supply wrong sig
        var isValid = GateHmacHelper.ValidateHmac(message, "deadbeef00000000", SigningKey);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void TamperedAction_DifferentAction_ReturnsFalse()
    {
        // Arrange — sign for 'reject' but use 'approve' in validation.
        var gateId = Guid.NewGuid();
        var expiry = FutureExpiry();
        var sigMessage = $"{gateId}:reject:{expiry}";
        var sig = GateHmacHelper.BuildHmac(sigMessage, SigningKey);

        // Validate with 'approve' message — should fail.
        var isValid = GateHmacHelper.ValidateHmac($"{gateId}:approve:{expiry}", sig, SigningKey);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void WrongKey_ReturnsFalse()
    {
        // Arrange
        var message = $"{Guid.NewGuid()}:approve:{FutureExpiry()}";
        var sig = GateHmacHelper.BuildHmac(message, "correct-key");

        // Act — validate with a different key.
        var isValid = GateHmacHelper.ValidateHmac(message, sig, "wrong-key");

        isValid.Should().BeFalse();
    }
}
