using CseForms.Domain.Common;
using CseForms.Domain.Forms;

namespace CseForms.Domain.Tests;

public class FormTests
{
    private const string Schema = """{"fields":[]}""";
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_form_is_draft_without_version()
    {
        var form = new Form("inscription", "Fiche d'inscription", Now);

        Assert.Equal(FormStatus.Draft, form.Status);
        Assert.Null(form.CurrentVersionId);
        Assert.Empty(form.Versions);
    }

    [Theory]
    [InlineData("  Inscription ", "inscription")]
    [InlineData("arbre-de-noel-2026", "arbre-de-noel-2026")]
    public void Slug_is_normalized(string input, string expected) =>
        Assert.Equal(expected, new Form(input, "Titre", Now).Slug);

    [Theory]
    [InlineData("")]
    [InlineData("avec espace")]
    [InlineData("accentué")]
    [InlineData("-debut")]
    [InlineData("fin-")]
    [InlineData("double--tiret")]
    [InlineData("../etc")]
    public void Invalid_slug_is_rejected(string slug) =>
        Assert.Throws<DomainException>(() => new Form(slug, "Titre", Now));

    [Fact]
    public void Non_utc_date_is_rejected() =>
        Assert.Throws<DomainException>(() => new Form("inscription", "Titre", new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.FromHours(1))));

    [Fact]
    public void Publish_creates_first_version_and_publishes_form()
    {
        var form = new Form("inscription", "Titre", Now);

        var version = form.Publish(Schema, null, null, Now, "editeur");

        Assert.Equal(1, version.Number);
        Assert.Equal(form.Id, version.FormId);
        Assert.Equal(version.Id, form.CurrentVersionId);
        Assert.Equal(FormStatus.Published, form.Status);
    }

    [Fact]
    public void Publishing_again_creates_new_version_and_keeps_previous_one()
    {
        var form = new Form("inscription", "Titre", Now);
        var v1 = form.Publish(Schema, null, null, Now, "editeur");

        var v2 = form.Publish("""{"fields":[{"id":"nom"}]}""", null, null, Now.AddDays(1), "editeur");

        Assert.Equal(2, v2.Number);
        Assert.Equal(v2.Id, form.CurrentVersionId);
        Assert.Equal(2, form.Versions.Count);
        Assert.Equal(Schema, v1.SchemaJson);
    }

    [Fact]
    public void Publish_requires_schema() =>
        Assert.Throws<DomainException>(() => new Form("inscription", "Titre", Now).Publish(" ", null, null, Now, null));

    [Fact]
    public void Submission_references_version_in_use_at_submission_time()
    {
        var form = new Form("inscription", "Titre", Now);
        var v1 = form.Publish(Schema, null, null, Now, null);
        var first = form.CreateSubmission("INS-2026-0001", "{}", Now, 0);

        var v2 = form.Publish(Schema, null, null, Now.AddHours(1), null);
        var second = form.CreateSubmission("INS-2026-0002", "{}", Now.AddHours(2), 1);

        Assert.Equal(v1.Id, first.FormVersionId);
        Assert.Equal(v2.Id, second.FormVersionId);
    }

    [Fact]
    public void Draft_form_does_not_accept_submission()
    {
        var form = new Form("inscription", "Titre", Now);

        Assert.False(form.IsAcceptingResponses(Now, 0));
        Assert.Throws<DomainException>(() => form.CreateSubmission("INS-2026-0001", "{}", Now, 0));
    }

    [Fact]
    public void Closed_form_does_not_accept_submission_until_reopened()
    {
        var form = new Form("inscription", "Titre", Now);
        form.Publish(Schema, null, null, Now, null);

        form.Close();
        Assert.False(form.IsAcceptingResponses(Now, 0));

        form.Reopen();
        Assert.True(form.IsAcceptingResponses(Now, 0));
    }

    [Fact]
    public void Schedule_limits_accepted_period()
    {
        var form = new Form("inscription", "Titre", Now);
        form.Publish(Schema, null, null, Now, null);
        form.SetSchedule(Now.AddDays(1), Now.AddDays(10));

        Assert.False(form.IsAcceptingResponses(Now, 0));
        Assert.True(form.IsAcceptingResponses(Now.AddDays(1), 0));
        Assert.False(form.IsAcceptingResponses(Now.AddDays(10), 0));
    }

    [Fact]
    public void Closing_date_must_follow_opening_date() =>
        Assert.Throws<DomainException>(() => new Form("inscription", "Titre", Now).SetSchedule(Now, Now));

    [Fact]
    public void Max_responses_is_enforced()
    {
        var form = new Form("sortie", "Sortie", Now);
        form.Publish(Schema, null, null, Now, null);
        form.SetMaxResponses(20);

        Assert.True(form.IsAcceptingResponses(Now, 19));
        Assert.False(form.IsAcceptingResponses(Now, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Max_responses_and_retention_must_be_positive(int value)
    {
        var form = new Form("sortie", "Sortie", Now);

        Assert.Throws<DomainException>(() => form.SetMaxResponses(value));
        Assert.Throws<DomainException>(() => form.SetRetentionDays(value));
    }

    [Fact]
    public void New_submission_is_never_scheduled_for_purge()
    {
        var form = new Form("inscription", "Titre", Now);
        form.Publish(Schema, null, null, Now, null);
        form.SetRetentionDays(90);

        Assert.Null(form.CreateSubmission("INS-2026-0001", "{}", Now, 0).PurgeAt);
    }
}
