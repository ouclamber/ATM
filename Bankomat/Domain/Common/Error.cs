namespace Bankomat.Domain.Common;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string msg)  => new("VALIDATION", msg);
    public static Error NotFound(string msg)    => new("NOT_FOUND", msg);
    public static Error Conflict(string msg)    => new("CONFLICT", msg);
    public static Error Forbidden(string msg)   => new("FORBIDDEN", msg);
    public static Error Business(string msg)    => new("BUSINESS", msg);
}