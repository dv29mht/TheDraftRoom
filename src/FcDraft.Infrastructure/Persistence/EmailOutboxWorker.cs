using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Background delivery loop for the durable email outbox. Polls for due messages on a fixed interval
/// and delivers them through <see cref="IEmailOutboxProcessor"/>. Registered only when SQL
/// persistence is configured. Delivery errors are handled inside the processor (retry/backoff); the
/// loop itself only guards against an unexpected fault so one bad cycle never stops the worker.
/// </summary>
public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BrevoOptions> brevoOptions,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Configuration validation: surface a clear warning if the outbox is running without Brevo
        // credentials. Messages still queue durably and deliver once credentials are supplied.
        var brevo = brevoOptions.Value;
        if (string.IsNullOrWhiteSpace(brevo.ApiKey) || string.IsNullOrWhiteSpace(brevo.SenderEmail))
        {
            logger.LogWarning(
                "Email outbox worker started but Brevo is not configured (Brevo:ApiKey/Brevo:SenderEmail). " +
                "Messages will queue and remain pending until credentials are provided.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();
                await processor.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Email outbox delivery cycle failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
