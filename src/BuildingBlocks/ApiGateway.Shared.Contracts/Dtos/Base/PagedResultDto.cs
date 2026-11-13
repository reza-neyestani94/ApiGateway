namespace ApiGateway.Shared.Contracts.Dtos.Base;

public sealed class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
    public int FromItem { get; set; }
    public int ToItem { get; set; }

    public static PagedResultDto<T> Create(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalItems)
    {
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var fromItem = totalItems == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;
        var toItem = totalItems == 0 ? 0 : Math.Min(pageNumber * pageSize, totalItems);

        return new PagedResultDto<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasPreviousPage = pageNumber > 1,
            HasNextPage = pageNumber < totalPages,
            FromItem = fromItem,
            ToItem = toItem
        };
    }
}