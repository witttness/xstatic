namespace Extatic.Api.Services;

public class NotFoundException(string message) : Exception(message);
public class ForbiddenException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);
public class FileTooLargeException(string message) : Exception(message);
public class BadRequestException(string message) : Exception(message);
public class UnauthorizedException(string message) : Exception(message);

/// <summary>429. <paramref name="retryAfter"/> (when set) is emitted as a Retry-After header.</summary>
public class TooManyRequestsException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public class ValidationException(string message, IEnumerable<ValidationError> errors)
    : Exception(message)
{
    public IEnumerable<ValidationError> Errors { get; } = errors;
}

public record ValidationError(string Pointer, string Message);
