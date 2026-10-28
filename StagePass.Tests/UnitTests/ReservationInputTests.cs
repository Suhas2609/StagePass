using System.ComponentModel.DataAnnotations;
using StagePass.Api.DTOs.Reservations;

namespace StagePass.Tests.UnitTests;

public class ReservationInputTests
{
    private static CreateReservationRequest Valid() => new()
    {
        TicketTypeId = 1,
        Quantity = 2,
        IdempotencyKey = Guid.NewGuid(),
        CustomerIdentifier = "customer@example.com"
    };

    private static bool IsValid(CreateReservationRequest request)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
    }

    [Fact]
    public void CreateReservationRequest_WithQuantityZero_IsInvalid()
    {
        var r = Valid(); r.Quantity = 0;
        Assert.False(IsValid(r));
    }

    [Fact]
    public void CreateReservationRequest_WithNegativeQuantity_IsInvalid()
    {
        var r = Valid(); r.Quantity = -1;
        Assert.False(IsValid(r));
    }

    [Fact]
    public void CreateReservationRequest_WithQuantityEleven_IsInvalid()
    {
        var r = Valid(); r.Quantity = 11;
        Assert.False(IsValid(r));
    }

    [Fact]
    public void CreateReservationRequest_WithEmptyCustomerIdentifier_IsInvalid()
    {
        var r = Valid(); r.CustomerIdentifier = "";
        Assert.False(IsValid(r));
    }

    [Fact]
    public void CreateReservationRequest_WithEmptyGuidIdempotencyKey_IsInvalid()
    {
        // Guid.Empty passes [Required] on its own, so the DTO must reject it explicitly.
        var r = Valid(); r.IdempotencyKey = Guid.Empty;
        Assert.False(IsValid(r));
    }

    [Fact]
    public void CreateReservationRequest_WithValidData_IsValid()
    {
        Assert.True(IsValid(Valid()));
    }
}
