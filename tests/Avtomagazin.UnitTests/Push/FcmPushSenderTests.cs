using Avtomagazin.Notifications.Api.Push;
using Microsoft.Extensions.Configuration;

namespace Avtomagazin.UnitTests;

public class FcmPushSenderTests
{
    [Fact]
    public void IsConfigured_false_without_account()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.False(FcmPushSender.IsConfigured(config));
    }

    [Fact]
    public void IsConfigured_true_with_service_account_json()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Fcm:ServiceAccountJson"] =
                """{"project_id":"test","client_email":"fcm@test.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----\n"}"""
        }).Build();
        Assert.True(FcmPushSender.IsConfigured(config));
    }

    [Fact]
    public void IsConfigured_false_for_broken_json()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Fcm:ServiceAccountJson"] = "{not-json"
        }).Build();
        Assert.False(FcmPushSender.IsConfigured(config));
    }
}
