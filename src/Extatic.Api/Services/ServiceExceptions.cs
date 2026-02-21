namespace Extatic.Api.Services;

public class NotFoundException(string message) : Exception(message);
public class ForbiddenException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);
public class FileTooLargeException(string message) : Exception(message);

public class ValidationException(string message, IEnumerable<ValidationError> errors)
    : Exception(message)
{
    public IEnumerable<ValidationError> Errors { get; } = errors;
}

public record ValidationError(string Pointer, string Message);
