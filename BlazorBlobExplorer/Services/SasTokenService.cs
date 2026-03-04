using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace BlazorBlobExplorer.Services;

public class SasTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    private string? _delegationKeyValue;
    private string? _delegationKeySignedOid;
    private string? _delegationKeySignedTid;
    private DateTime _delegationKeyStart;
    private DateTime _delegationKeyExpiry;
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public SasTokenService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    private string AccountName => _config["AzureStorage:AccountName"]!;
    private string ContainerName => _config["AzureStorage:ContainerName"]!;

    private async Task<string> GetAccessTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-2))
            return _cachedToken;

        var http = _httpClientFactory.CreateClient("SwaAuth");
        var response = await http.GetFromJsonAsync<SwaAuthResponse>("/.auth/me");

        var tokenClaim = response?.ClientPrincipal?.Claims?
            .FirstOrDefault(c =>
                c.Typ == "access_token" ||
                c.Typ == "https://storage.azure.com/access_token");

        if (tokenClaim?.Val == null)
            throw new InvalidOperationException(
                "Azure Storage access token not found in SWA claims.");

        _cachedToken = tokenClaim.Val;
        _tokenExpiry = DateTime.UtcNow.AddHours(1);
        return _cachedToken;
    }

    public async Task EnsureDelegationKeyAsync()
    {
        if (_delegationKeyValue != null && DateTime.UtcNow < _delegationKeyExpiry.AddMinutes(-5))
            return;

        var token = await GetAccessTokenAsync();
        _delegationKeyStart = DateTime.UtcNow.AddMinutes(-5);
        _delegationKeyExpiry = DateTime.UtcNow.AddHours(23);

        var url = $"https://{AccountName}.blob.core.windows.net/?restype=service&comp=userdelegationkey";
        var body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                   "<KeyInfo>" +
                   $"<Start>{_delegationKeyStart:yyyy-MM-ddTHH:mm:ssZ}</Start>" +
                   $"<Expiry>{_delegationKeyExpiry:yyyy-MM-ddTHH:mm:ssZ}</Expiry>" +
                   "</KeyInfo>";

        var client = _httpClientFactory.CreateClient("AzureStorage");
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-ms-version", "2020-12-06");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        _delegationKeyValue = xml.Root!.Element("Value")!.Value;
        _delegationKeySignedOid = xml.Root!.Element("SignedOid")!.Value;
        _delegationKeySignedTid = xml.Root!.Element("SignedTid")!.Value;
    }

    public async Task<string> GetBlobSasUrlAsync(string blobPath, string permissions = "r", int expiryMinutes = 60)
    {
        await EnsureDelegationKeyAsync();
        var start = DateTime.UtcNow.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var expiry = DateTime.UtcNow.AddMinutes(expiryMinutes).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var canonicalizedResource = $"/blob/{AccountName}/{ContainerName}/{blobPath.TrimStart('/')}";
        var sig = ComputeSignature(BuildStringToSign(permissions, start, expiry, canonicalizedResource, "b"));

        var q = BuildSasParams("b", permissions, start, expiry, sig);
        return $"https://{AccountName}.blob.core.windows.net/{ContainerName}/{Uri.EscapeDataString(blobPath.TrimStart('/'))}?{q}";
    }

    public async Task<string> GetContainerSasUrlAsync(string permissions = "rl", int expiryMinutes = 60)
    {
        await EnsureDelegationKeyAsync();
        var start = DateTime.UtcNow.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var expiry = DateTime.UtcNow.AddMinutes(expiryMinutes).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var canonicalizedResource = $"/blob/{AccountName}/{ContainerName}";
        var sig = ComputeSignature(BuildStringToSign(permissions, start, expiry, canonicalizedResource, "c"));

        var q = BuildSasParams("c", permissions, start, expiry, sig);
        return $"https://{AccountName}.blob.core.windows.net/{ContainerName}?{q}";
    }

    private string BuildSasParams(string resource, string permissions, string start, string expiry, string sig)
    {
        var p = new Dictionary<string, string>
        {
            ["sv"] = "2020-12-06",
            ["sr"] = resource,
            ["st"] = start,
            ["se"] = expiry,
            ["sp"] = permissions,
            ["spr"] = "https",
            ["skoid"] = _delegationKeySignedOid!,
            ["sktid"] = _delegationKeySignedTid!,
            ["skt"] = _delegationKeyStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["ske"] = _delegationKeyExpiry.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["sks"] = "b",
            ["skv"] = "2020-12-06",
            ["sig"] = Uri.EscapeDataString(sig)
        };
        return string.Join("&", p.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private string BuildStringToSign(string permissions, string start, string expiry,
                                      string canonicalizedResource, string resource)
    {
        var lines = new[]
        {
            permissions, start, expiry, canonicalizedResource,
            _delegationKeySignedOid!, _delegationKeySignedTid!,
            _delegationKeyStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            _delegationKeyExpiry.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            "b", "2020-12-06", "", "", "", "", "https", "2020-12-06",
            resource, "", "", "", "", ""
        };
        return string.Join("\n", lines);
    }

    private string ComputeSignature(string stringToSign)
    {
        var keyBytes = Convert.FromBase64String(_delegationKeyValue!);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }
}
