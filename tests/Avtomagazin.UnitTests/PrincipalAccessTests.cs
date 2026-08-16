using System.Security.Claims;
using Avtomagazin.Contracts;

namespace Avtomagazin.UnitTests;

public class PrincipalAccessTests
{
    [Fact]
    public void Driver_writes_only_assigned_van()
    {
        var van = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var driver = Principal(Roles.Driver, van);

        Assert.True(driver.CanWriteVehicle(van));
        Assert.False(driver.CanWriteVehicle(other));
        Assert.False(driver.CanDispatch());
    }

    [Fact]
    public void Seller_writes_only_assigned_van()
    {
        var van = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var seller = Principal(Roles.Seller, van);

        Assert.True(seller.CanWriteVehicle(van));
        Assert.False(seller.CanWriteVehicle(other));
        Assert.False(seller.CanDispatch());
    }

    [Fact]
    public void Dispatcher_writes_any_van()
    {
        var van = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Assert.True(Principal(Roles.Operator, null).CanWriteVehicle(van));
        Assert.True(Principal(Roles.Admin, null).CanWriteVehicle(van));
        Assert.True(Principal(Roles.Admin, null).CanDispatch());
    }

    [Fact]
    public void Resident_cannot_write_van()
    {
        var van = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Assert.False(Principal(Roles.Resident, null).CanWriteVehicle(van));
    }

    private static ClaimsPrincipal Principal(string role, Guid? vehicleId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        };
        if (vehicleId is Guid id)
        {
            claims.Add(new Claim(AuthClaims.VehicleId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
