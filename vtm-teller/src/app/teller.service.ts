import { Injectable, computed, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import {
  ConnectionQuality,
  Room,
  RoomEvent,
  Track,
  type LocalTrackPublication,
  type RemoteTrack,
} from 'livekit-client';
import { ToastService } from '@thimi/muk-kit';
import { CommandChannel, CommandError, TOPICS } from '@vtm/shared/command-channel';
import { LinkWatchdog } from '@vtm/shared/link-watchdog';
import type { KioskState } from '@vtm/shared/vtm-commands';

export const TELLER_CONFIG = {
  apiBaseUrl: 'https://livekit.nipunmcs.biz',
  /** ws://, not http:// — that one is for the management API. */
  liveKitUrl: 'wss://monapisam.nipunmcs.biz',
} as const;

export interface QueueEntry {
  sessionId: string;
  roomName: string;
  kioskId: string;
  kioskName: string;
  branchId: string;
  createdAt: string;
  waitingSeconds: number;
}

export interface Me {
  tellerId: string;
  displayName: string;
  role: string;
  branchId: string;
  status: string;
}

interface ApiEnvelope<T> {
  success: boolean;
  status: number;
  message: string;
  data?: T;
  errors?: Record<string, string[]>;
}

/**
 * The teller console, as one service.
 *
 * Zoneless: LiveKit and SignalR both fire from outside Angular, and a signal write schedules a
 * render by itself. There is no NgZone anywhere.
 */
@Injectable({ providedIn: 'root' })
export class TellerService {
  private readonly toast = inject(ToastService);

  private token = '';
  private hub?: HubConnection;
  private room?: Room;
  private ticker?: ReturnType<typeof setInterval>;

  /** Typed command plumbing, shared with the kiosk. */
  private readonly channel = new CommandChannel();

  /** Same reason as the kiosk: the SDK takes ~15s to admit a link is dead. See K-14. */
  private readonly watchdog = new LinkWatchdog(stalled => {
    if (this.inCall()) this.reconnecting.set(stalled);
  });

  private rejoinAttempt = 0;
  private rejoinTimer?: ReturnType<typeof setTimeout>;

  readonly me = signal<Me | undefined>(undefined);
  readonly signedIn = computed(() => !!this.me());
  readonly available = signal(false);
  readonly connected = signal(false);

  readonly queue = signal<QueueEntry[]>([]);
  readonly waitingCount = computed(() => this.queue().length);

  readonly inCall = signal(false);
  readonly currentRoom = signal('');
  readonly customerName = signal('');

  readonly remoteVideo = signal<RemoteTrack | undefined>(undefined);
  /** The customer's voice. Rendered by <vtm-audio-sink>, which owns a real element. */
  readonly remoteAudio = signal<RemoteTrack | undefined>(undefined);
  readonly remoteScreen = signal<RemoteTrack | undefined>(undefined);
  readonly localVideo = signal<LocalTrackPublication | undefined>(undefined);

  // ── The teller's own tracks ──────────────────────────────────────────
  readonly micOn = signal(true);
  readonly camOn = signal(true);
  readonly sharingOwn = signal(false);
  readonly needsAudioGesture = signal(false);

  // ── The kiosk's tracks, as the teller drives them ────────────────────
  //
  // These follow LiveKit's events about the customer, not what we last asked for. A mute
  // that the server refused must not leave this console showing a muted customer.
  readonly kioskMicOn = signal(true);
  readonly kioskCamOn = signal(true);
  readonly kioskSharing = computed(() => !!this.remoteScreen());
  readonly busyCommand = signal<string>('');

  /** Set when the customer presses "I'm finished". Cleared when the session ends. */
  readonly exitRequested = signal(false);

  /** True while getting back into a call we were dropped from. */
  readonly reconnecting = signal(false);

  // ── Session ──────────────────────────────────────────────────────────

  async signIn(username: string, password: string): Promise<boolean> {
    try {
      const auth = await this.post<{ accessToken: string }>('/api/auth/login', {
        username,
        password,
      });

      this.token = auth.accessToken;
      this.me.set(await this.get<Me>('/api/me'));
      this.available.set(this.me()?.status === 'Available');

      await this.connectHub();
      await this.refreshQueue();
      this.startTicker();

      return true;
    } catch (e) {
      this.toast.error(this.describe(e));
      return false;
    }
  }

  async signOut(): Promise<void> {
    await this.leaveCall(false);
    await this.hub?.stop();
    clearInterval(this.ticker);

    this.hub = undefined;
    this.token = '';
    this.me.set(undefined);
    this.queue.set([]);
    this.connected.set(false);
  }

  async setAvailable(next: boolean): Promise<void> {
    try {
      const me = await this.patch<Me>('/api/me/status', {
        status: next ? 'available' : 'away',
      });

      this.me.set(me);
      this.available.set(next);
    } catch (e) {
      // Put the switch back — the UI must not claim a state the server rejected.
      this.available.set(!next);
      this.toast.error(this.describe(e));
    }
  }

  // ── The ring ─────────────────────────────────────────────────────────

  private async connectHub(): Promise<void> {
    const hub = new HubConnectionBuilder()
      .withUrl(`${TELLER_CONFIG.apiBaseUrl}/hubs/queue`, {
        accessTokenFactory: () => this.token,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    hub.on('SessionWaiting', (e: QueueEntry) => {
      // Trust the list we already have and add to it, rather than refetching: the ring
      // should be instant, and a round trip here is visible as a delay.
      this.queue.update(q =>
        q.some(x => x.roomName === e.roomName)
          ? q
          : [...q, { ...e, kioskName: e.kioskName ?? e.kioskId, waitingSeconds: 0 }],
      );

      this.toast.info(`A customer is waiting at ${e.kioskName ?? e.kioskId}.`);
    });

    hub.on('SessionTaken', (roomName: string) => this.drop(roomName));
    hub.on('SessionEnded', (roomName: string) => this.drop(roomName));

    hub.onreconnected(async () => {
      this.connected.set(true);
      // Anything that happened while disconnected was missed, so resync.
      await this.refreshQueue();
    });
    hub.onreconnecting(() => this.connected.set(false));
    hub.onclose(() => this.connected.set(false));

    await hub.start();
    this.hub = hub;
    this.connected.set(true);
  }

  private drop(roomName: string): void {
    this.queue.update(q => q.filter(x => x.roomName !== roomName));
  }

  /** The waiting times have to keep moving, or the list looks frozen. */
  private startTicker(): void {
    clearInterval(this.ticker);
    this.ticker = setInterval(
      () => this.queue.update(q => q.map(e => ({ ...e, waitingSeconds: e.waitingSeconds + 1 }))),
      1000,
    );
  }

  async refreshQueue(): Promise<void> {
    try {
      const res = await this.get<{ count: number; entries: QueueEntry[] }>('/api/queue');
      this.queue.set(res.entries);
    } catch (e) {
      this.toast.error(this.describe(e));
    }
  }

  // ── Taking a customer ────────────────────────────────────────────────

  async accept(entry: QueueEntry): Promise<void> {
    try {
      const res = await this.post<{ roomName: string; tellerToken: string }>(
        `/api/queue/${entry.roomName}/accept`,
        {},
      );

      this.drop(entry.roomName);
      this.currentRoom.set(res.roomName);
      await this.join(res.tellerToken);
      this.inCall.set(true);
    } catch (e) {
      // Losing the race is normal, not a fault. Clear it and move on.
      this.drop(entry.roomName);
      this.toast.warning(this.describe(e));
    }
  }

  private async join(token: string): Promise<void> {
    const room = new Room({
      adaptiveStream: true,
      dynacast: true,
      audioCaptureDefaults: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
      },
      videoCaptureDefaults: { resolution: { width: 1280, height: 720, frameRate: 30 } },
    });
    this.room = room;

    room
      .on(RoomEvent.TrackSubscribed, (track: RemoteTrack) => {
        if (track.kind !== Track.Kind.Video) {
          this.remoteAudio.set(track);
          return;
        }

        // The kiosk screen and the customer's face are both video. Which is which
        // matters: the teller needs to see the screen large.
        if (track.source === Track.Source.ScreenShare) {
          this.remoteScreen.set(track);
        } else {
          this.remoteVideo.set(track);
        }
      })
      .on(RoomEvent.TrackUnsubscribed, (track: RemoteTrack) => {
        // The sink and the tiles detach their own elements when the signal clears.
        if (track.kind !== Track.Kind.Video) this.remoteAudio.set(undefined);
        else if (track.source === Track.Source.ScreenShare) this.remoteScreen.set(undefined);
        else this.remoteVideo.set(undefined);
      })
      .on(RoomEvent.ParticipantConnected, p => this.customerName.set(p.name || p.identity))
      .on(RoomEvent.ParticipantDisconnected, () => {
        this.toast.info('The customer has left.');
        void this.leaveCall(true);
      })
      .on(RoomEvent.LocalTrackPublished, pub => {
        if (pub.kind === Track.Kind.Video && pub.source === Track.Source.Camera) {
          this.localVideo.set(pub);
        }
      })
      .on(RoomEvent.AudioPlaybackStatusChanged, () =>
        this.needsAudioGesture.set(!room.canPlaybackAudio),
      )
      // What the customer's tracks are actually doing, rather than what we last asked for.
      .on(RoomEvent.TrackMuted, (_pub, p) => {
        if (p !== room.localParticipant) this.syncKioskState();
      })
      .on(RoomEvent.TrackUnmuted, (_pub, p) => {
        if (p !== room.localParticipant) this.syncKioskState();
      })
      .on(RoomEvent.TrackPublished, () => this.syncKioskState())
      .on(RoomEvent.TrackUnpublished, () => this.syncKioskState())
      // Quality reports come over the signal connection, so a cut cable produces none -
      // measured. This is for the other shape of trouble: alive but losing packets.
      .on(RoomEvent.ConnectionQualityChanged, (quality, p) => {
        if (p === room.localParticipant) {
          this.reconnecting.set(quality === ConnectionQuality.Lost);
        }
      })
      // A signal-only reconnect raises SignalReconnecting; a full one raises Reconnecting.
      .on(RoomEvent.SignalReconnecting, () => this.reconnecting.set(true))
      .on(RoomEvent.Reconnecting, () => this.reconnecting.set(true))
      .on(RoomEvent.Reconnected, () => {
        this.reconnecting.set(false);
        this.channel.attach(room);
        // Whatever happened while this console was away was not delivered. Ask.
        void this.pullKioskState();
        this.syncKioskState();
      })
      .on(RoomEvent.Disconnected, () => {
        // Measured: a server-leave ends as Disconnected and does not come back by itself.
        // A teller dropped mid-call has a customer still sitting in front of a kiosk.
        void this.rejoin();
      });

    // The customer asking to finish. Answering is what tells their kiosk it was heard.
    this.channel.handle('exit.request', async () => {
      this.exitRequested.set(true);
      this.toast.warning('The customer has asked to finish.');
    });

    // A hint that something changed on the kiosk. Only a hint: nothing is retained on the data
    // channel, so this console never treats a broadcast as the source of truth.
    this.channel.on(TOPICS.kioskState, (data) => this.applyKioskState(data as KioskState));

    this.channel.attach(room);

    await room.connect(TELLER_CONFIG.liveKitUrl, token, {
      // How long to wait for a peer connection to come UP. It does not govern how long the SDK
      // takes to notice one has gone DOWN - measured: lowering this changed nothing about the
      // ~15s before a cut cable is reported. That delay is tracked as K-14.
      peerConnectionTimeout: 10_000,
    });
    await room.localParticipant.enableCameraAndMicrophone();

    // The customer may already be in the room, in which case no event fires for them.
    const existing = [...room.remoteParticipants.values()][0];
    if (existing) this.customerName.set(existing.name || existing.identity);

    this.micOn.set(true);
    this.camOn.set(true);
    this.sharingOwn.set(false);
    this.syncKioskState();
    void this.pullKioskState();

    this.watchdog.start(() => {
      const el = document.querySelector<HTMLVideoElement>('.call__main video');
      return el && !el.paused ? el.currentTime : undefined;
    });
  }

  /**
   * Ask the kiosk what it is doing.
   *
   * The data channel has no retention, so a console that has just joined - or just come back -
   * has no way to have heard anything that happened before. Asking is the only honest way to
   * know, and it is why the kiosk owns `state.get`.
   */
  private async pullKioskState(): Promise<void> {
    if (!this.channel.peer()) return;

    try {
      this.applyKioskState(await this.channel.call('state.get', undefined));
    } catch {
      // An older kiosk build may not answer this. Track state from LiveKit's events instead
      // rather than showing an error for something the teller cannot act on.
    }
  }

  private applyKioskState(state?: KioskState): void {
    if (!state || state.version !== 1) return;

    this.exitRequested.set(state.exitRequested);
    // Mic and camera still come from LiveKit's own events - they are the authority on a track,
    // and the kiosk's view of itself can only ever agree or be stale.
  }

  private get customer() {
    return [...(this.room?.remoteParticipants.values() ?? [])][0];
  }

  private syncKioskState(): void {
    const c = this.customer;
    if (!c) return;

    const live = (source: Track.Source) => {
      const pub = c.getTrackPublication(source);
      return !!pub && !pub.isMuted;
    };

    this.kioskMicOn.set(live(Track.Source.Microphone));
    this.kioskCamOn.set(live(Track.Source.Camera));
  }

  // ── Driving the kiosk ────────────────────────────────────────────────

  /**
   * Mute and camera go through the API, not through the kiosk app. Server-side mute is
   * authoritative and keeps working when the kiosk page is wedged, which is exactly when a
   * teller most needs it.
   */
  async setKioskMic(on: boolean): Promise<void> {
    await this.setKioskTrack(Track.Source.Microphone, on, 'mic');
  }

  async setKioskCam(on: boolean): Promise<void> {
    await this.setKioskTrack(Track.Source.Camera, on, 'cam');
  }

  private async setKioskTrack(source: Track.Source, on: boolean, tag: string): Promise<void> {
    const c = this.customer;
    const sid = c?.getTrackPublication(source)?.trackSid;

    if (!c || !sid) {
      this.toast.warning('The customer is not publishing that yet.');
      return;
    }

    this.busyCommand.set(tag);
    try {
      await this.post(`/api/sessions/${this.currentRoom()}/participants/${c.identity}/mute`, {
        trackSid: sid,
        muted: !on,
      });
      // Do not set the signal here - the TrackMuted event will, from what really happened.
    } catch (e) {
      this.toast.error(this.describe(e));
    } finally {
      this.busyCommand.set('');
    }
  }

  /**
   * The screen share has to be run by the kiosk itself: a server cannot create a track, and a
   * browser will not start a capture on behalf of another page. This is what the RPC channel
   * exists for.
   */
  async setKioskScreenShare(on: boolean): Promise<void> {
    const c = this.customer;
    if (!c) return;

    this.busyCommand.set('share');
    try {
      await this.channel.call(on ? 'screenshare.start' : 'screenshare.stop', undefined);
    } catch (e) {
      // The channel turns the SDK's error codes into something a teller can act on. A timeout
      // starting a share almost always means the kiosk was opened as an ordinary browser
      // window, where getDisplayMedia waits on a picker nobody is there to answer.
      const detail = e instanceof CommandError ? e.message : 'The command failed.';
      const hint =
        on && e instanceof CommandError && e.retryable
          ? ' It may not be running in kiosk mode.'
          : '';

      this.toast.error(detail + hint);
    } finally {
      this.busyCommand.set('');
    }
  }

  // ── In-call ──────────────────────────────────────────────────────────

  async toggleMic(): Promise<void> {
    const next = !this.micOn();
    await this.room?.localParticipant.setMicrophoneEnabled(next);
    this.micOn.set(next);
  }

  async toggleCam(): Promise<void> {
    const next = !this.camOn();
    await this.room?.localParticipant.setCameraEnabled(next);
    this.camOn.set(next);
  }

  async enableAudio(): Promise<void> {
    await this.room?.startAudio();
    this.needsAudioGesture.set(false);
  }

  /** The teller sharing their own screen with the customer - a form, a statement. */
  async toggleOwnShare(): Promise<void> {
    const next = !this.sharingOwn();
    try {
      await this.room?.localParticipant.setScreenShareEnabled(next);
      this.sharingOwn.set(next);
    } catch {
      // The picker was dismissed. Not an error worth a toast.
      this.sharingOwn.set(false);
    }
  }

  /**
   * Get back into a call we were dropped from.
   *
   * The customer is still standing at the kiosk. Clearing this console and returning to the
   * queue would leave them talking to nobody while their session is still open, so the teller
   * goes back in. A 404 - the session really has ended - is the one case that stops it.
   */
  private async rejoin(): Promise<void> {
    const room = this.currentRoom();

    if (!room || !this.inCall()) {
      return;
    }

    this.reconnecting.set(true);

    const delay = Math.min(1000 * 2 ** this.rejoinAttempt, 10_000);
    this.rejoinAttempt += 1;

    clearTimeout(this.rejoinTimer);
    this.rejoinTimer = setTimeout(async () => {
      try {
        const res = await this.post<{ roomName: string; token: string; status: string }>(
          `/api/sessions/${room}/rejoin`,
          {},
        );

        await this.join(res.token);

        this.rejoinAttempt = 0;
        this.reconnecting.set(false);
      } catch (e) {
        if (e instanceof Error && e.message.includes('404')) {
          this.rejoinAttempt = 0;
          this.toast.info('That session has ended.');
          await this.leaveCall(false);
          return;
        }

        void this.rejoin();
      }
    }, delay);
  }

  /**
   * @param endSession false when signing out mid-call - the customer may still be
   * served by someone else, so the room is left alone.
   */
  async leaveCall(endSession: boolean): Promise<void> {
    this.watchdog.stop();
    clearTimeout(this.rejoinTimer);
    this.rejoinAttempt = 0;
    this.reconnecting.set(false);

    const room = this.currentRoom();
    await this.room?.disconnect();
    this.room?.removeAllListeners();
    this.room = undefined;

    if (endSession && room) {
      try {
        await this.del(`/api/sessions/${room}`);
      } catch {
        /* the empty-timeout collects it */
      }
    }

    this.inCall.set(false);
    this.exitRequested.set(false);
    this.sharingOwn.set(false);
    this.currentRoom.set('');
    this.customerName.set('');
    this.remoteVideo.set(undefined);
    this.remoteAudio.set(undefined);
    this.remoteScreen.set(undefined);
    this.localVideo.set(undefined);
    this.needsAudioGesture.set(false);

    await this.refreshQueue();
  }

  // ── Plumbing ─────────────────────────────────────────────────────────

  private get<T>(path: string) {
    return this.send<T>('GET', path);
  }
  private post<T>(path: string, body?: unknown) {
    return this.send<T>('POST', path, body);
  }
  private patch<T>(path: string, body: unknown) {
    return this.send<T>('PATCH', path, body);
  }
  private del<T>(path: string) {
    return this.send<T>('DELETE', path);
  }

  private async send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const res = await fetch(TELLER_CONFIG.apiBaseUrl + path, {
      method,
      headers: {
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(this.token ? { Authorization: `Bearer ${this.token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    const envelope = (await res.json().catch(() => null)) as ApiEnvelope<T> | null;

    if (!res.ok || !envelope?.success) {
      const detail = envelope?.errors
        ? Object.values(envelope.errors).flat().join(' ')
        : envelope?.message;
      // The status stays in the message: rejoin needs to tell "the session has ended" (404,
      // stop trying) from "the API is having a moment" (anything else, keep trying).
      throw new Error(detail ? `${detail} (${res.status})` : `Request failed (${res.status}).`);
    }

    return envelope.data as T;
  }

  private describe(e: unknown): string {
    if (e instanceof TypeError) {
      return 'Cannot reach the VTM service. Check that the API is running.';
    }
    return e instanceof Error ? e.message : 'Something went wrong.';
  }
}
