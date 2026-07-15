using System.Security.Claims;
using System.Text;
using FcDraft.API.Middleware;
using FcDraft.Application;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Bind to the port the hosting platform assigns (Render, Railway, and similar inject PORT) so the
// single container serves on the address the platform routes to. Local development leaves PORT
// unset and keeps the launchSettings URLs.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "The Draft Room API",
        Version = "v1",
        Description = "Private, real-time FC Kick Off tournament drafting API."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = ClaimTypes.Role
        };

        // Re-check the account on every authenticated request so a rotated security stamp (password
        // change/reset, deactivation, admin action, sign-out-everywhere) or a deactivation revokes
        // previously issued tokens immediately, without waiting for the token to expire.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var tokenStamp = principal?.FindFirstValue(DraftClaimTypes.SecurityStamp);
                if (userId is null || !Guid.TryParse(userId, out var id))
                {
                    context.Fail("The token is missing its subject.");
                    return;
                }

                var identity = context.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
                var user = await identity.FindByIdAsync(id, context.HttpContext.RequestAborted);
                if (user is null
                    || user.Status != AccountStatus.Active
                    || !string.Equals(user.SecurityStamp, tokenStamp, StringComparison.Ordinal))
                {
                    context.Fail("The session is no longer valid.");
                }
            }
        };
    });
builder.Services.AddAuthorization();

// The app runs behind the hosting platform's TLS-terminating proxy, so trust the forwarded
// scheme/host headers. This keeps Request.IsHttps reflecting the original request and stops the
// HTTPS redirect below from looping when the edge already served the request over HTTPS. The
// proxy's IP is dynamic, so no known-proxy allowlist is applied.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Bring the database up to date before serving traffic. Registered only when SQL persistence is
// configured, so the in-memory foundation and hermetic tests skip this entirely.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetService<IDatabaseInitializer>();
    if (initializer is not null)
    {
        await initializer.InitializeAsync();
    }
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseForwardedHeaders();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "The Draft Room API v1");
    options.DocumentTitle = "The Draft Room API";
    options.DisplayRequestDuration();
});
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ForcedPasswordChangeMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthResponse });
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/swagger")
        || context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();

// Preserves the original { status, service } shape and adds a per-check breakdown so the database
// health of the running instance is observable. Returns 503 when any check is unhealthy.
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
        service = "fc-draft-api",
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString().ToLowerInvariant())
    });
}

public partial class Program;
