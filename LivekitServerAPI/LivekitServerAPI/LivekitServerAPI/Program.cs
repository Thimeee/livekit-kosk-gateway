using System.Text;
using FastEndpoints;
using LivekitServerAPI;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Infrastructure.Auth;
using LivekitServerAPI.Infrastructure.Errors;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using LivekitServerAPI.Infrastructure.Sessions;
using LivekitServerAPI.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger: captures failures that happen before configuration is read.
// Replaced below once appsettings has been bound.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateSlimBuilder(args);

    // Real logger, configured entirely from the "Serilog" section of appsettings.json.
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .CreateLogger();

    builder.Services.AddSerilog();

    // ── Configuration ───────────────────────────────────────────────────────
    // Validated on start, so a missing or malformed value fails the boot rather
    // than the first request that needs it.
    builder.Services
        .AddOptions<LiveKitOptions>()
        .Bind(builder.Configuration.GetSection(LiveKitOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(
            o => Encoding.UTF8.GetByteCount(o.ApiSecret) >= 32,
            "LiveKit:ApiSecret must be at least 32 bytes - the SDK rejects anything shorter.")
        .Validate(
            o => Uri.TryCreate(o.ServerUrl, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            "LiveKit:ServerUrl must be an absolute http:// or https:// URL. "
            + "The ws:// URL is for clients, not for the management API.")
        .Validate(
            o => o.TokenTtl > TimeSpan.Zero && o.TokenTtl <= TimeSpan.FromHours(6),
            "LiveKit:TokenTtl must be greater than zero and no more than 6 hours.")
        .ValidateOnStart();

    // ── Persistence ─────────────────────────────────────────────────────
    var connectionString = builder.Configuration.GetConnectionString("Vtm")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Vtm is not configured. The API cannot start without its database.");

    builder.Services.AddDbContext<VtmDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
        {
            // A transient SQL failure should not surface as a failed VTM session.
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        }));

    builder.Services.AddScoped<ISessionStore, SessionStore>();

    // Keeps the database honest about sessions whose room has gone. Without it a teller's queue
    // fills with customers who left hours ago, and nothing can clear them - both the delete and
    // the status endpoints look the room up first and answer 404 once it is missing.
    builder.Services.AddHostedService<StaleSessionReaper>();

    // ── Authentication ──────────────────────────────────────────────────
    // The only place the choice of identity source is visible. See docs/PROJECT.md D-021.
    builder.Services.Configure<DemoDataOptions>(
        builder.Configuration.GetSection(DemoDataOptions.SectionName));

    builder.Services.AddVtmAuthentication(builder.Configuration);

    // ── Realtime ────────────────────────────────────────────────────────
    // The ring. LiveKit cannot carry it - the teller is not in a room yet.
    builder.Services.AddSignalR();
    builder.Services.AddScoped<IQueueNotifier, SignalRQueueNotifier>();

    // The browser apps are served from a different origin in development.
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    // ── LiveKit services ────────────────────────────────────────────────────
    // LiveKitTokenService is stateless, so a singleton is safe.
    builder.Services.AddSingleton<ILiveKitTokenService, LiveKitTokenService>();

    // RoomService is SCOPED on purpose: RoomServiceClient mutates the shared
    // HttpClient's Authorization header on every call, so a singleton would let
    // concurrent requests send each other's tokens. See docs/PROJECT.md K-4.
    builder.Services.AddHttpClient(RoomService.HttpClientName);
    builder.Services.AddScoped<IRoomService, RoomService>();

    builder.Services.AddExceptionHandler<LiveKitExceptionHandler>();

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();
    builder.Services.AddFastEndpoints(DiscoveredTypes.All);

    var app = builder.Build();

    app.UseExceptionHandler(_ => { });

    app.UseCors();

    app.UseAuthentication();
    app.UseAuthorization();

    // One tidy line per request instead of the framework's several.
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseFastEndpoints(c =>
    {
        // FastEndpoints does NOT use ASP.NET Core's ConfigureHttpJsonOptions - it has its own
        // serializer options, so the source-generated context has to be registered here.
        c.Serializer.Options.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);

        c.Errors.ResponseBuilder = (failures, ctx, statusCode) => new ErrorResponseDto
        {
            Success = false,
            Status = statusCode,
            Message = "Validation failed for the request.",
            // Keys are lower-camelCased here rather than taken as-is. FluentValidation and
            // AddError(x => x.Prop) disagree on casing, so a client keying off errors.roomName
            // would silently miss errors.RoomName.
            Errors = failures
                .GroupBy(f => CamelCase(f.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(f => f.ErrorMessage).ToArray()),
        };
    });

    app.MapHub<QueueHub>("/hubs/queue");

    await app.SeedBootstrapAdminAsync();
    await app.SeedDemoDataAsync();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LivekitServerAPI terminated unexpectedly");
    throw;
}
finally
{
    // Flushes anything still buffered in the file sink before the process exits.
    Log.CloseAndFlush();
}

static string CamelCase(string name) =>
    string.IsNullOrEmpty(name) || char.IsLower(name[0])
        ? name
        : char.ToLowerInvariant(name[0]) + name[1..];
