using System.Net;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

public sealed class SigningKeySourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("answer")]
    [InlineData("webhook")]
    public async Task Optional_purpose_is_preserved_when_reading_and_caching_published_keys(string? purpose)
    {
        using var wire = new KeyDocumentWire();
        wire.Body = "{\"keys\":[{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"axfR8uEsQkf4vOblY6RA8ncDfYEt6zOg9KE5RdiYwpY\",\"y\":\"T-NC4v4af5uO5-tKfA-eFivOM1drMV7Oy7ZAaDe_UfU\",\"kid\":\"key/version\",\"use\":\"sig\",\"alg\":\"ES256\",\"status\":\"active\",\"notBefore\":\"2026-10-20T08:00:00Z\""
            + (purpose is null ? string.Empty : ",\"purpose\":\"" + purpose + "\"") + "}]}";
        using var http = Client(wire);
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), new ManualClock());
        var keys = await source.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(purpose, Assert.Single(keys.Keys).Purpose);
        Assert.Equal("key/version", keys.Keys[0].Kid);
        Assert.Same(keys, await source.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, wire.Fetches);
    }

    [Fact]
    public void A_cache_longer_than_five_minutes_is_refused()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);

        var refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => new HttpSigningKeySource(http, TimeSpan.FromSeconds(301), new ManualClock()));

        Assert.Equal("cacheDuration", refused.ParamName);
        Assert.NotNull(new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), new ManualClock()));
        Assert.Equal(TimeSpan.FromMinutes(5), new AnisPartnersClientOptions().SigningKeyCacheDuration);
        Assert.Equal(0, wire.Fetches);
    }

    [Fact]
    public async Task An_unknown_key_refetch_happens_at_most_once_per_thirty_seconds_even_when_it_fails()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;

        var held = await source.GetAsync(ct);
        Assert.Equal(1, wire.Fetches);
        wire.Status = HttpStatusCode.ServiceUnavailable;

        var refused = await Assert.ThrowsAsync<HttpRequestException>(async () => await source.RefreshAsync(ct));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(2, wire.Fetches);
        Assert.Same(held, await source.RefreshAsync(ct));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Same(held, await source.RefreshAsync(ct));
        Assert.Equal(2, wire.Fetches);

        clock.Advance(TimeSpan.FromSeconds(1));
        wire.Status = HttpStatusCode.OK;
        var refreshed = await source.RefreshAsync(ct);

        Assert.NotSame(held, refreshed);
        Assert.Same(refreshed, await source.GetAsync(ct));
        Assert.Equal(3, wire.Fetches);
    }

    [Fact]
    public async Task Callers_waiting_on_one_refresh_share_its_fetch()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), new ManualClock());
        var ct = TestContext.Current.CancellationToken;
        await source.GetAsync(ct);

        wire.PauseNextFetch();
        var callers = Enumerable.Range(0, 8).Select(_ => source.RefreshAsync(ct).AsTask()).ToArray();
        await wire.FetchStarted.WaitAsync(ct);
        Assert.Equal(2, wire.Fetches);
        Assert.All(callers, caller => Assert.False(caller.IsCompleted));
        wire.ReleaseFetch();

        var documents = await Task.WhenAll(callers);

        Assert.All(documents, document => Assert.Same(documents[0], document));
        Assert.Equal(2, wire.Fetches);
    }

    [Fact]
    public async Task Callers_waiting_on_a_refresh_across_the_cooldown_boundary_share_one_fetch()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;
        await source.GetAsync(ct);

        wire.PauseNextFetch();
        var first = source.RefreshAsync(ct).AsTask();
        await wire.FetchStarted.WaitAsync(ct);
        var early = Enumerable.Range(0, 4).Select(_ => source.RefreshAsync(ct).AsTask()).ToArray();
        clock.Advance(TimeSpan.FromSeconds(30));
        var late = Enumerable.Range(0, 4).Select(_ => source.RefreshAsync(ct).AsTask()).ToArray();
        var callers = new[] { first }.Concat(early).Concat(late).ToArray();
        Assert.Equal(2, wire.Fetches);
        Assert.All(callers, caller => Assert.False(caller.IsCompleted));
        wire.ReleaseFetch();

        var documents = await Task.WhenAll(callers);

        Assert.Equal(2, wire.Fetches);
        Assert.All(documents, document => Assert.Same(documents[0], document));

        var refreshed = await source.RefreshAsync(ct);
        Assert.Equal(3, wire.Fetches);
        Assert.NotSame(documents[0], refreshed);
    }

    [Fact]
    public async Task Callers_that_miss_an_expired_cache_together_share_one_fetch()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;
        var held = await source.GetAsync(ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        wire.PauseNextFetch();
        var callers = Enumerable.Range(0, 8).Select(_ => source.GetAsync(ct).AsTask()).ToArray();
        await wire.FetchStarted.WaitAsync(ct);
        Assert.Equal(2, wire.Fetches);
        Assert.All(callers, caller => Assert.False(caller.IsCompleted));
        wire.ReleaseFetch();

        var documents = await Task.WhenAll(callers);

        Assert.NotSame(held, documents[0]);
        Assert.All(documents, document => Assert.Same(documents[0], document));
        Assert.Equal(2, wire.Fetches);
    }

    [Fact]
    public async Task A_backwards_wall_clock_step_does_not_change_the_cooldown()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;
        await source.GetAsync(ct);
        var held = await source.RefreshAsync(ct);
        Assert.Equal(2, wire.Fetches);

        clock.AdjustWallClock(TimeSpan.FromMinutes(-1));
        Assert.Same(held, await source.RefreshAsync(ct));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Same(held, await source.RefreshAsync(ct));
        Assert.Equal(2, wire.Fetches);

        clock.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await source.RefreshAsync(ct);

        Assert.NotSame(held, refreshed);
        Assert.Equal(3, wire.Fetches);
    }

    [Fact]
    public async Task The_cached_document_is_still_used_at_299_seconds()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;
        var held = await source.GetAsync(ct);

        clock.Advance(TimeSpan.FromSeconds(299));

        Assert.Same(held, await source.GetAsync(ct));
        Assert.Equal(1, wire.Fetches);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.NotSame(held, await source.GetAsync(ct));
        Assert.Equal(2, wire.Fetches);
    }

    [Fact]
    public async Task A_backwards_wall_clock_step_does_not_extend_trust_past_300_seconds()
    {
        using var wire = new KeyDocumentWire();
        using var http = Client(wire);
        var clock = new ManualClock();
        var source = new HttpSigningKeySource(http, TimeSpan.FromMinutes(5), clock);
        var ct = TestContext.Current.CancellationToken;
        var held = await source.GetAsync(ct);

        clock.Advance(TimeSpan.FromSeconds(299));
        clock.AdjustWallClock(TimeSpan.FromHours(-1));

        Assert.Same(held, await source.GetAsync(ct));
        Assert.Equal(1, wire.Fetches);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.NotSame(held, await source.GetAsync(ct));
        Assert.Equal(2, wire.Fetches);
    }

    private static HttpClient Client(KeyDocumentWire wire)
        => new(wire) { BaseAddress = new Uri("https://partners.example/") };

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = PipelineFixture.Now;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            _now += duration;
            _timestamp += duration.Ticks;
        }

        public void AdjustWallClock(TimeSpan duration) => _now += duration;
    }

    private sealed class KeyDocumentWire : HttpMessageHandler
    {
        private TaskCompletionSource? _fetchStarted;
        private TaskCompletionSource? _fetchReleased;

        public int Fetches { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = """{"keys":[]}""";
        public Task FetchStarted => _fetchStarted!.Task;

        public void PauseNextFetch()
        {
            _fetchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _fetchReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseFetch() => _fetchReleased!.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Fetches++;
            Assert.Equal("/.well-known/partner-signing-keys.json", request.RequestUri!.AbsolutePath);

            if (_fetchReleased is { } released)
            {
                _fetchStarted!.SetResult();
                await released.Task.WaitAsync(cancellationToken);
                _fetchReleased = null;
            }

            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
