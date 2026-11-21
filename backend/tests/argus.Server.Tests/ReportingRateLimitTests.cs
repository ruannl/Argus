using argus.Server.Security;

namespace argus.Server.Tests;

/// <summary>
/// The process-local abuse buckets added for reports. They are deliberately
/// in-memory limits, not distributed correctness locks.
/// </summary>
public sealed class ReportingRateLimitTests
{
    [Fact]
    public void ReportReadsAllowOneHundredAndTwentyPerCustomerWindow()
    {
        var limiter = new RateLimiter();
        Guid customer = Guid.NewGuid();

        for (int index = 0; index < 120; index++)
        {
            Assert.True(limiter.CheckReportRead(customer));
        }
        Assert.False(limiter.CheckReportRead(customer));
        Assert.True(limiter.CheckReportRead(Guid.NewGuid()));
    }

    [Fact]
    public void SalaryPreviewsAndMutationsAllowThirtyPerCustomerWindow()
    {
        var limiter = new RateLimiter();
        Guid customer = Guid.NewGuid();

        for (int index = 0; index < 30; index++)
        {
            Assert.True(limiter.CheckSalaryMutation(customer));
        }
        Assert.False(limiter.CheckSalaryMutation(customer));
        Assert.True(limiter.CheckSalaryMutation(Guid.NewGuid()));
    }
}
