namespace FcDraft.Application.Common.Exceptions;

public abstract class AppException(string message) : Exception(message);

public sealed class ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class UnauthorizedAppException(string message = "Invalid credentials.")
    : AppException(message);

public sealed class ForbiddenAppException(string message)
    : AppException(message);

public sealed class ConflictAppException(string message)
    : AppException(message);
