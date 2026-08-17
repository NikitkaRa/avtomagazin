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
        Assert.Empty(snap.Eta);
        Assert.Empty(snap.Routes);
        Assert.Empty(snap.DriverNotes);
        Assert.Contains(hits, h => h.Contains("fleet/api/vehicles", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/cases", StringComparison.Ordinal));
        Assert.DoesNotContain(hits, h => h.Contains("routing/api/eta", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/routes", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("routing/api/driver-notes", StringComparison.Ordinal));
        Assert.Equal(4, hits.Count);
    }

    private sealed class RecordingHandler(List<string> hits, Func<string, string> bodyFor) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            hits.Add(path);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyFor(path), Encoding.UTF8, "application/json")
            };
        }
    }
}
