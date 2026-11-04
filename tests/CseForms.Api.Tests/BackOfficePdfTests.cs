using System.Net;
using CseForms.Application.Pdf;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Api.Tests;

/// <summary>PDF d'une réponse dans le back-office : état, téléchargement journalisé, régénération.</summary>
public partial class BackOfficeTests
{
    private static readonly byte[] FakePdf = "%PDF-1.7 fictif"u8.ToArray();

    private async Task StorePdfAsync(Guid submissionId)
    {
        var vault = factory.Services.GetRequiredService<IFileVault>();
        var fileId = Guid.NewGuid();
        var stored = await vault.WriteAsync(fileId, submissionId, FakePdf, default);

        await using var db = factory.Database.CreateContext();
        db.SubmissionFiles.Add(new SubmissionFile(fileId, submissionId, SubmissionFileKind.Pdf, stored.StoragePath, stored.Sha256, stored.SizeBytes, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<List<SubmissionJob>> JobsAsync(Guid submissionId)
    {
        await using var db = factory.Database.CreateContext();
        return await db.Jobs.Where(j => j.SubmissionId == submissionId).ToListAsync();
    }

    [Fact]
    public async Task Pdf_is_downloaded_decrypted_and_the_download_is_audited()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf");
        await StorePdfAsync(submissionId);
        var admin = await CreateUserAsync("admin");
        var client = Client();
        await LoginAsync(client, admin, Password);

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.Contains($"/admin/reponses/{submissionId}/pdf", page);

        var response = await client.GetAsync($"/admin/reponses/{submissionId}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bo-pdf-0001.pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(FakePdf, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == "submission.download");
    }

    [Fact]
    public async Task Pdf_requires_login_and_rights()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf-droits");
        await StorePdfAsync(submissionId);

        var anonymous = await Client().GetAsync($"/admin/reponses/{submissionId}/pdf");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("https://localhost/admin/connexion", anonymous.Headers.Location!.ToString(), StringComparison.Ordinal);

        var editor = await CreateUserAsync("editor");
        var client = Client();
        await LoginAsync(client, editor, Password);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/reponses/{submissionId}/pdf")).StatusCode);

        var audit = await AuditAsync($"submission:{submissionId}");
        Assert.DoesNotContain(audit, e => e.Action == "submission.download");
        Assert.Contains(audit, e => e.Action == "access.denied");
    }

    [Fact]
    public async Task Pending_generation_is_shown_and_missing_pdf_is_not_found()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf-attente");
        await using (var db = factory.Database.CreateContext())
        {
            db.Jobs.Add(new SubmissionJob(submissionId, JobType.Pdf, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var admin = await CreateUserAsync("admin");
        var client = Client();
        await LoginAsync(client, admin, Password);

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.Contains("Génération en cours", page);
        Assert.DoesNotContain("Régénérer le PDF", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/reponses/{submissionId}/pdf")).StatusCode);
    }

    [Fact]
    public async Task Editor_can_request_a_new_pdf_once()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf-regen");
        await StorePdfAsync(submissionId);
        var editor = await CreateUserAsync("editor");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", editor, "--form", "bo-pdf-regen"));
        var client = Client();
        await LoginAsync(client, editor, Password);

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.Contains("Régénérer le PDF", page);

        var response = await client.PostAsync(
            $"/admin/reponses/{submissionId}?handler=Regenerate",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(page) }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(JobStatus.Pending, Assert.Single(await JobsAsync(submissionId)).Status);
        Assert.Contains("Nouvelle génération en cours", await client.GetStringAsync($"/admin/reponses/{submissionId}"));
        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == "submission.pdf.regenerate");
    }

    [Fact]
    public async Task Regeneration_requires_the_antiforgery_token()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf-csrf");
        var admin = await CreateUserAsync("admin");
        var client = Client();
        await LoginAsync(client, admin, Password);

        var response = await client.PostAsync($"/admin/reponses/{submissionId}?handler=Regenerate", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await JobsAsync(submissionId));
    }

    [Fact]
    public async Task Reader_cannot_regenerate()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-pdf-lecteur");
        var reader = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", reader, "--form", "bo-pdf-lecteur"));
        var client = Client();
        await LoginAsync(client, reader, Password);

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.DoesNotContain("Régénérer le PDF", page);

        var response = await client.PostAsync(
            $"/admin/reponses/{submissionId}?handler=Regenerate",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(page) }));

        Assert.NotEqual($"/admin/reponses/{submissionId}", response.Headers.Location?.OriginalString);
        Assert.Empty(await JobsAsync(submissionId));
        Assert.Contains(await AuditAsync($"submission:{submissionId}"), e => e.Action == "access.denied");
    }
}
