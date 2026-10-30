using System.Text.Json.Nodes;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;

namespace CseForms.Application.Tests;

public class SubmissionPresenterTests
{
    private static FormDefinition Definition()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "form-engine", "inscription-fictive.json");
        var fixture = JsonNode.Parse(File.ReadAllText(path))!;
        return FormDefinitionParser.Parse(fixture["definition"]!.ToJsonString()).Definition!;
    }

    [Fact]
    public void Values_are_presented_with_labels_and_french_formats()
    {
        var data = JsonNode.Parse("""
            {"civilite":"Mme","nom":"DUPONT","contrat":"CDD","dateEmbauche":"2026-09-01","dateFin":"2027-03-01",
             "adresse":{"street":"1 rue Fictive","postalCode":"63200","city":"RIOM"},
             "enfants":[{"nom":"DUPONT","prenom":"Léa","naissance":"2020-05-01"}],
             "attestation":true,"rgpdLu":false}
            """)!.AsObject();

        var sections = SubmissionPresenter.Present(Definition(), data);
        var values = sections.SelectMany(s => s.Items).OfType<PresentedValue>().ToDictionary(v => v.Label, v => v.Value);

        Assert.Equal("DUPONT", values["Nom"]);
        Assert.Equal("01/09/2026", values["Date d'embauche"]);
        Assert.Equal("01/03/2027", values["Date de fin"]);
        Assert.Equal("1 rue Fictive, 63200 RIOM", values["Adresse"]);
        Assert.Equal("Oui", values["J'atteste l'exactitude des informations"]);
        Assert.Equal("Non", values["J'ai lu la notice d'information"]);

        var children = Assert.Single(sections.SelectMany(s => s.Items).OfType<PresentedTable>());
        Assert.Equal(["Nom", "Prénom", "Date de naissance", "Sexe"], children.Headers);
        Assert.Equal(["DUPONT", "Léa", "01/05/2020", ""], Assert.Single(children.Rows));
    }

    [Fact]
    public void Absent_fields_and_empty_sections_are_not_presented()
    {
        var sections = SubmissionPresenter.Present(Definition(), JsonNode.Parse("""{"nom":"DUPONT"}""")!.AsObject());

        var section = Assert.Single(sections);
        Assert.Equal("État civil", section.Title);
        Assert.Single(section.Items);
    }

    [Theory]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z")] // passage à l'heure d'été ce jour-là
    [InlineData("2026-07-14", "2026-07-13T22:00:00Z")]
    [InlineData("2026-12-01", "2026-11-30T23:00:00Z")]
    public void Start_of_day_is_computed_in_paris(string day, string expectedUtc) =>
        Assert.Equal(
            DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture),
            CseCalendar.StartOfDayUtc(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)));
}

public class BackOfficeServiceTests
{
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();

    private sealed class FakeStore(bool canAccess) : IBackOfficeStore
    {
        public SubmissionQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(CurrentUser user, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormSummary>>([]);

        public Task<bool> CanAccessFormAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult(canAccess);

        public Task<FormHeader?> GetFormAsync(Guid formId, CancellationToken cancellationToken) =>
            Task.FromResult<FormHeader?>(formId == FormId
                ? new FormHeader(FormId, "f", "F", """{"sections":[{"id":"s","fields":[{"id":"lieu","type":"site","label":"Site"}]}]}""")
                : null);

        public Task<SubmissionPage> ListSubmissionsAsync(SubmissionQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new SubmissionPage([], 0, 1, 1, []));
        }

        public Task<SubmissionRecord?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult<SubmissionRecord?>(submissionId == SubmissionId
                ? new SubmissionRecord(SubmissionId, FormId, "F", "TST-2026-0001", 1, """{"sections":[{"id":"s","fields":[{"id":"nom","type":"text","label":"Nom"}]}]}""", """{"nom":"DUPONT"}""", "new", DateTimeOffset.UnixEpoch, null)
                : null);
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<(string Action, Guid? UserId, string? Target)> Entries { get; } = [];

        public Task RecordAsync(string action, Guid? userId, string? target, CancellationToken cancellationToken)
        {
            Entries.Add((action, userId, target));
            return Task.CompletedTask;
        }
    }

    private static readonly CurrentUser User = new(Guid.NewGuid(), IsAdmin: false);

    [Fact]
    public async Task Viewing_a_submission_is_audited()
    {
        var audit = new FakeAudit();
        var detail = await new BackOfficeService(new FakeStore(canAccess: true), audit).GetSubmissionAsync(User, SubmissionId, default);

        Assert.NotNull(detail);
        Assert.Equal("DUPONT", Assert.IsType<PresentedValue>(detail.Sections[0].Items[0]).Value);
        Assert.Equal((AuditActions.SubmissionViewed, (Guid?)User.Id, $"submission:{SubmissionId}"), Assert.Single(audit.Entries));
    }

    [Fact]
    public async Task Denied_access_looks_like_not_found_and_is_audited()
    {
        var audit = new FakeAudit();
        var service = new BackOfficeService(new FakeStore(canAccess: false), audit);

        Assert.Null(await service.GetSubmissionAsync(User, SubmissionId, default));
        Assert.Null(await service.ListSubmissionsAsync(User, FormId, new SubmissionFilter(), default));
        Assert.All(audit.Entries, e => Assert.Equal(AuditActions.AccessDenied, e.Action));
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Unknown_submission_is_not_audited()
    {
        var audit = new FakeAudit();

        Assert.Null(await new BackOfficeService(new FakeStore(canAccess: true), audit).GetSubmissionAsync(User, Guid.NewGuid(), default));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Filter_is_normalized_and_period_converted_to_utc()
    {
        var store = new FakeStore(canAccess: true);
        var filter = new SubmissionFilter(" tst-2026 ", " new ", " Riom ", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), Page: 0);

        await new BackOfficeService(store, new FakeAudit()).ListSubmissionsAsync(User, FormId, filter, default);

        var query = store.LastQuery!;
        Assert.Equal("TST-2026", query.CaseNumberContains);
        Assert.Equal("new", query.Status);
        Assert.Equal("lieu", query.SiteFieldId);
        Assert.Equal("Riom", query.Site);
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero), query.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero), query.ToUtcExclusive);
        Assert.Equal(1, query.Page);
    }
}
