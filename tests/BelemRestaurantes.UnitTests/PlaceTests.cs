using System;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.Resilience;
using BelemRestaurantes.Infrastructure.Migrations;

namespace BelemRestaurantes.UnitTests.Rules;

public class PlaceValidatorTests
{
    private static BelemRestaurantes.Application.Models.DiscoveredPlace Build(
        string name = "Restaurante Teste", string externalId = "places/abc") =>
        new(externalId, name, "Batista Campos", "Belém", "restaurante", "Rua X", -1.45d, -48.49d,
            "https://maps.example/x", "https://site.example", 4, 10, ["restaurant"]);

    [Fact]
    public void ValidPlace_Accepted() =>
        Assert.True(PlaceValidator.Validate(Build()).IsAcceptable);

    [Fact]
    public void PlaceOutsidePA_Rejected()
    {
        var place = Build() with { Latitude = 10d };
        Assert.False(PlaceValidator.Validate(place).IsAcceptable);
    }

    [Fact]
    public void InvalidRating_Rejected()
    {
        var place = Build() with { GoogleRating = 9 };
        Assert.False(PlaceValidator.Validate(place).IsAcceptable);
    }

    [Theory]
    [InlineData("reviews")] [InlineData("photos")] [InlineData("photoUrls")]
    public void ForbiddenFields_remain_Forbidden(string field) =>
        Assert.False(PlaceValidator.IsPersistable(field));

    [Fact]
    public void PersistedFields_Are_Whitelisted()
    {
        foreach (var field in new[] { "id", "displayName", "rating", "types", "websiteUri" })
        {
            Assert.True(PlaceValidator.IsPersistable(field));
        }
    }
}

public class SqlSplitterTests
{
    [Fact]
    public void Splits_Commas_And_Semicolons()
    {
        const string sql = "SELECT 1; -- comment\nINSERT INTO t (a) VALUES ('a;b');";
        var parts = SqlScriptSplitter.Split(sql);
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Keeps_Line_Comment_Intact()
    {
        const string script = "CREATE TABLE x (a INT);\n-- a;b\nCREATE TABLE y (b INT);";
        var parts = SqlScriptSplitter.Split(script);
        Assert.Equal(2, parts.Count);
    }
}

public class CircuitBreakerTests
{
    [Fact]
    public void Opens_After_Threshold()
    {
        var cb = new CircuitBreaker(2, TimeSpan.FromMinutes(1));
        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Open, cb.State);
        Assert.False(cb.TryEnter());
    }

    [Fact]
    public void HalfOpen_After_Break()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var cb = new CircuitBreaker(1, TimeSpan.FromMinutes(2), clock);
        cb.RecordFailure();
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(CircuitState.HalfOpen, cb.State);
    }
}

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
    public FakeClock(DateTimeOffset start) => UtcNow = start;
    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}

public class PasswordLockoutTests
{
    [Fact]
    public void LockersEscalateWithFailures()
    {
        var user = new AdminUser { Id = "u", AccessFailedCount = 4 };
        var result = AdminPasswordPolicy.RegisterFailure(user, DateTimeOffset.UtcNow);
        Assert.True(result.ShouldLockOut);
    }
}
