using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FcDraft.API;
using FcDraft.API.Middleware;
using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// PR-22 observability and PWA-hardening proofs: the correlation id flows request → MediatR
/// handler log → response header, every /api response carries the version-handshake contract
/// header and a no-store cache policy, the anonymous version endpoint serves the handshake, and
/// /health reports the self check plus the contract on the in-memory branch.
/// </summary>
public sealed class ObservabilityTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    [Fact]
    public async Task Api_response_echoes_a_wellformed_supplied_correlation_id()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "pr22-proof-correlation-0001");

        var response = await client.GetAsync("/api/meta/version");

        Assert.Equal("pr22-proof-correlation-0001", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task Api_response_generates_a_correlation_id_when_the_supplied_one_is_malformed()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "bad id\twith control chars");

        var response = await client.GetAsync("/api/meta/version");

        var generated = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.Matches("^[0-9a-f]{32}$", generated);
    }

    [Fact]
    public async Task Correlation_id_flows_from_the_request_into_the_handler_pipeline_log()
    {
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "pr22-handler-propagation-01");

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededAccounts.AdminEmail,
            password = SeededAccounts.AdminPassword
        });

        // The MediatR logging behavior sits OUTSIDE the handler: its line proves the id assigned by
        // the HTTP middleware was visible inside the application pipeline for this exact request.
        Assert.Contains(logs.Messages, message =>
            message.Contains("RequestLoggingBehavior")
            && message.Contains("LoginCommand")
            && message.Contains("pr22-handler-propagation-01"));
    }

    [Fact]
    public async Task Authenticated_api_reads_are_stamped_no_store_and_with_the_contract()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(session.AccessToken);

        var response = await client.GetAsync("/api/me/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(
            ApiContract.Version.ToString(),
            Assert.Single(response.Headers.GetValues(ApiContract.HeaderName)));
    }

    [Fact]
    public async Task Login_response_is_stamped_no_store()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededAccounts.AdminEmail,
            password = SeededAccounts.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Unauthenticated_api_responses_still_carry_the_contract_header()
    {
        var response = await factory.CreateClient().GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            ApiContract.Version.ToString(),
            Assert.Single(response.Headers.GetValues(ApiContract.HeaderName)));
    }

    [Fact]
    public async Task Version_endpoint_is_anonymous_and_reports_the_handshake_contract()
    {
        var response = await factory.CreateClient().GetAsync("/api/meta/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fc-draft-api", body.GetProperty("service").GetString());
        Assert.Equal(ApiContract.Version, body.GetProperty("contract").GetInt32());
        Assert.Equal("local", body.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task Health_reports_the_self_check_and_the_contract_on_the_inmemory_branch()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        Assert.Equal(ApiContract.Version, body.GetProperty("contract").GetInt32());
        Assert.Equal("healthy", body.GetProperty("checks").GetProperty("self").GetString());
    }

    [Fact]
    public async Task Unexpected_exceptions_reach_the_error_reporter_with_the_correlation_id()
    {
        var reporter = new RecordingErrorReporter();
        var accessor = new FcDraft.Infrastructure.Observability.CorrelationIdAccessor();
        accessor.Set("pr22-error-report-00000001");
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<GlobalExceptionMiddleware>.Instance,
            reporter,
            accessor);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var (exception, correlationId) = Assert.Single(reporter.Reports);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("pr22-error-report-00000001", correlationId);

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        Assert.Equal("pr22-error-report-00000001", problem.GetProperty("correlationId").GetString());
        Assert.Equal("Please try again.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Expected_app_exceptions_are_not_sent_to_the_error_reporter()
    {
        var reporter = new RecordingErrorReporter();
        var accessor = new FcDraft.Infrastructure.Observability.CorrelationIdAccessor();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new KeyNotFoundException("missing"),
            NullLogger<GlobalExceptionMiddleware>.Instance,
            reporter,
            accessor);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(reporter.Reports);
    }

    private sealed class RecordingErrorReporter : IErrorReporter
    {
        public List<(Exception Exception, string? CorrelationId)> Reports { get; } = [];

        public void Report(Exception exception, string? correlationId = null, string? source = null) =>
            Reports.Add((exception, correlationId));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Add($"{category}: {formatter(state, exception)}");
        }
    }
}
