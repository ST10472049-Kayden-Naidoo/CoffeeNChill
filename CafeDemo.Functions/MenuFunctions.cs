using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using CafeDemo.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CafeDemo.Functions.Functions;

public class MenuFunctions
{
    private const string TableName = "Menultems";
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<MenuFunctions> _logger;

    public MenuFunctions(TableServiceClient tableServiceClient, ILogger<MenuFunctions> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }

    private async Task<TableClient> GetTableClientAsync()
    {
        var table = _tableServiceClient.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync();
        return table;
    }

    private HttpResponseData JsonResponse(HttpRequestData req, object obj, HttpStatusCode status)
    {
        var res = req.CreateResponse(status);
        res.Headers.Add("Content-Type", "application/json");
        res.WriteString(JsonSerializer.Serialize(obj));
        return res;
    }

    [Function("CreateMenultem")]
    public async Task<HttpResponseData> CreateMenultem([HttpTrigger(AuthorizationLevel.Function, "post", Route = "menu")] HttpRequestData req)
    {
        CreateMenuItemDto? dto;
        try { dto = await JsonSerializer.DeserializeAsync<CreateMenuItemDto>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return JsonResponse(req, new { error = "Invalid JSON" }, HttpStatusCode.BadRequest); }

        if (dto == null) return JsonResponse(req, new { error = "Body required" }, HttpStatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(dto.Category) || string.IsNullOrWhiteSpace(dto.Sku) || string.IsNullOrWhiteSpace(dto.Name))
            return JsonResponse(req, new { error = "Category, Sku, Name required" }, HttpStatusCode.BadRequest);

        var entity = new MenuItemEntity { PartitionKey = dto.Category.Trim(), RowKey = dto.Sku.Trim(), Name = dto.Name.Trim(),
            Description = dto.Description ?? string.Empty, Price = dto.Price, IsAvailable = dto.IsAvailable };

        var table = await GetTableClientAsync();
        try { await table.AddEntityAsync(entity); }
        catch (RequestFailedException ex) when (ex.Status == 409) { return JsonResponse(req, new { error = "Exists" }, HttpStatusCode.Conflict); }

        var res = JsonResponse(req, entity, HttpStatusCode.Created);
        res.Headers.Add("Location", $"/api/menu/{Uri.EscapeDataString(entity.PartitionKey)}/{Uri.EscapeDataString(entity.RowKey)}");
        return res;
    }

    [Function("GetAllMenultems")]
    public async Task<HttpResponseData> GetAllMenultems([HttpTrigger(AuthorizationLevel.Function, "get", Route = "menu")] HttpRequestData req)
    {
        var table = await GetTableClientAsync();
        var list = new List<MenuItemEntity>();
        await foreach (var it in table.QueryAsync<MenuItemEntity>()) list.Add(it);
        return JsonResponse(req, list, HttpStatusCode.OK);
    }

    [Function("GetMenultemsByCategory")]
    public async Task<HttpResponseData> GetMenultemsByCategory([HttpTrigger(AuthorizationLevel.Function, "get", Route = "menu/category/{category}")] HttpRequestData req, string category)
    {
        var table = await GetTableClientAsync();
        var list = new List<MenuItemEntity>();
        await foreach (var it in table.QueryAsync<MenuItemEntity>(x => x.PartitionKey == category)) list.Add(it);
        return JsonResponse(req, list, HttpStatusCode.OK);
    }

    [Function("UpdateMenultem")]
    public async Task<HttpResponseData> UpdateMenultem([HttpTrigger(AuthorizationLevel.Function, "put", Route = "menu/{category}/{id}")] HttpRequestData req, string category, string id)
    {
        var table = await GetTableClientAsync();
        MenuItemEntity existing;
        try { existing = (await table.GetEntityAsync<MenuItemEntity>(category, id)).Value; }
        catch (RequestFailedException ex) when (ex.Status == 404) { return JsonResponse(req, new { error = "Not found" }, HttpStatusCode.NotFound); }

        UpdateMenuItemDto? dto;
        try { dto = await JsonSerializer.DeserializeAsync<UpdateMenuItemDto>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return JsonResponse(req, new { error = "Invalid JSON" }, HttpStatusCode.BadRequest); }

        if (dto?.Price != null) existing.Price = dto.Price.Value;
        if (dto?.IsAvailable != null) existing.IsAvailable = dto.IsAvailable.Value;
        if (!string.IsNullOrWhiteSpace(dto?.Description)) existing.Description = dto.Description;

        await table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace);
        return JsonResponse(req, existing, HttpStatusCode.OK);
    }

    [Function("DeleteMenultem")]
    public async Task<HttpResponseData> DeleteMenultem([HttpTrigger(AuthorizationLevel.Function, "delete", Route = "menu/{category}/{id}")] HttpRequestData req, string category, string id)
    {
        var table = await GetTableClientAsync();
        try { await table.DeleteEntityAsync(category, id); }
        catch (RequestFailedException ex) when (ex.Status == 404) { return JsonResponse(req, new { error = "Not found" }, HttpStatusCode.NotFound); }
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
