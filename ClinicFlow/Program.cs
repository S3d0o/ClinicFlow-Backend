
using ClinicFlow.BackgroundJobs;
using Microsoft.Extensions.Options;
using Persistence.AppData;
using Scalar.AspNetCore;
using Shared.Settings;

Log.Logger = new LoggerConfiguration() // capture startup errors
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ClinicFlow application...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) =>
    {
        config
       .ReadFrom.Configuration(context.Configuration)
       .ReadFrom.Services(services);
    });

    #region Add services to the container.

    builder.Services.AddWebApiServices();
    builder.Services.AddAuthRateLimiting(builder.Configuration);

    builder.Services.AddInfraStructureServices(builder.Configuration);

    builder.Services.AddCoreServices(builder.Configuration);

    builder.Services.AddHostedService<AppointmentReminderJob>();
    builder.Services.AddHostedService<SlotGenerationJob>();

    // Liveness probe for the Docker HEALTHCHECK (GET /health)
    builder.Services.AddHealthChecks();


    #endregion

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();
    var frontendUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("React", policy =>
        {
            policy
                .WithOrigins(frontendUrl.TrimEnd('/'))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                // The browser hides other response headers from a cross-origin frontend: X-Total-Count for
                // paged lists (REVIEW.md NL2), Retry-After for the auth rate limit (contract §2.7)
                .WithExposedHeaders("X-Total-Count", "Retry-After");
        });
    });

    var app = builder.Build();

    // The app runs without email, so missing SMTP settings are a warning rather than a startup failure;
    // without it they only surfaced at the first send (REVIEW.md NL12)
    var missingEmailSettings = app.Services.GetRequiredService<IOptions<EmailSettings>>().Value.MissingSettings();
    if (missingEmailSettings.Count > 0)
        Log.Warning("Email is not configured (missing EmailSettings: {MissingSettings}); confirmation, password-reset and reminder emails will fail to send",
            missingEmailSettings);

    // Demo accounts use published passwords: only seed them in Development unless explicitly enabled.
    var seedDemoData = app.Configuration.GetValue<bool?>("Seeding:DemoData") ?? app.Environment.IsDevelopment();
    await DataSeeder.SeedAsync(
        app.Services,
        seedDemoData,
        app.Configuration["Seeding:AdminEmail"],
        app.Configuration["Seeding:AdminPassword"]);

    #region Middleware Pipeline

    // Only trusts X-Forwarded-* from loopback by default. When deploying behind a reverse proxy,
    // add its address to ForwardedHeadersOptions.KnownProxies (see AddWebApiServices).
    app.UseForwardedHeaders();

    // An unhandled exception becomes a 500 ProblemDetails without exception details (the developer
    // exception page still applies in Development); empty 401/403/404 responses get a ProblemDetails body
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging(opt =>
    {
        opt.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
            diagnosticContext.Set("RequestPath", httpContext.Request.Path);
            diagnosticContext.Set("RequestMethod", httpContext.Request.Method);
            diagnosticContext.Set("UserIP", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
    }

    app.UseHttpsRedirection();
    app.UseCors("React");
    // After CORS, so the browser can read a 429; per client IP, so it doesn't need the authenticated user
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    #endregion

    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous(); // the fallback policy would otherwise require a login
    await app.RunAsync();
    return 0;
}
// HostAbortedException is how `dotnet ef` stops the host after reading the model — not a failure
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Critical failure — a non-zero exit code lets Docker/orchestrators see the crash
    Log.Fatal(ex, "Application failed to start");
    return 1;
}
finally
{
    // Ensures all logs are flushed
    Log.CloseAndFlush();
}


public partial class Program() { }