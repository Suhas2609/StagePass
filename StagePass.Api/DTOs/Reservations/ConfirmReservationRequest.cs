using System.ComponentModel.DataAnnotations;

namespace StagePass.Api.DTOs.Reservations;

public class ConfirmReservationRequest
{
    [Required]
    public string CustomerIdentifier { get; set; } = string.Empty;
}
