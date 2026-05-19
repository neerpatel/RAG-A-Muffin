using RagAMuffin.Services.Interfaces;

namespace RagAMuffin.Services
{
    public class ConnectorSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory         _scopeFactory;
        private readonly ILogger<ConnectorSyncService> _logger;
        private readonly ConnectorConfigService        _connectorConfig;
        private readonly SyncLogService                _syncLog;
        private readonly int                           _defaultIntervalMinutes;

        public ConnectorSyncService(
            IServiceScopeFactory scopeFactory,
            ILogger<ConnectorSyncService> logger,
            IConfiguration configuration,
            ConnectorConfigService connectorConfig,
            SyncLogService syncLog)
        {
            _scopeFactory   = scopeFactory;
            _logger         = logger;
            _connectorConfig = connectorConfig;
            _syncLog         = syncLog;
            _defaultIntervalMinutes = configuration.GetValue("Ingestion:IntervalMinutes", 60);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ConnectorSyncService started");
            await SyncAllAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var minutes = _connectorConfig.Current.SyncIntervalMinutes > 0
                    ? _connectorConfig.Current.SyncIntervalMinutes
                    : _defaultIntervalMinutes;

                _logger.LogInformation("Next sync in {Minutes}m", minutes);

                try { await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken); }
                catch (OperationCanceledException) { break; }

                await SyncAllAsync(stoppingToken);
            }
        }

        public async Task SyncAllAsync(CancellationToken ct)
        {
            _logger.LogInformation("Connector sync starting...");
            try
            {
                var enabled = _connectorConfig.Current.EnabledConnectors;
                var enabledSet = enabled.Count > 0
                    ? new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase)
                    : null; // null means all enabled

                using var scope = _scopeFactory.CreateScope();
                var connectors = scope.ServiceProvider.GetServices<IConnector>().ToList();
                var pipeline   = scope.ServiceProvider.GetRequiredService<IIngestionPipeline>();

                if (connectors.Count == 0)
                {
                    _logger.LogWarning("No connectors registered — nothing to sync");
                    return;
                }

                foreach (var connector in connectors)
                {
                    if (enabledSet is not null && !enabledSet.Contains(connector.SourceType))
                    {
                        _logger.LogInformation("Skipping disabled connector: {SourceType}", connector.SourceType);
                        continue;
                    }

                    _logger.LogInformation("Running connector: {SourceType}", connector.SourceType);
                    try
                    {
                        var documents = await connector.FetchAsync(ct);
                        await pipeline.IngestAsync(documents, ct);
                        _syncLog.Record(connector.SourceType, "ok", documents.Count());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Connector '{SourceType}' failed", connector.SourceType);
                        _syncLog.Record(connector.SourceType, "error", 0, ex.Message);
                    }
                }

                _logger.LogInformation("Connector sync complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectorSyncService encountered an unexpected error");
            }
        }
    }
}
