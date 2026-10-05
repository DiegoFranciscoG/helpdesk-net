using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Catalog;
using Helpdesk.Application.Suggestions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helpdesk.Infrastructure.Background;

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    /// <summary>Permite apagar los servicios en segundo plano (pruebas de integración los invocan a mano).</summary>
    public bool Enabled { get; set; } = true;

    public int SlaMonitorIntervalSeconds { get; set; } = 30;

    /// <summary>Espera antes de reintentar una sugerencia fallida (p. ej. P27 despertando en Render).</summary>
    public int SuggestionRetryDelaySeconds { get; set; } = 90;
}

/// <summary>Bandeja acotada en memoria: si se llena se descarta lo más antiguo (el agente puede pedir otra sugerencia).</summary>
internal sealed class ChannelSuggestionInbox : ISuggestionInbox
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public void Enqueue(Guid ticketId) => _channel.Writer.TryWrite(ticketId);

    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return id;
        }
    }
}

/// <summary>Evalúa avisos e incumplimientos de SLA cada N segundos (R8) y cierra tickets resueltos vencidos (R9).</summary>
internal sealed partial class SlaMonitorWorker(IServiceScopeFactory scopes, IOptions<WorkerOptions> options, ILogger<SlaMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.SlaMonitorIntervalSeconds)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var changed = await scope.ServiceProvider.GetRequiredService<SlaMonitorService>().RunOnceAsync(stoppingToken);
                if (changed > 0)
                {
                    LogEvaluated(logger, changed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitor de SLA: {Count} tickets con avisos, incumplimientos o cierre automático")]
    private static partial void LogEvaluated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló una pasada del monitor de SLA")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Procesa la bandeja de sugerencias llamando a P27; al arrancar recupera tickets recientes sin sugerencia.</summary>
internal sealed partial class SuggestionWorker(
    IServiceScopeFactory scopes,
    ISuggestionInbox inbox,
    INlpClassifier classifier,
    IOptions<WorkerOptions> options,
    TimeProvider clock,
    ILogger<SuggestionWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 2;
    private readonly Dictionary<Guid, int> _attempts = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!classifier.IsEnabled)
        {
            LogDisabled(logger);
            return;
        }

        await BackfillAsync(stoppingToken);
        await foreach (var ticketId in inbox.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var created = await scope.ServiceProvider.GetRequiredService<SuggestionService>().ProcessAsync(ticketId, stoppingToken);
                if (!created)
                {
                    ScheduleRetry(ticketId, stoppingToken);
                }
                else
                {
                    _attempts.Remove(ticketId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, ticketId);
            }
        }
    }

    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var pending = await scope.ServiceProvider.GetRequiredService<ITicketRepository>()
                .GetUnclassifiedWithoutSuggestionAsync(clock.GetUtcNow().AddDays(-7), 50, cancellationToken);
            foreach (var id in pending)
            {
                inbox.Enqueue(id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, Guid.Empty);
        }
    }

    private void ScheduleRetry(Guid ticketId, CancellationToken cancellationToken)
    {
        var attempts = _attempts.GetValueOrDefault(ticketId) + 1;
        if (attempts >= MaxAttempts)
        {
            _attempts.Remove(ticketId);
            return;
        }

        _attempts[ticketId] = attempts;
        _ = Task.Delay(TimeSpan.FromSeconds(options.Value.SuggestionRetryDelaySeconds), clock, cancellationToken)
            .ContinueWith(_ => inbox.Enqueue(ticketId), cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Integración con el clasificador desactivada (sin Nlp__BaseUrl)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo procesar la sugerencia del ticket {TicketId}")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid ticketId);
}
