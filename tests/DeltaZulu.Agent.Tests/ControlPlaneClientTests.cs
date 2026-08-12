using System.Net;
using System.Net.Http.Json;
using DeltaZulu.Agent.ControlPlane;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ControlPlaneClientTests
{
    [TestMethod]
    public async Task EnrollAsync_PostsToEnrollRouteAndReturnsResponse()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("/api/agent/v1/enroll", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EnrollResponse("agent-1", "tenant-1", "secret", 30)),
            };
        });
        var client = new ControlPlaneClient(new HttpClient(handler) { BaseAddress = new Uri("https://control-plane.example") });

        var response = await client.EnrollAsync(
            new EnrollRequest("bootstrap-token", "host-1", "linux", "1.0.0", Tags: null),
            CancellationToken.None);

        Assert.AreEqual("agent-1", response.AgentId);
        Assert.AreEqual(30, response.HeartbeatIntervalSeconds);
    }

    [TestMethod]
    public async Task HeartbeatAsync_SendsBearerAuthorizationFromAgentSecret()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler((request, ct) =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HeartbeatResponse(null, null, PolicyChanged: false, Commands: null)),
            };
        });
        var client = new ControlPlaneClient(new HttpClient(handler) { BaseAddress = new Uri("https://control-plane.example") });
        client.UseAgentSecret("dz-as-test-secret");

        await client.HeartbeatAsync(
            new HeartbeatRequest("1.0.0", null, null, "healthy", BufferPressure: 0, QueueDepth: 0, DroppedCount: 0, ForwardFailedCount: 0),
            CancellationToken.None);

        Assert.AreEqual("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.AreEqual("dz-as-test-secret", captured.Headers.Authorization!.Parameter);
        Assert.AreEqual("/api/agent/v1/heartbeat", captured.RequestUri!.AbsolutePath);
    }

    [TestMethod]
    public async Task AckAsync_ThrowsWithStatusCodeAndBodyOnFailure()
    {
        var handler = new RecordingHandler((request, ct) => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"bundle.unknown\"}"),
        });
        var client = new ControlPlaneClient(new HttpClient(handler) { BaseAddress = new Uri("https://control-plane.example") });

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            client.AckAsync(new AckRequest("bundle-1", "Applied", null), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("bundle.unknown", exception.Message);
    }

    [TestMethod]
    public void Constructor_RejectsNonHttpsBaseAddress()
    {
        var handler = new RecordingHandler((request, ct) => new HttpResponseMessage(HttpStatusCode.OK));

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            new ControlPlaneClient(new HttpClient(handler) { BaseAddress = new Uri("http://control-plane.example") }));

        Assert.Contains("https", exception.Message);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }
}
