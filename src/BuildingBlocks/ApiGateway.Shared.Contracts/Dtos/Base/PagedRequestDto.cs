namespace ApiGateway.Shared.Contracts.Dtos.Base;

public abstract class PagedRequestDto
{
    private const int MaxPageSize = 100;

    private int _pageNumber = 1;
    private int _pageSize = 10;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : Math.Min(value, MaxPageSize);
    }

    public string? SortBy { get; set; }

    public SortDirectionDto SortDirection { get; set; } = SortDirectionDto.Desc;
}
