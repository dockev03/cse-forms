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

        submission.ChangeStatus(" entered ");
        Assert.Equal("entered", submission.Status);

        Assert.Throws<DomainException>(() => submission.ChangeStatus(" "));
    }
}
