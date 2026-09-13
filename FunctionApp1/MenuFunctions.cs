using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Elasticsearch.Net;
using FunctionApp1.Models;
using FunctionApp1.Models.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nest;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using ResponseBase = FunctionApp1.Models.ResponseBase;

namespace FunctionApp1;

public class MenuFunctions
{
    private const string TableName = "Menultems";
    private readonly TableServiceClient _tableServiceClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<MenuFunctions> _logger;

    private const string _documentTableName = "Document";
    private const string _blobContainerName = "documents";

    public MenuFunctions(TableServiceClient tableServiceClient, ILogger<MenuFunctions> logger, BlobServiceClient blobServiceClient)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
        _blobServiceClient = blobServiceClient;

        _tableServiceClient.CreateTableIfNotExists(_documentTableName);
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

        var entity = new MenuItemModels
        {
            PartitionKey = dto.Category.Trim(),
            RowKey = dto.Sku.Trim(),
            Name = dto.Name.Trim(),
            Description = dto.Description ?? string.Empty,
            Price = dto.Price,
            IsAvailable = dto.IsAvailable
        };

        var table = await GetTableClientAsync();
        try { await table.AddEntityAsync(entity); }
        catch (RequestFailedException ex) when (ex.Status == 409) { return JsonResponse(req, new { error = "Exists" }, HttpStatusCode.Conflict); }

        var res = JsonResponse(req, entity, HttpStatusCode.Created);
        res.Headers.Add("Location", $"/api/menu/{Uri.EscapeDataString(entity.PartitionKey)}/{Uri.EscapeDataString(entity.RowKey)}");
        return res;
    }

    [Function("GetAllMenultems")]
    public async Task<HttpResponseData> GetAllMenultems([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        var table = await GetTableClientAsync();
        var list = new List<MenuItemModels>();
        await foreach (var it in table.QueryAsync<MenuItemModels>()) list.Add(it);
        return JsonResponse(req, list, HttpStatusCode.OK);
    }

    [Function("GetMenultemsByCategory")]
    public async Task<HttpResponseData> GetMenultemsByCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequestData req, string category)
    {
        var table = await GetTableClientAsync();
        var list = new List<MenuItemModels>();
        await foreach (var it in table.QueryAsync<MenuItemModels>(x => x.PartitionKey == category)) list.Add(it);
        return JsonResponse(req, list, HttpStatusCode.OK);
    }

    [Function("UpdateMenultem")]
    public async Task<HttpResponseData> UpdateMenultem([HttpTrigger(AuthorizationLevel.Function, "put", Route = "menu/{category}/{id}")] HttpRequestData req, string category, string id)
    {
        var table = await GetTableClientAsync();
        MenuItemModels existing;
        try { existing = (await table.GetEntityAsync<MenuItemModels>(category, id)).Value; }
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

    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> UploadStaffDocument([HttpTrigger(AuthorizationLevel.Function, "post", Route = "documents/upload")] HttpRequest req, HttpRequestData request)
    {
        _logger.LogInformation("Calling UploadStaffDocument");

        try
        {
            if (!req.HasFormContentType)
            {
                return JsonResponse(request, new { error = "Invalid content type. Expected multipart/form-data." }, HttpStatusCode.BadRequest);
            }

            var formCollection = await req.ReadFormAsync();
            var file = formCollection.Files["file"];

            if (file == null || file.Length == 0)
            {
                return JsonResponse(request, new { error = "No file found in the request or file is empty." }, HttpStatusCode.BadRequest);
            }

            if (file.Length > 20 * 1024 * 1024)
            {
                return JsonResponse(request, new { error = "Max 20MB allowed" }, HttpStatusCode.BadRequest);
            }

            var allowed = new[] { ".jpg", ".png", ".pdf", ".docx", ".txt", ".rtf" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowed.Contains(ext))
            {
                return JsonResponse(request, new { error = "Invalid file type: Only " + string.Join(", ", allowed.Select(n => n)) + " files allowed" }, HttpStatusCode.BadRequest);
            }

            var id = Guid.NewGuid().ToString();
            var blobName = id + ext;
            var fileUrl = await SaveFileToBlobStorageAsync(file, blobName);

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(file.FileName, out string contentType))
            {
                contentType = "application/octet-stream";
            }

            var document = new Document()
            {
                PartitionKey = _documentTableName,
                RowKey = id,
                Id = id,
                DocumentName = file.FileName,
                FileUrl = fileUrl,
                BlobName = blobName
            };

            var documentTableClient = _tableServiceClient.GetTableClient(_documentTableName);
            await documentTableClient.AddEntityAsync(document);

            _logger.LogInformation($"Document uploaded successfully: {document.DocumentName}");

            var response = new ResponseBase
            {
                Success = true,
                Message = "Document uploaded successfully",
                Data = DocumentDto.ToDto(document)
            };

            return JsonResponse(request, response, HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            return JsonResponse(request, new { error = "Internal server error" }, HttpStatusCode.InternalServerError);
        }
    }

    [Function("ListStaffDocuments")]
    public async Task<HttpResponseData> ListStaffDocuments([HttpTrigger(AuthorizationLevel.Function, "get", Route = "documents")] HttpRequestData req)
    {
        _logger.LogInformation("Calling ListStaffDocuments");
        try
        {
            var documentTableClient = _tableServiceClient.GetTableClient(_documentTableName);
            var documents = await documentTableClient.QueryAsync<Document>().ToListAsync();
            var documentDtos = documents.Select(x => DocumentDto.ToDto(x)).ToList();

            var data = new { Count = documentDtos.Count, Documents = documentDtos };
            var response = new ResponseBase
            {
                Success = true,
                Message = "All documents retrieved",
                Data = data
            };

            return JsonResponse(req, response, HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents");
            return JsonResponse(req, new { error = "Internal server error" }, HttpStatusCode.InternalServerError);
        }
    }

    [Function("DownloadStaffDocument")]
    public async Task<HttpResponseData> DownloadStaffDocument([HttpTrigger(AuthorizationLevel.Function, "get", Route = "documents/download/{blobName}")] HttpRequestData req, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var download = await blobClient.DownloadAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/octet-stream");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{blobName}\"");

            await download.Value.Content.CopyToAsync(response.Body);

            _logger.LogInformation($"Document downloaded: {blobName}");
            return response;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning($"Document not found: {blobName}");
            return JsonResponse(req, new { error = "Document not found" }, HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading document");
            return JsonResponse(req, new { error = "Internal server error" }, HttpStatusCode.InternalServerError);
        }
    }

    private async Task<string?> SaveFileToBlobStorageAsync(IFormFile file, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(blobName);
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving file to blob storage");
            throw;
        }
    }
}

//The IIE (2026) - Azure Functions Worker SDK documentation