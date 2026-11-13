using CseForms.Domain.Common;
using CseForms.Domain.Forms;
using CseForms.Domain.Submissions;

namespace CseForms.Domain.Tests;

public class SubmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    private static Form PublishedForm()
    {
        var form = new Form("inscription", "Titre", Now);
        form.Publish("""{"fields":[]}""", null, null, Now, null);
        return form;
    }

    [Fact]
    public void New_submission_has_initial_status()
    {
        var submission = PublishedForm().CreateSubmission("INS-2026-0001", "{}", Now, 0);

        Assert.Equal(Submission.InitialStatus, submission.Status);
        Assert.Equal("INS-2026-0001", submission.CaseNumber);
        Assert.Equal(Now, submission.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INS-2026-000000000000000000000001")]
    public void Invalid_case_number_is_rejected(string caseNumber) =>
        Assert.Throws<DomainException>(() => PublishedForm().CreateSubmission(caseNumber, "{}", Now, 0));

    [Fact]
    public void Data_is_required() =>
        Assert.Throws<DomainException>(() => PublishedForm().CreateSubmission("INS-2026-0001", "", Now, 0));

    [Fact]
    public void Status_change_is_trimmed_and_validated()
    {
        var submission = PublishedForm().CreateSubmission("INS-2026-0001", "{}", Now, 0);

        Assert.True(submission.ChangeStatus(" entered ", Now, null));
        Assert.Equal("entered", submission.Status);
        Assert.False(submission.ChangeStatus("entered", Now, null));

        Assert.Throws<DomainException>(() => submission.ChangeStatus(" ", Now, null));
        Assert.Throws<DomainException>(() => submission.ChangeStatus("archived", Now, 0));
        Assert.Throws<DomainException>(() => submission.ChangeStatus("archived", new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.FromHours(1)), null));
    }

    [Fact]
    public void Retention_starts_when_the_submission_is_processed()
    {
        var submission = PublishedForm().CreateSubmission("INS-2026-0001", "{}", Now, 0);
        Assert.Null(submission.PurgeAt);

        var entered = Now.AddDays(10);
        submission.ChangeStatus("entered", entered, 180);
        Assert.Equal(entered.AddDays(180), submission.PurgeAt);

        // Archiver ensuite ne prolonge pas la conservation.
        submission.ChangeStatus("archived", entered.AddDays(30), 180);
        Assert.Equal(entered.AddDays(180), submission.PurgeAt);
    }

    [Fact]
    public void Back_to_initial_status_cancels_the_purge()
    {
        var submission = PublishedForm().CreateSubmission("INS-2026-0001", "{}", Now, 0);
        submission.ChangeStatus("entered", Now, 180);

        submission.ChangeStatus(Submission.InitialStatus, Now.AddDays(1), 180);
        Assert.Null(submission.PurgeAt);

        submission.ChangeStatus("entered", Now.AddDays(2), 180);
        Assert.Equal(Now.AddDays(182), submission.PurgeAt);
    }

    [Fact]
    public void Without_retention_no_purge_is_scheduled()
    {
        var submission = PublishedForm().CreateSubmission("INS-2026-0001", "{}", Now, 0);

        submission.ChangeStatus("archived", Now, null);

        Assert.Null(submission.PurgeAt);
    }
}
