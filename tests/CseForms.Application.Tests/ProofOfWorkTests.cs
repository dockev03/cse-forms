using CseForms.Application.ProofOfWork;

namespace CseForms.Application.Tests;

public class ProofOfWorkServiceTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray());

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delay) => _now += delay;
    }

    private sealed class FakeStore : IProofOfWorkStore
    {
        public HashSet<string> Used { get; } = [];

        public Task<bool> TryConsumeAsync(string challenge, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
            Task.FromResult(Used.Add(challenge));

        public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private static (ProofOfWorkService Service, FakeStore Store, FakeTimeProvider Time) Create(string? key = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
        var store = new FakeStore();
        var options = new ProofOfWorkOptions { HmacKey = key ?? Key, MaxNumber = 1000, ExpiresMinutes = 20 };
        return (new ProofOfWorkService(options, store, time), store, time);
    }

    private static ProofOfWorkSolution Solve(ProofOfWorkChallenge challenge)
    {
        for (var n = 0; n <= challenge.MaxNumber; n++)
        {
            if (ProofOfWorkService.Hash(challenge.Salt, n) == challenge.Challenge)
            {
                return new ProofOfWorkSolution(challenge.Algorithm, challenge.Challenge, n, challenge.Salt, challenge.Signature);
            }
        }

        throw new InvalidOperationException();
    }

    [Fact]
    public void Challenge_follows_the_altcha_format_and_expires()
    {
        var (service, _, _) = Create();

        var challenge = service.CreateChallenge();

        Assert.Equal("SHA-256", challenge.Algorithm);
        Assert.Equal(1000, challenge.MaxNumber);
        Assert.Matches("^[0-9a-f]{64}$", challenge.Challenge);
        Assert.Matches("^[0-9a-f]{64}$", challenge.Signature);
        Assert.EndsWith($"?expires={new DateTimeOffset(2026, 11, 13, 8, 20, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", challenge.Salt, StringComparison.Ordinal);
        Assert.NotEqual(challenge.Salt, service.CreateChallenge().Salt);
    }

    [Fact]
    public async Task Valid_solution_is_accepted_once()
    {
        var (service, store, _) = Create();
        var solution = Solve(service.CreateChallenge());

        Assert.True(await service.VerifyAsync(solution, default));
        Assert.False(await service.VerifyAsync(solution, default));
        Assert.Single(store.Used);
    }

    [Fact]
    public async Task Expired_solution_is_refused()
    {
        var (service, store, time) = Create();
        var solution = Solve(service.CreateChallenge());

        time.Advance(TimeSpan.FromMinutes(20));

        Assert.False(await service.VerifyAsync(solution, default));
        Assert.Empty(store.Used);
    }

    [Fact]
    public async Task Tampered_solutions_are_refused()
    {
        var (service, store, _) = Create();
        var solution = Solve(service.CreateChallenge());
        var laterSalt = solution.Salt![..solution.Salt.IndexOf('=', StringComparison.Ordinal)] + "=4102444800";

        Assert.False(await service.VerifyAsync(null, default));
        Assert.False(await service.VerifyAsync(solution with { Number = (solution.Number + 1) % 1001 }, default));
        Assert.False(await service.VerifyAsync(solution with { Number = -1 }, default));
        Assert.False(await service.VerifyAsync(solution with { Number = 1001 }, default));
        Assert.False(await service.VerifyAsync(solution with { Salt = laterSalt }, default));
        Assert.False(await service.VerifyAsync(solution with { Signature = new string('0', 64) }, default));
        Assert.False(await service.VerifyAsync(solution with { Algorithm = "SHA-1" }, default));
        Assert.False(await service.VerifyAsync(solution with { Salt = new string('a', 200) }, default));
        Assert.Empty(store.Used);
    }

    [Fact]
    public async Task Challenge_signed_with_another_key_is_refused()
    {
        var other = Create(Convert.ToBase64String(new byte[32]));
        var solution = Solve(other.Service.CreateChallenge());

        Assert.False(await Create().Service.VerifyAsync(solution, default));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("pas-du-base64", false)]
    [InlineData("AAAA", false)]
    public void Key_must_be_32_bytes_of_base64(string? key, bool valid)
    {
        Assert.Equal(valid, ProofOfWorkOptions.IsValidKey(key));
        Assert.True(ProofOfWorkOptions.IsValidKey(Key));
    }
}
