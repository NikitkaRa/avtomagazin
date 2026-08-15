using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avtomagazin.IntegrationTests;

public class IdentityApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public IdentityApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_with_demo_user_returns_jwt()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "resident@demo.by",
            password = "demo"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal("resident", doc.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_seller_is_driver_for_pukhovichi_van()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "seller@demo.by",
            password = "demo"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("driver", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            doc.RootElement.GetProperty("vehicleId").GetGuid());
    }

    [Fact]
    public async Task Login_with_bad_password_returns_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "resident@demo.by",
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_creates_resident_and_cannot_pick_staff_role()
    {
        var email = $"user-{Guid.NewGuid():N}@demo.by";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            name = "Марина",
            role = "admin"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("resident", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("Марина", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("active", doc.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Register_staff_is_pending_until_admin_approves()
    {
        var email = $"driver-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            name = "Пётр",
            client = "staff",
            staffRole = "driver"
        });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        Assert.Equal("driver", created.RootElement.GetProperty("role").GetString());
        Assert.Equal("pending", created.RootElement.GetProperty("status").GetString());
        Assert.True(created.RootElement.GetProperty("accessToken").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        var userId = created.RootElement.GetProperty("userId").GetGuid();

        var pendingLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" });
        Assert.Equal(HttpStatusCode.Forbidden, pendingLogin.StatusCode);

        using var admin = await AuthedAsync("admin@demo.by");
        var approve = await admin.PostAsJsonAsync($"/api/users/{userId}/approve", new
        {
            role = "driver",
            vehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var tokenDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(tokenDoc.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal("active", tokenDoc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_staff_without_role_returns_bad_request()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"staff-{Guid.NewGuid():N}@demo.by",
            password = "secret12",
            client = "staff"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_cannot_self_serve_admin()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"boss-{Guid.NewGuid():N}@demo.by",
            password = "secret12",
            client = "staff",
            staffRole = "admin"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_list_is_admin_only()
    {
        var anonymous = await _client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var dispatcher = await AuthedAsync("operator@demo.by");
        var forbidden = await dispatcher.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = await AuthedAsync("admin@demo.by");
        var ok = await admin.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Admin_can_reject_pending_and_disable_active_staff()
    {
        var email = $"disp-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            name = "Оля",
            client = "staff",
            staffRole = "operator"
        });
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var pendingId = created.RootElement.GetProperty("userId").GetGuid();

        using var admin = await AuthedAsync("admin@demo.by");
        var reject = await admin.PostAsJsonAsync($"/api/users/{pendingId}/reject", new { });
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var gone = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" });
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);

        var keepEmail = $"keep-{Guid.NewGuid():N}@demo.by";
        var keep = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = keepEmail,
            password = "secret12",
            client = "staff",
            staffRole = "driver"
        });
        using var keepDoc = JsonDocument.Parse(await keep.Content.ReadAsStringAsync());
        var keepId = keepDoc.RootElement.GetProperty("userId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{keepId}/approve", new
        {
            vehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{keepId}/disable", new { })).StatusCode);

        var disabledLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email = keepEmail, password = "secret12" });
        Assert.Equal(HttpStatusCode.Forbidden, disabledLogin.StatusCode);
    }

    private async Task<HttpClient> AuthedAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "demo" });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Register_duplicate_email_returns_conflict()
    {
        var email = $"dup-{Guid.NewGuid():N}@demo.by";
        var body = new { email, password = "secret12", name = "A" };
        var first = await _client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await _client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Me_returns_user_for_valid_token()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "resident@demo.by",
            password = "demo"
        });
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meDoc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("resident@demo.by", meDoc.RootElement.GetProperty("email").GetString());
        Assert.Equal("resident", meDoc.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Request-Id").FirstOrDefault()));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Request_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/alive");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "demo-request-123");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("demo-request-123", response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task Disable_rejects_old_jwt()
    {
        var email = $"revoked-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            client = "staff",
            staffRole = "operator"
        });
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("userId").GetGuid();

        using var admin = await AuthedAsync("admin@demo.by");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{id}/approve", new { })).StatusCode);

        using var staff = _factory.CreateClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        staff.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                doc.RootElement.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{id}/disable", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Approve_driver_without_van_fails()
    {
        var email = $"novan-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            client = "staff",
            staffRole = "driver"
        });
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("userId").GetGuid();

        using var admin = await AuthedAsync("admin@demo.by");
        var approve = await admin.PostAsJsonAsync($"/api/users/{id}/approve", new { role = "driver" });
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"short-{Guid.NewGuid():N}@demo.by",
            password = "1234567",
            client = "resident"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_issues_new_jwt_and_kills_old()
    {
        var email = $"pwd-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            client = "resident"
        });
        register.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var oldToken = created.RootElement.GetProperty("accessToken").GetString();

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldToken);

        var wrong = await authed.PostAsJsonAsync("/api/auth/password", new { current = "nope-nope", next = "secret99" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var shortNext = await authed.PostAsJsonAsync("/api/auth/password", new { current = "secret12", next = "1234567" });
        Assert.Equal(HttpStatusCode.BadRequest, shortNext.StatusCode);

        var changed = await authed.PostAsJsonAsync("/api/auth/password", new { current = "secret12", next = "secret99" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedDoc = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        var nextToken = changedDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nextToken));
        Assert.NotEqual(oldToken, nextToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await authed.GetAsync("/api/auth/me")).StatusCode);

        using var fresh = _factory.CreateClient();
        fresh.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", nextToken);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/me")).StatusCode);

        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret99" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Admin_can_reset_staff_password_but_not_admin()
    {
        var email = $"reset-{Guid.NewGuid():N}@demo.by";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "secret12",
            client = "staff",
            staffRole = "operator"
        });
        using var created = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("userId").GetGuid();

        using var admin = await AuthedAsync("admin@demo.by");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{id}/approve", new { })).StatusCode);

        var reset = await admin.PostAsJsonAsync($"/api/users/{id}/password", new { password = "resetpass" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret12" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "resetpass" })).StatusCode);

        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        Guid? adminId = null;
        foreach (var u in users.EnumerateArray())
        {
            if (u.GetProperty("email").GetString() == "admin@demo.by")
            {
                adminId = u.GetProperty("id").GetGuid();
            }
        }

        Assert.True(adminId.HasValue);
        var blocked = await admin.PostAsJsonAsync($"/api/users/{adminId}/password", new { password = "resetpass" });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }
}
