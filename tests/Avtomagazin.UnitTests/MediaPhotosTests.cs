using Avtomagazin.ServiceDefaults;
using NSubstitute;

namespace Avtomagazin.UnitTests;

public class MediaPhotosTests
{
    [Fact]
    public async Task Rejects_arbitrary_http_url()
    {
        var storage = Substitute.For<IObjectStorage>();
        storage.IsOwnedUrl(Arg.Any<string>()).Returns(false);

        var (url, error) = await MediaPhotos.StoreAsync(
            storage, "vans", "https://evil.example/track.jpg", previousUrl: null, clear: false);

        Assert.Null(url);
        Assert.Contains("произвольн", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Keeps_same_url_on_idempotent_save()
    {
        var storage = Substitute.For<IObjectStorage>();
        const string prev = "https://cdn.example/old.jpg";

        var (url, error) = await MediaPhotos.StoreAsync(
            storage, "vans", prev, previousUrl: prev, clear: false);

        Assert.Equal(prev, url);
        Assert.Null(error);
    }

    [Fact]
    public async Task Allows_owned_object_url()
    {
        var storage = Substitute.For<IObjectStorage>();
        const string owned = "http://127.0.0.1:9000/avtomagazin/vans/a.jpg";
        storage.IsOwnedUrl(owned).Returns(true);

        var (url, error) = await MediaPhotos.StoreAsync(
            storage, "vans", owned, previousUrl: null, clear: false);

        Assert.Equal(owned, url);
        Assert.Null(error);
    }
}
