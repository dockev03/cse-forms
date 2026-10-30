using CseForms.Domain.Common;
using CseForms.Domain.Forms;
using CseForms.Domain.Submissions;

namespace CseForms.Domain.Tests;

public class CaseNumberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("INS", 2026, 42, "INS-2026-0042")]
    [InlineData(" ins ", 2026, 1, "INS-2026-0001")]
    [InlineData("SORTIE", 2027, 12345, "SORTIE-2027-12345")]
    public void Case_number_is_formatted(string prefix, int year, long sequence, string expected) =>
        Assert.Equal(expected, CaseNumber.Format(prefix, year, sequence));

    [Theory]
    [InlineData("I")]
    [InlineData("TROPLONG")]
    [InlineData("IN5")]
    [InlineData("")]
    public void Invalid_prefix_is_rejected(string prefix) =>
        Assert.Throws<DomainException>(() => CaseNumber.Format(prefix, 2026, 1));

    [Fact]
    public void Invalid_sequence_is_rejected() =>
        Assert.Throws<DomainException>(() => CaseNumber.Format("INS", 2026, 0));

    [Fact]
    public void Form_has_default_prefix_that_can_be_changed()
    {
        var form = new Form("inscription", "Titre", Now);
        Assert.Equal(Form.DefaultCaseNumberPrefix, form.CaseNumberPrefix);

        form.SetCaseNumberPrefix("ins");
        Assert.Equal("INS", form.CaseNumberPrefix);
    }

    [Fact]
    public void Submission_for_an_outdated_version_is_rejected()
    {
        var form = new Form("inscription", "Titre", Now);
        var v1 = form.Publish("""{"sections":[]}""", null, null, Now, null);
        form.Publish("""{"sections":[]}""", null, null, Now, null);

        Assert.Throws<DomainException>(() => form.CreateSubmission(v1.Id, "INS-2026-0001", "{}", Now, 0));
    }
}
