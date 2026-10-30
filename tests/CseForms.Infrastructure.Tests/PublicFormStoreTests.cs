using CseForms.Application.PublicForms;
using CseForms.Domain.Forms;
using CseForms.Infrastructure.Persistence;
using CseForms.Infrastructure.PublicForms;
using CseForms.Infrastructure.Security;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Tests;

public class PublicFormStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    private async Task<SaveSubmissionOutcome> SaveAsync(Form form, Guid? versionId = null, int year = 2026)
    {
        await using var db = fixture.CreateContext();
        return await new PublicFormStore(db).SaveSubmissionAsync(
            new SaveSubmissionCommand(form.Id, versionId ?? form.CurrentVersionId!.Value, """{"nom":"FICTIF"}""", Now, year),
            CancellationToken.None);
    }

    [Fact]
    public async Task Draft_form_is_not_found()
    {
        await using var db = fixture.CreateContext();
        db.Forms.Add(new Form("store-brouillon", "Brouillon", Now));
        await db.SaveChangesAsync();

        Assert.Null(await new PublicFormStore(db).FindPublishedAsync("store-brouillon", CancellationToken.None));
        Assert.Null(await new PublicFormStore(db).FindPublishedAsync(new string('a', 500), CancellationToken.None));
    }

    [Fact]
    public async Task Published_form_is_found_with_current_version_and_count()
    {
        Form form;
        await using (var db = fixture.CreateContext())
        {
            form = await TestData.AddPublishedFormAsync(db, "store-publie");
        }

        await SaveAsync(form);

        await using (var db = fixture.CreateContext())
        {
            var published = await new PublicFormStore(db).FindPublishedAsync(" Store-Publie ", CancellationToken.None);

            Assert.NotNull(published);
            Assert.Equal(form.CurrentVersionId, published.Version.Id);
            Assert.Equal(1, published.ResponseCount);
        }
    }

    [Fact]
    public async Task Case_numbers_follow_each_other_per_form_and_year()
    {
        Form form;
        await using (var db = fixture.CreateContext())
        {
            form = await TestData.AddPublishedFormAsync(db, "store-numeros", configure: f => f.SetCaseNumberPrefix("NUM"));
        }

        var first = Assert.IsType<SaveSubmissionOutcome.Created>(await SaveAsync(form));
        var second = Assert.IsType<SaveSubmissionOutcome.Created>(await SaveAsync(form));
        var nextYear = Assert.IsType<SaveSubmissionOutcome.Created>(await SaveAsync(form, year: 2027));

        Assert.Equal("NUM-2026-0001", first.CaseNumber);
        Assert.Equal("NUM-2026-0002", second.CaseNumber);
        Assert.Equal("NUM-2027-0001", nextYear.CaseNumber);
    }

    [Fact]
    public async Task Outdated_version_is_refused()
    {
        Form form;
        Guid oldVersion;
        await using (var db = fixture.CreateContext())
        {
            form = await TestData.AddPublishedFormAsync(db, "store-version");
            oldVersion = form.CurrentVersionId!.Value;
            form.Publish(TestData.SimpleDefinition, null, null, Now, "test");
            await db.SaveChangesAsync();
        }

        Assert.IsType<SaveSubmissionOutcome.VersionChanged>(await SaveAsync(form, oldVersion));
    }

    [Fact]
    public async Task Concurrent_submissions_respect_max_responses_and_unique_numbers()
    {
        Form form;
        await using (var db = fixture.CreateContext())
        {
            form = await TestData.AddPublishedFormAsync(db, "store-concurrence", configure: f =>
            {
                f.SetCaseNumberPrefix("CONC");
                f.SetMaxResponses(5);
            });
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => SaveAsync(form))));

        var created = outcomes.OfType<SaveSubmissionOutcome.Created>().ToList();
        Assert.Equal(5, created.Count);
        Assert.Equal(15, outcomes.OfType<SaveSubmissionOutcome.NotAccepting>().Count());
        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => $"CONC-2026-{i:D4}"),
            created.Select(c => c.CaseNumber).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Closed_form_refuses_submission()
    {
        Form form;
        await using (var db = fixture.CreateContext())
        {
            form = await TestData.AddPublishedFormAsync(db, "store-ferme", configure: f =>
            {
                f.SetCaseNumberPrefix("FERME");
                f.Close();
            });
        }

        Assert.IsType<SaveSubmissionOutcome.NotAccepting>(await SaveAsync(form));

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.Set<CaseNumberCounter>().AnyAsync(c => c.Prefix == "FERME"));
        }
    }

    [Fact]
    public async Task Forms_sharing_a_prefix_never_share_a_case_number()
    {
        Form first;
        Form second;
        await using (var db = fixture.CreateContext())
        {
            first = await TestData.AddPublishedFormAsync(db, "store-prefixe-a", configure: f => f.SetCaseNumberPrefix("PARTA"));
            second = await TestData.AddPublishedFormAsync(db, "store-prefixe-b", configure: f => f.SetCaseNumberPrefix("PARTA"));
        }

        var a = Assert.IsType<SaveSubmissionOutcome.Created>(await SaveAsync(first));
        var b = Assert.IsType<SaveSubmissionOutcome.Created>(await SaveAsync(second));

        Assert.Equal("PARTA-2026-0001", a.CaseNumber);
        Assert.Equal("PARTA-2026-0002", b.CaseNumber);
    }

    [Fact]
    public void Access_code_hash_is_salted_and_verifiable()
    {
        var hasher = new AccessCodeHasher();

        var first = hasher.Hash("4821");
        var second = hasher.Hash("4821");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("4821", first, StringComparison.Ordinal);
        Assert.True(hasher.Verify(first, "4821"));
        Assert.False(hasher.Verify(first, "4822"));
        Assert.False(hasher.Verify("pas-un-hache", "4821"));
    }
}
