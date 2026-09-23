import { Room, RoomEvent, RpcError, type RemoteParticipant } from 'livekit-client';
import { COMMANDS, TOPICS, type CommandName, type Payloads } from './vtm-commands';

/**
 * The teller ↔ kiosk command channel.
 *
 * What this exists to hide:
 *
 * - **Method names are typed.** A command that exists on one side only is a compile error here,
 *   not a button that quietly does nothing in a branch.
 * - **Identities are read from the room, never assumed.** The API prefixes what you ask for -
 *   request `probe-a` and you join as `teller-probe-a`. Addressing RPC by the name you asked for
 *   fails with a connection timeout that looks like a network fault.
 * - **RPC errors are translated.** The SDK has eleven codes; a bare `catch {}` turns "this kiosk
 *   is running an old build" and "the kiosk is not in kiosk mode" into the same silence.
 * - **Payload limits are checked before sending.** The SDK truncates silently past its limits.
 *
 * What it deliberately does NOT do: pretend the channel is pub/sub. LiveKit's data channel has
 * no subscriptions, no retention and no replay - `topic` is a label carried in the packet and
 * every participant receives every packet and filters it themselves. Anything published while a
 * participant was away is gone. State therefore has an owner and is *asked for*, not listened
 * for; see `requestState`.
 */

/** Anything the caller can act on, rather than a number. */
export class CommandError extends Error {
  constructor(
    readonly code: number,
    message: string,
    /** True when retrying could plausibly work. */
    readonly retryable: boolean,
  ) {
    super(message);
    this.name = 'CommandError';
  }
}

/** Measured against the pinned SDK: see RpcError.ErrorCode in client-sdk-js-main. */
const CODES = {
  APPLICATION_ERROR: 1500,
  CONNECTION_TIMEOUT: 1501,
  RESPONSE_TIMEOUT: 1502,
  RECIPIENT_DISCONNECTED: 1503,
  RESPONSE_PAYLOAD_TOO_LARGE: 1504,
  SEND_FAILED: 1505,
  UNSUPPORTED_METHOD: 1400,
  RECIPIENT_NOT_FOUND: 1401,
  REQUEST_PAYLOAD_TOO_LARGE: 1402,
  UNSUPPORTED_SERVER: 1403,
  UNSUPPORTED_VERSION: 1404,
} as const;

/** The SDK truncates past these rather than failing, which hides the problem. */
const MAX_PAYLOAD_BYTES = 15_000;

function describe(code: number, method: CommandName): CommandError {
  switch (code) {
    case CODES.UNSUPPORTED_METHOD:
      return new CommandError(code, `The kiosk does not support "${method}" - it is probably running an older build.`, false);
    case CODES.RECIPIENT_NOT_FOUND:
      return new CommandError(code, 'The other side is not in this call.', false);
    case CODES.RECIPIENT_DISCONNECTED:
      return new CommandError(code, 'The other side dropped out of the call.', true);
    case CODES.CONNECTION_TIMEOUT:
    case CODES.RESPONSE_TIMEOUT:
      return new CommandError(code, 'The kiosk did not answer in time.', true);
    case CODES.REQUEST_PAYLOAD_TOO_LARGE:
    case CODES.RESPONSE_PAYLOAD_TOO_LARGE:
      return new CommandError(code, 'That command carried too much data to send.', false);
    case CODES.SEND_FAILED:
      return new CommandError(code, 'The command could not be sent.', true);
    case CODES.UNSUPPORTED_SERVER:
    case CODES.UNSUPPORTED_VERSION:
      return new CommandError(code, 'This LiveKit server is too old for the command channel.', false);
    default:
      return new CommandError(code, 'The kiosk reported an error running that command.', false);
  }
}

type Handler<K extends CommandName> = (
  req: Payloads[K]['req'],
  callerIdentity: string,
) => Promise<Payloads[K]['res']>;

