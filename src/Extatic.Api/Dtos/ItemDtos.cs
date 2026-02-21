using System.Text.Json;

namespace Extatic.Api.Dtos;

public record ItemDto(
    Guid Id,
    Guid CollectionId,
    Guid? AppUserId,
    JsonElement Data,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<AttachmentDto>? Attachments = null);

public record CreateItemRequest(JsonElement Data);
public record UpdateItemRequest(JsonElement Data);

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
