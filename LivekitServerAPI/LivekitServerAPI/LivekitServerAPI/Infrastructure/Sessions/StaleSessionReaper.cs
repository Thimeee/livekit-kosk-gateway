using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Infrastructure.LiveKit;
using LivekitServerAPI.Infrastructure.Persistence;
using LivekitServerAPI.Infrastructure.Realtime;

namespace LivekitServerAPI.Infrastructure.Sessions;

/// <summary>
/// Retires sessions whose LiveKit room no longer exists.
/// </summary>
/// <remarks>
/// A kiosk page that is closed, crashes or loses power never tells anyone. LiveKit removes the
/// empty room on its own timeout, but the session row stays <c>Waiting</c> for ever — so the
/// queue goes on offering a customer who left hours ago.
///
/// Worse, nothing could clear them: both <c>DELETE /api/sessions/{room}</c> and the status
/// endpoint look the room up in LiveKit first and answer 404 when it is gone. A teller's queue
/// filled with entries that could not be accepted and could not be removed. Found with five of
/// them in it, the oldest twenty-seven hours old.
///
/// Webhooks are the better answer and are not built yet (PROJECT.md §3). Until they are, this
/// sweep keeps the database honest about what is actually happening.
/// </remarks>
public sealed class StaleSessionReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<StaleSessionReaper> _logger;
    private readonly StaleSessionOptions _options;

    public StaleSessionReaper(
        IServiceScopeFactory scopes,
        ILogger<StaleSessionReaper> logger,
        IConfiguration configuration)
    {
        _scopes = scopes;
        _logger = logger;
        _options = configuration.GetSection(StaleSessionOptions.SectionName)
                                .Get<StaleSessionOptions>() ?? new StaleSessionOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Stale session sweep is disabled.");
            return;
        }

        // A first sweep on startup clears whatever accumulated while the API was down.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A sweep that fails must not take the API down with it. LiveKit being briefly
                // unreachable is the ordinary case, and the next pass will do the work.
                _logger.LogWarning(ex, "Stale session sweep failed; will try again.");
            }

            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var rooms = scope.ServiceProvider.GetRequiredService<IRoomService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IQueueNotifier>();

        var open = await store.ListActiveAsync(ct);
        if (open.Count == 0) return;

        var cutoff = DateTimeOffset.UtcNow - _options.Grace;
        var retired = 0;

        foreach (var session in open)
        {
            // Young sessions are left alone: a room is created a moment after the row, and a
            // sweep landing in that gap would retire a session that is about to be fine.
            if (session.CreatedAt > cutoff) continue;

            if (await rooms.GetRoomAsync(session.RoomName, ct) is not null) continue;

            // Never accepted means the customer gave up; accepted means the call ended without
            // anyone saying so. The distinction is what makes the numbers mean anything later.
            var status = session.AcceptedAt is null
                ? SessionStatus.Abandoned
                : SessionStatus.Ended;

            var reason = session.AcceptedAt is null
                ? SessionEndReason.CustomerLeft
                : SessionEndReason.Timeout;

            await store.SetStatusAsync(session.RoomName, status, reason, DateTimeOffset.UtcNow, ct);

            // Take it off every teller's queue too, or they keep seeing it until a refresh.
            await notifier.SessionEndedAsync(session.BranchId, session.RoomName, ct);

            retired++;

            _logger.LogInformation(
                "Retired {RoomName} as {Status}: its room no longer exists (waiting {Age:0} minutes)",
                session.RoomName, status, (DateTimeOffset.UtcNow - session.CreatedAt).TotalMinutes);
        }

        if (retired > 0)
        {
            _logger.LogInformation("Stale session sweep retired {Count} of {Open}", retired, open.Count);
        }
    }
}

public sealed class StaleSessionOptions
{
    public const string SectionName = "StaleSessions";

    public bool Enabled { get; set; } = true;

    /// <summary>How often to sweep.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How old a session must be before it is considered at all.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than the gap between creating the row and the room being live, and
    /// longer than LiveKit's own empty timeout, so a room that is merely between participants is
    /// never mistaken for one that has gone.
    /// </remarks>
    public TimeSpan Grace { get; set; } = TimeSpan.FromMinutes(2);
}