/**
 * One map holding handlers for commands with different request and response types cannot be
 * expressed without widening somewhere. The widening is here, deliberately and in one place:
 * `handle` and `call` are fully typed, so callers never see it.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type StoredHandler = (req: any, callerIdentity: string) => Promise<any>;

export class CommandChannel {
  private readonly handlers = new Map<CommandName, StoredHandler>();
  private readonly topicListeners = new Map<string, Set<(data: unknown, from?: string) => void>>();
  private bound?: Room;

  /**
   * Handlers are registered against the SDK immediately and again on every reconnect.
   *
   * Measured: a `signal-reconnect` keeps them - the handler map lives on the Room and nothing
   * clears it. Re-registering anyway costs nothing and covers the case where the caller hands
   * this channel a *new* Room after a full rejoin.
   */
  attach(room: Room): void {
    this.bound = room;

    for (const [name, fn] of this.handlers) this.register(room, name, fn);

    room.on(RoomEvent.DataReceived, (
      payload: Uint8Array,
      participant?: RemoteParticipant,
      _kind?: unknown,
      topic?: string,
    ) => {
      const listeners = topic ? this.topicListeners.get(topic) : undefined;
      if (!listeners?.size) return;

      let decoded: unknown;
      try {
        decoded = JSON.parse(new TextDecoder().decode(payload));
      } catch {
        return; // not ours, or malformed - never let one bad packet break the room
      }

      for (const fn of listeners) fn(decoded, participant?.identity);
    });
  }

  /** Declare what this side answers. Safe to call before `attach`. */
  handle<K extends CommandName>(name: K, fn: Handler<K>): void {
    this.handlers.set(name, fn as StoredHandler);
    if (this.bound) this.register(this.bound, name, fn as StoredHandler);
  }

  private register(room: Room, name: CommandName, fn: StoredHandler): void {
    // Re-registering the same name throws, so clear it first.
    try {
      room.localParticipant.unregisterRpcMethod(name);
    } catch {
      /* not registered yet */
    }

    room.localParticipant.registerRpcMethod(name, async (data: { payload: string; callerIdentity: string }) => {
      const req = data.payload ? JSON.parse(data.payload) : undefined;
      const res = await fn(req, data.callerIdentity);
      return res === undefined ? '' : JSON.stringify(res);
    });
  }

  /**
   * Call the other participant.
   *
   * This is a two-party room, so "the other participant" is unambiguous - and reading it from
   * the room is the only way to get the identity right, since the API prefixes it.
   */
  async call<K extends CommandName>(
    name: K,
    req: Payloads[K]['req'],
    responseTimeout?: number,
  ): Promise<Payloads[K]['res']> {
    const room = this.bound;
    const peer = this.peer();

    if (!room || !peer) {
      throw new CommandError(CODES.RECIPIENT_NOT_FOUND, 'The other side is not in this call.', false);
    }

    const payload = req === undefined ? '' : JSON.stringify(req);

    if (payload.length > MAX_PAYLOAD_BYTES) {
      throw new CommandError(
        CODES.REQUEST_PAYLOAD_TOO_LARGE,
        'That command carried too much data to send.',
        false,
      );
    }

    try {
      const raw = await room.localParticipant.performRpc({
        destinationIdentity: peer.identity,
        method: name,
        payload,
        responseTimeout,
      });

      return (raw ? JSON.parse(raw) : undefined) as Payloads[K]['res'];
    } catch (e) {
      throw e instanceof RpcError ? describe(e.code, name) : describe(CODES.APPLICATION_ERROR, name);
    }
  }

  /** The other participant, by whatever the room actually calls them. */
  peer(): RemoteParticipant | undefined {
    return [...(this.bound?.remoteParticipants.values() ?? [])][0];
  }

  /** Class 2 commands have real-world side effects and must go through the API - see D-010. */
  isClassTwo(name: CommandName): boolean {
    return COMMANDS[name]?.cls === 2;
  }

  // ── Broadcast ────────────────────────────────────────────────────────
  //
  // For telling, not asking. Nothing is retained, so a broadcast is only ever a hint that
  // something changed - never the sole source of a fact. Whoever cares asks for the state.

  async broadcast(topic: string, data: unknown): Promise<void> {
    const body = new TextEncoder().encode(JSON.stringify(data));
    if (body.byteLength > MAX_PAYLOAD_BYTES) return;

    await this.bound?.localParticipant.publishData(body, { reliable: true, topic });
  }

  on(topic: string, fn: (data: unknown, from?: string) => void): () => void {
    const set = this.topicListeners.get(topic) ?? new Set();
    set.add(fn);
    this.topicListeners.set(topic, set);
    return () => set.delete(fn);
  }
}

export { TOPICS };
