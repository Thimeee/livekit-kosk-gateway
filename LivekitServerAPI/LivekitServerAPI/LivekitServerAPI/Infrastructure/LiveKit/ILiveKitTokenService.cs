using LivekitServerAPI.Domain.Sessions;

namespace LivekitServerAPI.Infrastructure.LiveKit;

/// <summary>
/// Mints <b>LiveKit</b> access tokens - the ones a client presents to the media server.
/// </summary>
/// <remarks>
/// Not to be confused with <c>IApiTokenIssuer</c>, which mints the tokens callers present to
/// <i>this</i> API. Two different JWTs with two different audiences: the API token says who is
/// calling, and is the <i>input</i> to deciding which LiveKit token they may have.
/// Stateless, so safe as a singleton.
/// </remarks>
public interface ILiveKitTokenService
{
    /// <summary>
    /// Issues a join token for <paramref name="participantId"/> in <paramref name="roomName"/>,
    /// carrying the grants appropriate to <paramref name="role"/>.
    /// </summary>
    string CreateJoinToken(ParticipantRole role, string roomName, string participantId);
}
