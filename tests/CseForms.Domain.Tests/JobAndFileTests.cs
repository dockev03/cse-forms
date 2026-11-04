using CseForms.Domain.Common;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;

namespace CseForms.Domain.Tests;

public class SubmissionJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_job_is_pending_and_can_be_queued_once()
    {
        var job = new SubmissionJob(Guid.NewGuid(), JobType.Pdf, Now);
        Assert.Equal(JobStatus.Pending, job.Status);

        job.MarkQueued(Now);

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Throws<DomainException>(() => job.MarkQueued(Now));
    }

    [Fact]
    public void Third_failure_is_final_and_error_is_truncated()
    {
        var job = new SubmissionJob(Guid.NewGuid(), JobType.Pdf, Now);

        Assert.True(job.RecordFailure("HttpRequestException", Now));
        Assert.True(job.RecordFailure("HttpRequestException", Now));
        Assert.False(job.RecordFailure(new string('x', 500), Now));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.True(job.IsFinished);
        Assert.Equal(SubmissionJob.ErrorMaxLength, job.LastError!.Length);
    }

    [Fact]
    public void Success_clears_the_last_error()
    {
        var job = new SubmissionJob(Guid.NewGuid(), JobType.Pdf, Now);
        job.RecordFailure("HttpRequestException", Now);

        job.MarkSucceeded(Now);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.LastError);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public void Dates_must_be_utc() =>
        Assert.Throws<DomainException>(() => new SubmissionJob(Guid.NewGuid(), JobType.Pdf, new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(1))));
}

public class SubmissionFileTests
{
    [Theory]
    [InlineData("", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("a/b.bin", "pas-une-empreinte")]
    [InlineData("a/b.bin", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Invalid_path_or_hash_is_refused(string path, string sha256) =>
        Assert.Throws<DomainException>(() =>
            new SubmissionFile(Guid.NewGuid(), Guid.NewGuid(), SubmissionFileKind.Pdf, path, sha256, 1, DateTimeOffset.UnixEpoch));

    [Fact]
    public void Hash_is_stored_in_lower_case()
    {
        var file = new SubmissionFile(Guid.NewGuid(), Guid.NewGuid(), SubmissionFileKind.Pdf, "a/b.bin", new string('A', 64), 1, DateTimeOffset.UnixEpoch);

        Assert.Equal(new string('a', 64), file.Sha256);
    }
}
