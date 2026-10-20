using System.Globalization;
using System.Text.Json;

namespace UsbDisplay.Core;

/// <summary>One instant-query result: a value, or why there is none. Transport = VictoriaMetrics not reached.</summary>
public readonly record struct Reading(double? Value, string? Error, bool Transport = false)
{
    public static Reading Ok(double v) => new(v, null);

    public static Reading Fail(string error, bool transport = false) => new(null, error, transport);
}

public sealed class PromClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;

    public PromClient(string vmUrl, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _base = vmUrl.TrimEnd('/');
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    public async Task<Reading> QueryAsync(string promql, CancellationToken ct)
    {
        try
        {
            var url = $"{_base}/api/v1/query?query={Uri.EscapeDataString(promql)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // 4xx = VM answered and rejected the query; 5xx = VM itself is in trouble
                return Reading.Fail($"HTTP {(int)resp.StatusCode}", (int)resp.StatusCode >= 500);
            }
            return ParseInstant(body);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Reading.Fail("timeout", true);
        }
        catch (HttpRequestException e)
        {
            return Reading.Fail("unreachable: " + e.Message, true);
        }
    }

    /// <summary>Parses an instant-query response that must hold exactly one sample.</summary>
    public static Reading ParseInstant(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "success") return Reading.Fail("query error");
            var result = root.GetProperty("data").GetProperty("result");
            var n = result.GetArrayLength();
            if (n == 0) return Reading.Fail("no data");
            if (n > 1) return Reading.Fail($"{n} series");
            var raw = result[0].GetProperty("value")[1].GetString();
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v) || double.IsInfinity(v))
            {
                return Reading.Fail("not a number");
            }
            return Reading.Ok(v);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Reading.Fail("bad response");
        }
    }

    public void Dispose() => _http.Dispose();
}
