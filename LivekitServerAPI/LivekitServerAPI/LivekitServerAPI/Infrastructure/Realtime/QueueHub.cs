using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LivekitServerAPI.Infrastructure.Realtime;

/// <summary>
/// Pushes the ring to tellers.
/// </summary>
/// <remarks>
/// LiveKit cannot carry this: the teller is not in a room yet, which is the whole point of the
/// queue. A customer arriving is a fact about the branch, not about any session anyone has joined.
/// <para>
/// Tellers are grouped by branch, so a customer in Colombo does not ring a desk in Kandy.
/// Supervisors and admins join every branch group they ask for.
/// </para>
/// </remarks>
[Authorize(Policy = VtmPolicies.Staff)]
public class QueueHub : Hub<IQueueClient>
{
    private readonly ILogger<QueueHub> _logger;

    public QueueHub(ILogger<QueueHub> logger) => _logger = logger;

    public static string BranchGroup(string branchId) => $"branch:{branchId}";

    public override async Task OnConnectedAsync()
    {
        var branch = Context.User?.Branch();
        var teller = Context.User?.SubjectId();

        if (!string.IsNullOrWhiteSpace(branch))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(branch));
        }

        _logger.LogInformation(
            "Teller {TellerId} connected to the queue feed for branch {BranchId}",
            teller, branch ?? "(none)");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Teller {TellerId} disconnected from the queue feed", Context.User?.SubjectId());

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Lets a supervisor watch a branch other than their own. Tellers are already in theirs and
    /// gain nothing by calling this.
    /// </summary>
    public async Task WatchBranch(string branchId)
    {
        if (Context.User?.Role() is not (VtmRoles.Supervisor or VtmRoles.Admin))
        {
            throw new HubException("Only supervisors may watch another branch.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(branchId));
    }
}

/// <summary>
/// What the server can call on a connected teller. Typed so a renamed method is a compile error
/// rather than a ring that silently stops arriving.
/// </summary>
public interface IQueueClient
{
    /// <summary>A customer is waiting. This is the ring.</summary>
    Task SessionWaiting(QueueEntryNotification entry);

    /// <summary>Someone else accepted it - remove it from the list.</summary>
    Task SessionTaken(string roomName, string tellerId);

    /// <summary>The customer left, or the session ended.</summary>
    Task SessionEnded(string roomName);
}

public record QueueEntryNotification(
    Guid SessionId,
    string RoomName,
    string KioskId,
    /// <summary>The kiosk's display name. A teller needs to know which desk to look at,
    /// and "Colombo Main - Lobby 1" tells them that where "K-01" does not.</summary>
    string KioskName,
    string BranchId,
    DateTimeOffset CreatedAt);
