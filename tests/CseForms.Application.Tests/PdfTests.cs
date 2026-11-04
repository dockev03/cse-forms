using System.Text.Json.Nodes;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

public class SubmissionPdfRendererTests
{
    // 14:05 UTC le 4 novembre 2026, soit 15:05 à Paris.
    private static readonly DateTimeOffset Submitted = new(2026, 11, 4, 14, 5, 0, TimeSpan.Zero);

    private static PdfSource Source(string dataJson, IReadOnlyDictionary<string, string>? settings = null)
    {
        var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "form-engine", "inscription-fictive.json")))!["definition"]!;
        return new PdfSource(Guid.NewGuid(), "Fiche d'inscription au CSE", "INS-2026-0042", 3, Submitted, definition.ToJsonString(), dataJson,
            settings ?? new Dictionary<string, string>());
    }

    [Fact]
    public void Document_shows_case_number_date_and_online_mention()
    {
        var html = SubmissionPdfRenderer.Render(Source("""{"nom":"FICTIF"}""")).Body;

        Assert.Contains("INS-2026-0042", html);
        Assert.Contains("04/11/2026 à 15:05", html);
        Assert.Contains("Formulaire soumis en ligne", html);
        Assert.Contains("<h1>Fiche d&#x27;inscription au CSE</h1>", html);
    }

    [Fact]
    public void Entered_values_are_encoded_never_interpreted()
    {
        var html = SubmissionPdfRenderer.Render(Source("""{"nom":"<script>alert(1)</script>","prenom":"<img src=x onerror=y>"}""")).Body;

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Document_loads_nothing_from_outside()
    {
        var pdf = SubmissionPdfRenderer.Render(Source("""{"nom":"FICTIF"}"""));

        foreach (var html in new[] { pdf.Body, pdf.Footer })
        {
            Assert.Contains("default-src 'none'", html);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", html.Replace("http-equiv", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Children_are_rendered_as_a_table()
    {
        var html = SubmissionPdfRenderer.Render(Source("""
            {"enfants":[{"nom":"FICTIF","prenom":"Léa","naissance":"2020-05-01","sexe":"F"},{"nom":"FICTIF","prenom":"Tom","naissance":"2022-01-15"}]}
            """)).Body;

        Assert.Contains("<table class=\"grid\">", html);
        Assert.Contains("<td>01/05/2020</td>", html);
        Assert.Contains("<td>Tom</td>", html);
    }

    [Fact]
    public void Office_details_come_from_settings()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.CseName] = "CSE fictif",
            [SettingKeys.CseEmail] = "cse@example.test",
            [SettingKeys.CseOffice] = "Secrétaire : Prénom Fictif",
        };

        var html = SubmissionPdfRenderer.Render(Source("""{"nom":"FICTIF"}""", settings)).Body;

        Assert.Contains("CSE fictif", html);
        Assert.Contains("cse@example.test", html);
        Assert.Contains("Bureau du CSE", html);
        Assert.Contains("Secrétaire : Prénom Fictif", html);
    }

    [Fact]
    public void Footer_carries_case_number_and_page_numbers()
    {
        var footer = SubmissionPdfRenderer.Render(Source("{}")).Footer;

        Assert.Contains("Dossier INS-2026-0042", footer);
        Assert.Contains("class=\"pageNumber\"", footer);
        Assert.Contains("class=\"totalPages\"", footer);
    }
}

