using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace BlazorBlobExplorer.Services;

public class BlobItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = "";
    public string DisplayName => Path.Split('/').Last(s => !string.IsNullOrEmpty(s));
    public string SizeDisplay => IsFolder ? "" : FormatSize(Size);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public class UploadProgress
{
    public string FileName { get; set; } = "";
    public int Percent { get; set; }
    public bool IsComplete { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AzureBlobService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SasTokenService _sasTokenService;
    private readonly IConfiguration _config;

    public AzureBlobService(IHttpClientFactory factory, SasTokenService sasTokenService, IConfiguration config)
    {
        _httpClientFactory = factory;
        _sasTokenService = sasTokenService;
        _config = config;
    }

    private string AccountName => _config["AzureStorage:AccountName"]!;
    private string ContainerName => _config["AzureStorage:ContainerName"]!;

    /// <summary>
    /// Lists blobs and virtual folders at a given prefix path (ADLS Gen2 hierarchical).
    /// </summary>
    public async Task<List<BlobItem>> ListAsync(string prefix = "")
    {
        var items = new List<BlobItem>();
        string? continuationToken = null;

        do
        {
            var sasDomain = await _sasTokenService.GetContainerSasUrlAsync("rl");
            // Parse existing SAS and add list params
            var uriBuilder = new UriBuilder(sasDomain);
            var existingQuery = uriBuilder.Query.TrimStart('?');

            var listParams = $"restype=container&comp=list&delimiter=%2F&include=metadata";
            if (!string.IsNullOrEmpty(prefix))
                listParams += $"&prefix={Uri.EscapeDataString(prefix)}";
            if (!string.IsNullOrEmpty(continuationToken))
                listParams += $"&marker={Uri.EscapeDataString(continuationToken)}";

            var finalUrl = $"https://{AccountName}.blob.core.windows.net/{ContainerName}?{listParams}&{existingQuery}";

            var client = _httpClientFactory.CreateClient("AzureStorage");
            var request = new HttpRequestMessage(HttpMethod.Get, finalUrl);
            request.Headers.Add("x-ms-version", "2020-12-06");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
            var ns = xml.Root!.Name.Namespace;

            // Virtual directories (folders)
            foreach (var blobPrefix in xml.Descendants(ns + "BlobPrefix"))
            {
                var name = blobPrefix.Element(ns + "Name")?.Value ?? "";
                items.Add(new BlobItem
                {
                    Name = name,
                    Path = name,
                    IsFolder = true
                });
            }

            // Actual blobs
            foreach (var blob in xml.Descendants(ns + "Blob"))
            {
                var name = blob.Element(ns + "Name")?.Value ?? "";
                var props = blob.Element(ns + "Properties");
                var sizeStr = props?.Element(ns + "Content-Length")?.Value ?? "0";
                var lastModStr = props?.Element(ns + "Last-Modified")?.Value ?? "";
                var contentType = props?.Element(ns + "Content-Type")?.Value ?? "application/octet-stream";

                DateTime.TryParse(lastModStr, out var lastMod);
                long.TryParse(sizeStr, out var size);

                items.Add(new BlobItem
                {
                    Name = name,
                    Path = name,
                    IsFolder = false,
                    Size = size,
                    LastModified = lastMod,
                    ContentType = contentType
                });
            }

            continuationToken = xml.Descendants(ns + "NextMarker").FirstOrDefault()?.Value;

        } while (!string.IsNullOrEmpty(continuationToken));

        return items;
    }

    /// <summary>
    /// Uploads a file to the specified path in the container.
    /// Reports progress via the progress callback.
    /// </summary>
    public async Task<bool> UploadAsync(string blobPath, Stream content, string contentType,
                                         IProgress<int>? progress = null)
    {
        var sasUrl = await _sasTokenService.GetBlobSasUrlAsync(blobPath, "cw");

        var client = _httpClientFactory.CreateClient("AzureStorage");

        // Read into memory to get length (required for blob upload)
        var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var request = new HttpRequestMessage(HttpMethod.Put, sasUrl);
        request.Headers.Add("x-ms-version", "2020-12-06");
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        progress?.Report(50);
        var response = await client.SendAsync(request);
        progress?.Report(100);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Gets a short-lived SAS download URL for a blob.
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(string blobPath, int expiryMinutes = 30)
    {
        return await _sasTokenService.GetBlobSasUrlAsync(blobPath, "r", expiryMinutes);
    }

    /// <summary>
    /// Deletes a blob at the specified path.
    /// </summary>
    public async Task<bool> DeleteAsync(string blobPath)
    {
        var sasUrl = await _sasTokenService.GetBlobSasUrlAsync(blobPath, "d");

        var client = _httpClientFactory.CreateClient("AzureStorage");
        var request = new HttpRequestMessage(HttpMethod.Delete, sasUrl);
        request.Headers.Add("x-ms-version", "2020-12-06");

        var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Creates a virtual folder by uploading a zero-byte placeholder blob.
    /// In ADLS Gen2 hierarchical namespace, actual directories exist natively.
    /// </summary>
    public async Task<bool> CreateFolderAsync(string folderPath)
    {
        // Normalize: ensure trailing slash
        var path = folderPath.TrimEnd('/') + "/.keep";
        return await UploadAsync(path, new MemoryStream(Array.Empty<byte>()), "application/octet-stream");
    }
}
