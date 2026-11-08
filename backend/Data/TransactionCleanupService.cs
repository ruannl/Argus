using Npgsql;

namespace argus.Server.Data;

/// <summary>
/// Expires prepared transaction operations at startup and every fifteen
/// minutes. It locks headers only, never then acquires account or customer
/// locks, and retains compact terminal receipts without payloads.
/// </summary>
public sealed class TransactionCleanupService(TransactionStore store, ILogger<TransactionCleanupService> logger) : BackgroundService
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
            catch (PostgresException exception)
            {
                logger.LogError("transaction_cleanup_failed error_class={ErrorClass} sql_state={SqlState}", "postgres", exception.SqlState);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "transaction_cleanup_failed error_class={ErrorClass}", exception.GetType().Name);
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
