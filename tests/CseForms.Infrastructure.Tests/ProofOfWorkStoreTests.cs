using CseForms.Infrastructure.ProofOfWork;
using CseForms.Testing;

namespace CseForms.Infrastructure.Tests;

public class ProofOfWorkStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Challenge_is_consumed_only_once_and_expired_ones_are_deleted()
    {
        var fresh = new string('a', 64);
        var expired = new string('b', 64);

        await using (var db = fixture.CreateContext())
        {
            var store = new ProofOfWorkStore(db);
            Assert.True(await store.TryConsumeAsync(fresh, Now.AddMinutes(10), default));
            Assert.False(await store.TryConsumeAsync(fresh, Now.AddMinutes(10), default));
            Assert.True(await store.TryConsumeAsync(expired, Now.AddMinutes(-1), default));

            Assert.Equal(1, await store.DeleteExpiredAsync(Now, default));
        }

        await using (var db = fixture.CreateContext())
        {
            var store = new ProofOfWorkStore(db);
            Assert.False(await store.TryConsumeAsync(fresh, Now.AddMinutes(10), default));
            Assert.True(await store.TryConsumeAsync(expired, Now.AddMinutes(10), default));
        }
    }
}
