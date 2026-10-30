using System.Text.Json;
using CseForms.Domain.Forms;
using CseForms.Domain.Sites;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CseForms.Infrastructure.Tests;

public class PersistenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Published_form_and_its_versions_are_persisted()
    {
        var form = new Form("persist-versions", "Titre", Now);
        form.Publish("""{"fields":[]}""", null, null, Now, "editeur");
        form.Publish("""{"fields":[{"id":"nom"}]}""", """{"steps":1}""", "<p></p>", Now.AddDays(1), "editeur");

        await using (var context = fixture.CreateContext())
        {
            context.Forms.Add(form);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var loaded = await context.Forms.Include(f => f.Versions).SingleAsync(f => f.Id == form.Id);

            Assert.Equal(FormStatus.Published, loaded.Status);
            Assert.Equal(2, loaded.Versions.Count);
            Assert.Equal(2, loaded.CurrentVersion!.Number);
        }
    }

    [Fact]
    public async Task Submission_data_is_stored_as_jsonb_and_linked_to_its_version()
    {
        var form = new Form("persist-submission", "Titre", Now);
        var version = form.Publish("""{"fields":[]}""", null, null, Now, null);
        var submission = form.CreateSubmission("TST-2026-0001", """{"nom":"DUPONT","prenom":"Camille"}""", Now, 0);

        await using (var context = fixture.CreateContext())
        {
            context.Forms.Add(form);
            context.Submissions.Add(submission);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var loaded = await context.Submissions.SingleAsync(s => s.Id == submission.Id);
            Assert.Equal(version.Id, loaded.FormVersionId);

            using var data = JsonDocument.Parse(loaded.DataJson);
            Assert.Equal("DUPONT", data.RootElement.GetProperty("nom").GetString());

            // Requête sur le contenu JSONB : vérifie le type réel de la colonne.
            var count = await context.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM submission WHERE data_json ->> 'nom' = {"DUPONT"}")
                .SingleAsync();
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task Version_referenced_by_a_submission_cannot_be_deleted()
    {
        var form = new Form("persist-restrict", "Titre", Now);
        var version = form.Publish("""{"fields":[]}""", null, null, Now, null);
        var submission = form.CreateSubmission("TST-2026-0002", "{}", Now, 0);

        await using (var context = fixture.CreateContext())
        {
            context.Forms.Add(form);
            context.Submissions.Add(submission);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlAsync($"DELETE FROM form_version WHERE id = {version.Id}"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
    }

    [Fact]
    public async Task Slug_is_unique()
    {
        await using var context = fixture.CreateContext();
        context.Forms.Add(new Form("persist-unique", "A", Now));
        context.Forms.Add(new Form("persist-unique", "B", Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Sites_are_persisted()
    {
        var site = new Site("Site fictif");

        await using (var context = fixture.CreateContext())
        {
            context.Sites.Add(site);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.True((await context.Sites.SingleAsync(s => s.Id == site.Id)).IsActive);
        }
    }
}
