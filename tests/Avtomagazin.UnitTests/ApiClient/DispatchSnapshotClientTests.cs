using System.Net;
using System.Text;
using Avtomagazin.ApiClient;

namespace Avtomagazin.UnitTests.ApiClient;

public class DispatchSnapshotClientTests
{
    [Fact]
    public async Task GetDispatchSnapshotAsync_fans_out_four_gets_in_parallel()
    {
        var hits = new List<string>();
        var handler = new RecordingHandler(hits, path => path switch
        {
            "fleet/api/vehicles" => """[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","plateNumber":"1234 AB-7","operatorName":"A","isActive":true}]""",
            "routing/api/cases" => """[]""",
            "routing/api/routes" => """[]""",
            "routing/api/driver-notes" => """[]""",
            _ when path.StartsWith("routing/api/cases", StringComparison.Ordinal) => """[]""",
            _ => throw new InvalidOperationException(path)
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://gateway.test/") };
        var client = new AvtomagazinClient(http);
        var snap = await client.GetDispatchSnapshotAsync("open,in_progress");

        Assert.Single(snap.Vehicles);
        Assert.Empty(snap.Cases);
        Assert.Empty(snap.Routes);
        Assert.Empty(snap.DriverNotes);
        Assert.Contains(hits, h => h.Contains("fleet/api/vehicles", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/cases", StringComparison.Ordinal));
        Assert.DoesNotContain(hits, h => h.Contains("routing/api/eta", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/routes", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/driver-notes", StringComparison.Ordinal));
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public async Task GetSnapshotAsync_does_not_treat_failed_routes_as_empty_day()
    {
        var handler = new RecordingHandler([], path => path switch
        {
            "fleet/api/vehicles" => """[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","plateNumber":"1","operatorName":"A","isActive":true}]""",
            "routing/api/routes" => throw new InvalidOperationException("should use status"),
            "routing/api/driver-notes" => """[]""",
            _ => throw new InvalidOperationException(path)
        }, path => path == "routing/api/routes" ? HttpStatusCode.BadGateway : HttpStatusCode.OK);

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://gateway.test/") };
        var client = new AvtomagazinClient(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSnapshotAsync());
    }

    [Fact]
    public async Task GetSnapshotAsync_marks_notes_unavailable_without_emptying_routes()
    {
        var handler = new RecordingHandler([], path => path switch
        {
            "fleet/api/vehicles" => """[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","plateNumber":"1","operatorName":"A","isActive":true}]""",
            "routing/api/routes" => """[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","name":"A","vehicleId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","stops":[]}]""",
            "routing/api/driver-notes" => throw new InvalidOperationException("should use status"),
            _ => throw new InvalidOperationException(path)
        }, path => path == "routing/api/driver-notes" ? HttpStatusCode.BadGateway : HttpStatusCode.OK);

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://gateway.test/") };
        var client = new AvtomagazinClient(http);
        var snap = await client.GetSnapshotAsync();

        Assert.True(snap.NotesUnavailable);
        Assert.Empty(snap.DriverNotes!);
        Assert.Single(snap.Vehicles);
        Assert.Single(snap.Routes);
    }

    private sealed class RecordingHandler(
        List<string> hits,
        Func<string, string> bodyFor,
        Func<string, HttpStatusCode>? statusFor = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            hits.Add(path);
            await Task.Yield();
            var status = statusFor?.Invoke(path) ?? HttpStatusCode.OK;
            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyFor(path), Encoding.UTF8, "application/json")
            };
        }
    }
}
