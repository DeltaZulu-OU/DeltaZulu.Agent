using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DeltaZulu.Agent.ControlPlane;

/// <summary>
/// HTTPS client for the DeltaZulu.Platform agent control plane pull protocol
/// (enroll, heartbeat, policy bundle pull/ack, command result).
/// </summary>
public sealed class ControlPlaneClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ControlPlaneClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (httpClient.BaseAddress is { Scheme: not "https" } baseAddress)
        {
            throw new ArgumentException(
                $"ControlPlaneClient requires an https base address to avoid sending the agent secret " +
                $"and heartbeat content in cleartext; got '{baseAddress}'.", nameof(httpClient));
        }

        _http = httpClient;
    }

    public void UseAgentSecret(string agentSecret) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", agentSecret);

    public async Task<EnrollResponse> EnrollAsync(EnrollRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/agent/v1/enroll", request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<EnrollResponse>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/agent/v1/heartbeat", request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    public async Task<BundleResponse> GetBundleAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(new Uri("/api/agent/v1/policy/bundle", UriKind.Relative), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<BundleResponse>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    public async Task AckAsync(AckRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/agent/v1/policy/ack", request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task PostCommandResultAsync(string commandId, CommandResultRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(
            $"/api/agent/v1/commands/{commandId}/result", request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}", null, response.StatusCode);
    }
}
