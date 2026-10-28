namespace StagePass.Api.Services;

public static class ErrorCodes
{
    public const string InsufficientInventory = "InsufficientInventory";
    public const string HoldExpired = "HoldExpired";
    public const string AlreadyConfirmed = "AlreadyConfirmed";
    public const string IdempotencyKeyReused = "IdempotencyKeyReused";
    public const string PerformanceCancelled = "PerformanceCancelled";
    public const string HoldNotFound = "HoldNotFound";
    public const string InvalidOwner = "InvalidOwner";
}
