using argus.Server.Security;

namespace argus.Server.Tests;

/// <summary>
/// The process-local import abuse bucket. Raising the limit does not weaken
/// parser concurrency or the file/row/page bounds.
/// </summary>
public sealed class ImportRateLimitTests
{
    [Fact]
    public void InspectAndPrepareAllowSixtyPerCustomerWindow()
    {
        var limiter = new RateLimiter();
        Guid customer = Guid.NewGuid();

        for (int index = 0; index < 60; index++)
        {
            Assert.True(limiter.CheckImportCustomer(customer));
        }
        Assert.False(limiter.CheckImportCustomer(customer));
        Assert.True(limiter.CheckImportCustomer(Guid.NewGuid()));
    }
}
