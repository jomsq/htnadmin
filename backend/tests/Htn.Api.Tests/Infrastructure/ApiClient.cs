using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Htn.Api.Tests.Infrastructure;

/// <summary>Cookie-holding client that sends a fresh X-CSRF-TOKEN on every state-changing request, like the SPA.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public HttpClient Http => http;

    public async Task<string> CsrfTokenAsync()
    {
        var res = await http.GetAsync("/api/auth/antiforgery");
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("token").GetString()!;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, bool csrf = true) => SendAsync(HttpMethod.Post, url, body, csrf);

    public Task<HttpResponseMessage> PutAsync(string url, object? body = null, bool csrf = true) => SendAsync(HttpMethod.Put, url, body, csrf);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, bool csrf)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        if (csrf) req.Headers.Add("X-CSRF-TOKEN", await CsrfTokenAsync());
        return await http.SendAsync(req);
    }

    /// <summary>multipart/form-data POST with one file field, like the SPA's upload inputs.</summary>
    public async Task<HttpResponseMessage> PostFileAsync(string url, byte[] content, string fileName, string contentType, string field = "file", bool csrf = true)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, field, fileName);
        req.Content = form;
        if (csrf) req.Headers.Add("X-CSRF-TOKEN", await CsrfTokenAsync());
        return await http.SendAsync(req);
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        PostAsync("/api/auth/login", new { email, password });

    public async Task LoginOkAsync(string email, string password)
    {
        var res = await LoginAsync(email, password);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage res) =>
        await res.Content.ReadFromJsonAsync<JsonElement>();
}