public class SubmissionPdfServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly FakeVault _vault = new();
    private readonly FakeConverter _converter = new();

    private SubmissionPdfService Service() =>
        new(_store, _vault, _converter, new FixedTimeProvider(Now), NullLogger<SubmissionPdfService>.Instance);

    [Fact]
    public async Task Successful_job_stores_the_pdf_and_removes_the_previous_one()
    {
        _store.ExistingPaths.Add("ancien");

        var result = await Service().RunJobAsync(_store.Job.Id, default);

        Assert.Equal(JobRunResult.Done, result);
        Assert.Equal(JobStatus.Succeeded, _store.Job.Status);
        var file = Assert.Single(_store.SavedFiles);
        Assert.Equal(SubmissionFileKind.Pdf, file.Kind);
        Assert.Equal(_store.Job.SubmissionId, file.SubmissionId);
        Assert.Equal(["ancien"], _vault.Deleted);
        Assert.Equal(["%PDF-fictif"], _vault.Written.Select(w => System.Text.Encoding.ASCII.GetString(w)));
    }

    [Fact]
    public async Task Failure_asks_for_a_retry_until_the_third_attempt()
    {
        _converter.Fail = true;

        Assert.Equal(JobRunResult.Retry, await Service().RunJobAsync(_store.Job.Id, default));
        Assert.Equal(JobRunResult.Retry, await Service().RunJobAsync(_store.Job.Id, default));
        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job.Id, default));

        Assert.Equal(JobStatus.Failed, _store.Job.Status);
        Assert.Equal(3, _store.Job.Attempts);
        Assert.Equal(nameof(HttpRequestException), _store.Job.LastError);
        Assert.Empty(_store.SavedFiles);
    }

    [Fact]
    public async Task Written_file_is_deleted_when_saving_fails()
    {
        _store.FailNextSave = true;

        var result = await Service().RunJobAsync(_store.Job.Id, default);

        Assert.Equal(JobRunResult.Retry, result);
        Assert.Single(_vault.Written);
        Assert.Equal(["nouveau"], _vault.Deleted);
        Assert.Equal(1, _store.Discards);
        Assert.Equal(1, _store.Job.Attempts);
    }

    [Fact]
    public async Task Finished_or_missing_jobs_are_skipped()
    {
        _store.Job.MarkSucceeded(Now);

        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job.Id, default));
        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(Guid.NewGuid(), default));
        Assert.Equal(0, _converter.Calls);
    }

    private sealed class FakeStore : ISubmissionPdfStore
    {
        public SubmissionJob Job { get; } = new(Guid.NewGuid(), JobType.Pdf, Now);

        public List<string> ExistingPaths { get; } = [];

        public List<SubmissionFile> SavedFiles { get; } = [];

        public bool FailNextSave { get; set; }

        public int Discards { get; private set; }

        private SubmissionFile? _pending;

        public Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(jobId == Job.Id ? Job : null);

        public Task<PdfSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult<PdfSource?>(new PdfSource(
                submissionId, "Formulaire fictif", "TST-2026-0001", 1, Now,
                """{"sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]}""", """{"nom":"FICTIF"}""",
                new Dictionary<string, string>()));

        public Task<IReadOnlyList<string>> ReplacePdfAsync(SubmissionFile file, CancellationToken cancellationToken)
        {
            _pending = file;
            return Task.FromResult<IReadOnlyList<string>>(ExistingPaths.ToList());
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Base indisponible.");
            }

            if (_pending is not null)
            {
                SavedFiles.Add(_pending);
                _pending = null;
            }

            return Task.CompletedTask;
        }

        public void DiscardFileChanges()
        {
            Discards++;
            _pending = null;
        }
    }

    private sealed class FakeVault : IFileVault
    {
        public List<byte[]> Written { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<StoredFile> WriteAsync(Guid fileId, Guid submissionId, byte[] content, CancellationToken cancellationToken)
        {
            Written.Add(content);
            return Task.FromResult(new StoredFile("nouveau", new string('a', 64), content.Length));
        }

        public Task<byte[]> ReadAsync(string storagePath, Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);
    }

    private sealed class FakeConverter : IPdfConverter
    {
        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public Task<byte[]> ConvertAsync(PdfHtml html, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail
                ? throw new HttpRequestException("Gotenberg indisponible.")
                : Task.FromResult("%PDF-fictif"u8.ToArray());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
