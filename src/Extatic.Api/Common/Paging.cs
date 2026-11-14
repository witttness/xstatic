using Extatic.Api.Services;

namespace Extatic.Api.Common;

public static class Paging
{
    public const int MaxPageSize = 100;

    /// <summary>Same rule as the existing item list endpoints. Throws <see cref="BadRequestException"/> (400).</summary>
    public static void Validate(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize || page - 1 > int.MaxValue / pageSize)
            throw new BadRequestException("page must be at least 1 and page_size must be between 1 and 100");
    }

    /// <summary>Rows to skip for a validated page.</summary>
    public static int Skip(int page, int pageSize) => (page - 1) * pageSize;
}
