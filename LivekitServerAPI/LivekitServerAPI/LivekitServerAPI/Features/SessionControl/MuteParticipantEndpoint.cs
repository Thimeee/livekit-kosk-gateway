using FastEndpoints;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Auth;
using LivekitServerAPI.Infrastructure.LiveKit;

namespace LivekitServerAPI.Features.SessionControl;

/// <summary>
/// Server-side mute of a participant's published track.
/// </summary>
/// <remarks>
/// With no <c>trackSid</c> this mutes every track the participant publishes, which is what a
/// teller means by "mute the customer". Naming a track mutes just that one.
/// </remarks>
public class MuteParticipantEndpoint
    : Endpoint<MuteParticipantRequest, ApiResponse<MuteParticipantResponse>>
{
    private readonly IRoomService _rooms;

    public MuteParticipantEndpoint(IRoomService rooms) => _rooms = rooms;

    public override void Configure()
    {
        Post("/api/sessions/{roomName}/participants/{identity}/mute");
        Policies(VtmPolicies.Teller);
    }

    public override async Task HandleAsync(MuteParticipantRequest req, CancellationToken ct)
    {
        var participant = await _rooms.GetParticipantAsync(req.RoomName, req.Identity, ct);
        if (participant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        List<string> trackSids;
        if (req.TrackSid is null)
        {
            trackSids = participant.Tracks.Select(t => t.Sid).ToList();
        }
        else
        {
            // LiveKit accepts an unknown track sid and reports success, so an unchecked
            // pass-through would answer "1 track muted" when nothing was muted.
            if (!participant.Tracks.Any(t => t.Sid == req.TrackSid))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            trackSids = [req.TrackSid];
        }

        foreach (var sid in trackSids)
        {
            await _rooms.MuteTrackAsync(req.RoomName, req.Identity, sid, req.Muted, ct);
        }

        Logger.LogInformation(
            "{Action} {TrackCount} track(s) of {Identity} in {RoomName}",
            req.Muted ? "Muted" : "Unmuted", trackSids.Count, req.Identity, req.RoomName);

        await Send.OkAsync(
            ApiResponse<MuteParticipantResponse>.Ok(
                new MuteParticipantResponse(req.Identity, req.Muted, trackSids.Count)),
            cancellation: ct);
    }
}
