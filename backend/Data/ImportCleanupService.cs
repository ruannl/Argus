namespace argus.Server.Data;

/// <summary>
/// Purges expired review payloads at startup and every fifteen minutes. It
/// retains only minimal terminal headers and never logs source content.
/// </summary>
public sealed class ImportCleanupService(
    StatementImportStore store,
    ILogger<ImportCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "import_cleanup_failed error_class={ErrorClass}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
