using System.Net;
using System.Security.Cryptography;
using CseForms.Application.Pdf;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Jobs;
using CseForms.Infrastructure.Pdf;
using CseForms.Infrastructure.Storage;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CseForms.Infrastructure.Tests;

public sealed class EncryptedFileVaultTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cse-forms-vault-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private EncryptedFileVault Vault(string? key = null) =>
        new(Options.Create(new StorageOptions { Path = _root, EncryptionKey = key ?? TestData.StorageKey }));

    private static readonly byte[] Content = "%PDF-1.7 Dossier fictif DUPONT"u8.ToArray();

    [Fact]
    public async Task Content_is_encrypted_on_disk_and_read_back()
    {
        var fileId = Guid.NewGuid();
        var stored = await Vault().WriteAsync(fileId, Guid.NewGuid(), Content, default);

        var onDisk = await File.ReadAllBytesAsync(Path.Combine(_root, stored.StoragePath));
        Assert.Equal(-1, onDisk.AsSpan().IndexOf("DUPONT"u8));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Content)), stored.Sha256);
        Assert.Equal(Content.Length, stored.SizeBytes);
        Assert.Equal(Content, await Vault().ReadAsync(stored.StoragePath, fileId, default));
        Assert.False(File.Exists(Path.Combine(_root, stored.StoragePath + ".tmp")));
    }

    [Fact]
    public async Task Tampered_swapped_or_wrongly_keyed_files_are_refused()
    {
        var fileId = Guid.NewGuid();
        var stored = await Vault().WriteAsync(fileId, Guid.NewGuid(), Content, default);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => Vault().ReadAsync(stored.StoragePath, Guid.NewGuid(), default));
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => Vault(Convert.ToBase64String(new byte[32])).ReadAsync(stored.StoragePath, fileId, default));

        var path = Path.Combine(_root, stored.StoragePath);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => Vault().ReadAsync(stored.StoragePath, fileId, default));
    }

    [Theory]
    [InlineData("../secret.bin")]
    [InlineData("/etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef/../../x.bin")]
    public async Task Paths_outside_the_storage_are_refused(string path) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Vault().ReadAsync(path, Guid.NewGuid(), default));

    [Fact]
    public async Task Delete_removes_the_file_and_its_empty_folder()
    {
        var stored = await Vault().WriteAsync(Guid.NewGuid(), Guid.NewGuid(), Content, default);

        Vault().Delete(stored.StoragePath);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/tmp/x", "trop-court")]
    public void Missing_configuration_is_refused(string path, string key) =>
        Assert.Throws<InvalidOperationException>(() => new EncryptedFileVault(Options.Create(new StorageOptions { Path = path, EncryptionKey = key })));
}

public class GotenbergPdfConverterTests
{
    private sealed class Handler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        }
    }

    private static GotenbergPdfConverter Converter(Handler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://gotenberg.test:3000/") });

    [Fact]
    public async Task Html_and_footer_are_sent_as_a4()
    {
        var handler = new Handler(HttpStatusCode.OK, "%PDF-1.7"u8.ToArray());

        var pdf = await Converter(handler).ConvertAsync(new PdfHtml("<p>corps</p>", "<p>pied</p>"), default);

        Assert.Equal("%PDF-1.7"u8.ToArray(), pdf);
        Assert.Equal("/forms/chromium/convert/html", handler.Path);
        Assert.Contains("filename=index.html", handler.Body);
        Assert.Contains("filename=footer.html", handler.Body);
        Assert.Contains("<p>corps</p>", handler.Body);
        Assert.Contains("8.27", handler.Body);
        Assert.Contains("11.7", handler.Body);
    }

    [Fact]
    public async Task Error_responses_and_non_pdf_bodies_fail_without_quoting_the_body()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Converter(new Handler(HttpStatusCode.ServiceUnavailable, "détail DUPONT"u8.ToArray())).ConvertAsync(new PdfHtml("", ""), default));
        Assert.DoesNotContain("DUPONT", error.Message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Converter(new Handler(HttpStatusCode.OK, "<html>"u8.ToArray())).ConvertAsync(new PdfHtml("", ""), default));
    }
}

public class PdfStoreAndDispatcherTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedSubmissionAsync(string slug)
    {
        await using var db = fixture.CreateContext();
        var form = await TestData.AddPublishedFormAsync(db, slug);
        var submission = form.CreateSubmission($"TST-2026-{Random.Shared.Next(1000, 9999)}", """{"nom":"FICTIF"}""", Now, 0);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private static SubmissionFile Pdf(Guid submissionId) =>
        new(Guid.NewGuid(), submissionId, SubmissionFileKind.Pdf, $"{submissionId:N}/{Guid.NewGuid():N}.bin", new string('a', 64), 10, Now);

    [Fact]
    public async Task Regenerated_pdf_replaces_the_previous_one()
    {
        var submissionId = await SeedSubmissionAsync("pdf-remplacement");
        var first = Pdf(submissionId);
        var second = Pdf(submissionId);

        await using (var db = fixture.CreateContext())
        {
            var store = new SubmissionPdfStore(db);
            Assert.Empty(await store.ReplacePdfAsync(first, default));
            await store.SaveChangesAsync(default);
        }

        await using (var db = fixture.CreateContext())
        {
            var store = new SubmissionPdfStore(db);
            Assert.Equal([first.StoragePath], await store.ReplacePdfAsync(second, default));
            await store.SaveChangesAsync(default);
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(second.Id, (await check.SubmissionFiles.SingleAsync(f => f.SubmissionId == submissionId)).Id);
    }

    [Fact]
    public async Task Discarded_file_changes_leave_the_job_tracked()
    {
        var submissionId = await SeedSubmissionAsync("pdf-abandon");
        Guid jobId;
        await using (var db = fixture.CreateContext())
        {
            var job = new SubmissionJob(submissionId, JobType.Pdf, Now);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await using (var db = fixture.CreateContext())
        {
            var store = new SubmissionPdfStore(db);
            var job = (await store.FindJobAsync(jobId, default))!;
            await store.ReplacePdfAsync(Pdf(submissionId), default);

            store.DiscardFileChanges();
            job.RecordFailure("HttpRequestException", Now);
            await store.SaveChangesAsync(default);
        }

        await using var check = fixture.CreateContext();
        Assert.False(await check.SubmissionFiles.AnyAsync(f => f.SubmissionId == submissionId));
        Assert.Equal(1, (await check.Jobs.SingleAsync(j => j.Id == jobId)).Attempts);
    }

    [Fact]
    public async Task Source_includes_version_and_settings()
    {
        var submissionId = await SeedSubmissionAsync("pdf-source");
        await using (var db = fixture.CreateContext())
        {
            if (await db.Settings.FindAsync("cse.name") is null)
            {
                db.Settings.Add(new CseForms.Domain.Settings.Setting("cse.name", "CSE fictif"));
                await db.SaveChangesAsync();
            }
        }

        await using var context = fixture.CreateContext();
        var source = await new SubmissionPdfStore(context).GetSourceAsync(submissionId, default);

        Assert.NotNull(source);
        Assert.Equal(1, source.VersionNumber);
        Assert.Equal("CSE fictif", source.Settings["cse.name"]);
        Assert.Contains("FICTIF", source.DataJson);
    }

    [Fact]
    public async Task Source_includes_the_pdf_template_of_the_filled_version()
    {
        Guid before;
        Guid after;
        await using (var db = fixture.CreateContext())
        {
            var form = await TestData.AddPublishedFormAsync(db, "pdf-modele");
            var first = form.CreateSubmission($"TST-2026-{Random.Shared.Next(1000, 9999)}", """{"nom":"FICTIF"}""", Now, 0);
            db.Submissions.Add(first);
            await db.SaveChangesAsync();

            form.Publish(TestData.SimpleDefinition, null, "<p>{{ field.nom }}</p>", Now, "test");
            await db.SaveChangesAsync();
            var second = form.CreateSubmission($"TST-2026-{Random.Shared.Next(1000, 9999)}", """{"nom":"FICTIF"}""", Now, 1);
            db.Submissions.Add(second);
            await db.SaveChangesAsync();

            before = first.Id;
            after = second.Id;
        }

        await using var context = fixture.CreateContext();
        var store = new SubmissionPdfStore(context);
        var old = await store.GetSourceAsync(before, default);
        var current = await store.GetSourceAsync(after, default);

        // Chaque réponse garde le modèle de la version qu'elle a remplie.
        Assert.Equal((1, null), (old!.VersionNumber, old.PdfTemplateHtml));
        Assert.Equal((2, "<p>{{ field.nom }}</p>"), (current!.VersionNumber, current.PdfTemplateHtml));
    }

    [Fact]
    public async Task Pending_jobs_are_dispatched_once()
    {
        var submissionId = await SeedSubmissionAsync("pdf-releve");
        await using (var db = fixture.CreateContext())
        {
            db.Jobs.Add(new SubmissionJob(submissionId, JobType.Pdf, Now));
            await db.SaveChangesAsync();
        }

        var enqueued = new List<Guid>();
        await using (var db = fixture.CreateContext())
        {
            await new PendingJobDispatcher(db, TimeProvider.System).DispatchAsync(j => enqueued.Add(j.SubmissionId), default);
        }

        await using (var db = fixture.CreateContext())
        {
            await new PendingJobDispatcher(db, TimeProvider.System).DispatchAsync(j => enqueued.Add(j.SubmissionId), default);
            Assert.Equal(JobStatus.Queued, (await db.Jobs.SingleAsync(j => j.SubmissionId == submissionId)).Status);
        }

        Assert.Equal(1, enqueued.Count(id => id == submissionId));
    }

    [Fact]
    public async Task Failed_enqueue_leaves_jobs_pending()
    {
        var submissionId = await SeedSubmissionAsync("pdf-echec-file");
        await using (var db = fixture.CreateContext())
        {
            db.Jobs.Add(new SubmissionJob(submissionId, JobType.Pdf, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PendingJobDispatcher(db, TimeProvider.System).DispatchAsync(_ => throw new InvalidOperationException(), default));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(JobStatus.Pending, (await check.Jobs.SingleAsync(j => j.SubmissionId == submissionId)).Status);
    }
}
