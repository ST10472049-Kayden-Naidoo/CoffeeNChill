using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CafeDemo.Functions.Functions;

public class DocumentFunctions
{
    private const string ContainerName = "staff-docs";
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<DocumentFunctions> _logger;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain"
    };

    public DocumentFunctions(BlobServiceClient blobServiceClient, ILogger<DocumentFunctions> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    private async Task<BlobContainerClient> GetContainerClientAsync()
    {
        var c = _blobServiceClient.GetBlobContainerClient(ContainerName);
        await c.CreateIfNotExistsAsync();
        return c;
    }

    private HttpResponseData JsonResponse(HttpRequestData req, object obj, HttpStatusCode status)
    {
        var res = req.CreateResponse(status);
        res.Headers.Add("Content-Type", "application/json");
        res.WriteString(JsonSerializer.Serialize(obj));
        return res;
    }

    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> UploadStaffDocument([HttpTrigger(AuthorizationLevel.Function, "post", Route = "documents/upload")] HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Content-Type", out var cvals))
            return JsonResponse(req, new { error = "Missing Content-Type" }, HttpStatusCode.BadRequest);

        var contentType = cvals.First();
        MediaTypeHeaderValue mediaType;
        try
        {
            mediaType = MediaTypeHeaderValue.Parse(contentType);
        }
        catch
        {
            return JsonResponse(req, new { error = "Invalid Content-Type header." }, HttpStatusCode.BadRequest);
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return JsonResponse(req, new { error = "No boundary" }, HttpStatusCode.BadRequest);

        var reader = new MultipartReader(boundary, req.Body);
        MultipartSection? section;
        var container = await GetContainerClientAsync();

        while ((section = await reader.ReadNextSectionAsync()) != null)
        {
            // Parse content-disposition to find file parts
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disp))
                continue;

            // disp.FileName and FileNameStar are StringSegment; check HasValue
            var fileNameSegment = disp.FileNameStar.HasValue ? disp.FileNameStar : disp.FileName;
            if (fileNameSegment == null || !fileNameSegment.HasValue)
                continue;

            var fileName = fileNameSegment.Value.Trim('"');

            // Read Content-Type header from section.Headers (dictionary)
            string partContentType = "application/octet-stream";
            if (section.Headers != null && section.Headers.TryGetValue("Content-Type", out var headerValues))
                partContentType = headerValues.ToString();

            if (!AllowedContentTypes.Contains(partContentType))
                return JsonResponse(req, new { error = $"Content-Type '{partContentType}' not allowed." }, HttpStatusCode.UnsupportedMediaType);

            var blob = container.GetBlobClient(fileName);
            await blob.UploadAsync(section.Body, new BlobHttpHeaders { ContentType = partContentType });
            _logger.LogInformation("Uploaded file {FileName}", fileName);

            return JsonResponse(req, new { fileName, contentType = partContentType }, HttpStatusCode.OK);
        }

        return JsonResponse(req, new { error = "No file part" }, HttpStatusCode.BadRequest);
    }

    [Function("ListStaffDocuments")]
    public async Task<HttpResponseData> ListStaffDocuments([HttpTrigger(AuthorizationLevel.Function, "get", Route = "documents")] HttpRequestData req)
    {
        var container = await GetContainerClientAsync();
        var list = new List<object>();
        await foreach (var b in container.GetBlobsAsync())
            list.Add(new { fileName = b.Name, size = b.Properties.ContentLength, lastModified = b.Properties.LastModified, contentType = b.Properties.ContentType });
        return JsonResponse(req, list, HttpStatusCode.OK);
    }

    [Function("DownloadStaffDocument")]
    public async Task<HttpResponseData> DownloadStaffDocument([HttpTrigger(AuthorizationLevel.Function, "get", Route = "documents/download/{fileName}")] HttpRequestData req, string fileName)
    {
        var container = await GetContainerClientAsync();
        var blob = container.GetBlobClient(fileName);
        if (!await blob.ExistsAsync())
            return JsonResponse(req, new { error = "Not found" }, HttpStatusCode.NotFound);

        var dl = await blob.DownloadStreamingAsync();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", dl.Value.Details.ContentType ?? "application/octet-stream");
        resp.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        await dl.Value.Content.CopyToAsync(resp.Body);
        return resp;
    }
}
