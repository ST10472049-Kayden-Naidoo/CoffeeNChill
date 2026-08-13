using Azure;
using Azure.Data.Tables;

namespace CafeDemo.Functions.Models;

public class MenuItemEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // Category
    public string RowKey { get; set; } = default!;       // SKU
    public string Name { get; set; } = default!;
    public string Description { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool IsAvailable { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

public record CreateMenuItemDto(string Category, string Sku, string Name, string? Description, double Price, bool IsAvailable);
public record UpdateMenuItemDto(double? Price, bool? IsAvailable, string? Description);
