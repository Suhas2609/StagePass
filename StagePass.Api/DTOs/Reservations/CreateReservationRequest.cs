using System.ComponentModel.DataAnnotations;

namespace StagePass.Api.DTOs.Reservations;

public class CreateReservationRequest : IValidatableObject
{
    [Required]
    public int TicketTypeId { get; set; }

    [Required]
    [Range(1, 10)]
    public int Quantity { get; set; }

    [Required]
    public Guid IdempotencyKey { get; set; }

    [Required]
    [MaxLength(200)]
    public string CustomerIdentifier { get; set; } = string.Empty;

    // [Required] does not reject Guid.Empty (it is a non-null value type), so check explicitly.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IdempotencyKey == Guid.Empty)
        {
            yield return new ValidationResult(
                "IdempotencyKey must not be an empty GUID.",
                new[] { nameof(IdempotencyKey) });
        }
    }
}
