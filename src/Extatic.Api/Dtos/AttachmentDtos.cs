namespace Extatic.Api.Dtos;

public record AttachmentDto(
    Guid Id,
    Guid ItemId,
    Guid? AppUserId,
    string Filename,
    string ContentType,
    long SizeBytes,
    string Url,
    DateTime CreatedAt);
