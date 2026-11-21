namespace argus.Server.Reporting;

/// <summary>
/// Reporting-only wall clock. Imported transaction dates are already calendar
/// dates and are never converted; only "today" and the current month use this
/// clock. Authentication, import validation and database expiry keep their
/// existing clocks.
/// </summary>
public static class ReportingClock
{
    public const string TimeZoneId = "Africa/Johannesburg";

    public static DateOnly Today(TimeProvider timeProvider)
    {
        DateTimeOffset utcNow = timeProvider.GetUtcNow();
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ReportingConfigurationException($"reporting timezone '{TimeZoneId}' is not available", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ReportingConfigurationException($"reporting timezone '{TimeZoneId}' is invalid", exception);
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(utcNow, zone);

        return DateOnly.FromDateTime(local.DateTime);
    }
}

public sealed class ReportingConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
