
using ClinicFlow.BackgroundJobs;
using Persistence.AppData;
using Scalar.AspNetCore;

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

    builder.Services.AddInfraStructureServices(builder.Configuration);

    builder.Services.AddCoreServices(builder.Configuration);

    builder.Services.AddHostedService<AppointmentReminderJob>();


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
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    await DataSeeder.SeedAsync(app.Services);

    #region Middleware Pipeline

    // Only trusts X-Forwarded-* from loopback by default. When deploying behind a reverse proxy,
    // add its address to ForwardedHeadersOptions.KnownProxies (see AddWebApiServices).
    app.UseForwardedHeaders();

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
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
    app.UseCors("React");
    app.UseAuthentication();
    app.UseAuthorization();

    #endregion

    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    // Critical failure
    Log.Fatal(ex, "Application failed to start");
}
finally
{
    // Ensures all logs are flushed
    Log.CloseAndFlush();
}


public partial class Program() { }