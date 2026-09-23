using System.Text.Json.Serialization;
using LivekitServerAPI.Contracts;
using LivekitServerAPI.Domain.Sessions;
using LivekitServerAPI.Features.Auth;
using LivekitServerAPI.Features.Health;
using LivekitServerAPI.Features.Me;
using LivekitServerAPI.Features.Queue;
using LivekitServerAPI.Features.Kiosks;
using LivekitServerAPI.Features.SessionControl;
using LivekitServerAPI.Features.Sessions;
using LivekitServerAPI.Features.Tokens;

namespace LivekitServerAPI;

// camelCase here too, not just in FastEndpoints' own serializer options. The exception
// handler serialises through this context directly, so without it error bodies came back
// PascalCase while every success body was camelCase.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]

// Tokens
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(ApiResponse<TokenResponse>))]

// Sessions
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(CreateSessionResponse))]
[JsonSerializable(typeof(ApiResponse<CreateSessionResponse>))]
[JsonSerializable(typeof(SessionSummary))]
[JsonSerializable(typeof(ApiResponse<SessionSummary>))]
[JsonSerializable(typeof(SessionListResponse))]
[JsonSerializable(typeof(ApiResponse<SessionListResponse>))]
[JsonSerializable(typeof(SessionDetailResponse))]
[JsonSerializable(typeof(ApiResponse<SessionDetailResponse>))]
[JsonSerializable(typeof(UpdateSessionStatusRequest))]
[JsonSerializable(typeof(EndSessionResponse))]
[JsonSerializable(typeof(ApiResponse<EndSessionResponse>))]

// Session control
[JsonSerializable(typeof(MuteParticipantRequest))]
[JsonSerializable(typeof(MuteParticipantResponse))]
[JsonSerializable(typeof(ApiResponse<MuteParticipantResponse>))]
[JsonSerializable(typeof(RemoveParticipantResponse))]
[JsonSerializable(typeof(ApiResponse<RemoveParticipantResponse>))]
[JsonSerializable(typeof(UpdatePermissionsRequest))]
[JsonSerializable(typeof(UpdatePermissionsResponse))]
[JsonSerializable(typeof(ApiResponse<UpdatePermissionsResponse>))]
[JsonSerializable(typeof(NotifyRequest))]
[JsonSerializable(typeof(NotifyResponse))]
[JsonSerializable(typeof(ApiResponse<NotifyResponse>))]
[JsonSerializable(typeof(TransferSessionRequest))]
[JsonSerializable(typeof(TransferSessionResponse))]
[JsonSerializable(typeof(ApiResponse<TransferSessionResponse>))]
[JsonSerializable(typeof(MonitorSessionRequest))]
[JsonSerializable(typeof(MonitorSessionResponse))]
[JsonSerializable(typeof(ApiResponse<MonitorSessionResponse>))]

// Auth
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(KioskAuthRequest))]
[JsonSerializable(typeof(AuthTokenResponse))]
[JsonSerializable(typeof(ApiResponse<AuthTokenResponse>))]

// Kiosks
[JsonSerializable(typeof(RegisterKioskRequest))]
[JsonSerializable(typeof(KioskResponse))]
[JsonSerializable(typeof(ApiResponse<KioskResponse>))]
[JsonSerializable(typeof(KioskListResponse))]
[JsonSerializable(typeof(ApiResponse<KioskListResponse>))]
[JsonSerializable(typeof(EnrollKioskRequest))]
[JsonSerializable(typeof(EnrollKioskResponse))]
[JsonSerializable(typeof(ApiResponse<EnrollKioskResponse>))]

// Queue
[JsonSerializable(typeof(QueueEntry))]
[JsonSerializable(typeof(QueueListResponse))]
[JsonSerializable(typeof(ApiResponse<QueueListResponse>))]
[JsonSerializable(typeof(AcceptSessionResponse))]
[JsonSerializable(typeof(ApiResponse<AcceptSessionResponse>))]

// Me
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(ApiResponse<MeResponse>))]
[JsonSerializable(typeof(UpdateMyStatusRequest))]

// Health
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ApiResponse<HealthResponse>))]

// Domain payloads that travel inside LiveKit (token + room metadata)
[JsonSerializable(typeof(ParticipantMetadata))]
[JsonSerializable(typeof(SessionMetadata))]

// Errors
[JsonSerializable(typeof(ErrorResponseDto))]
public partial class AppJsonSerializerContext : JsonSerializerContext;
