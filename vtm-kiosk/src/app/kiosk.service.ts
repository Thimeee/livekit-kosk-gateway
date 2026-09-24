import { Injectable, signal, computed } from '@angular/core';
import {
  ConnectionQuality,
  Room,
  RoomEvent,
  Track,
  type RemoteParticipant,
  type RemoteTrack,
  type LocalTrackPublication,
} from 'livekit-client';
import { CommandChannel, TOPICS } from '@vtm/shared/command-channel';
import { LinkWatchdog } from '@vtm/shared/link-watchdog';
import type { KioskState } from '@vtm/shared/vtm-commands';
import { deviceSecret, kioskConfig, loadKioskConfig } from './kiosk.config';

export type KioskScreen = 'booting' | 'idle' | 'waiting' | 'incall' | 'error';

interface ApiEnvelope<T> {
  success: boolean;
  status: number;
  message: string;
  data?: T;
  errors?: Record<string, string[]>;
}

/**
 * The whole kiosk, as one service.
 *
 * Everything is a signal. This app is zoneless, so a LiveKit callback that writes a signal
 * schedules change detection by itself — there is no NgZone.run() anywhere, and none is needed.
 */
@Injectable({ providedIn: 'root' })
export class KioskService {
  private room?: Room;
  private apiToken = '';

  /** Typed command plumbing, shared with the teller console. */
  private readonly channel = new CommandChannel();

  /**
   * Notices a dead link long before the SDK does. Without it a customer watches a frozen
   * teller for about 15 seconds with nothing said - see K-14.
   */
  private readonly watchdog = new LinkWatchdog(stalled => {
    // Only speak up during a call. Between calls there is nothing to have stalled.
    if (this.screen() === 'incall' || this.screen() === 'waiting') {
      this.reconnecting.set(stalled);
    }
  });

  /** Set while we are trying to get back into a room we were thrown out of. */
  private rejoinAttempt = 0;
  private rejoinTimer?: ReturnType<typeof setTimeout>;

  /**
   * True while a stop the teller asked for is in progress. It is the only way to tell that stop
   * apart from the customer pressing the browser's own "Stop sharing" - both end as the same
   * unpublish.
   */
  private tellerStoppingShare = false;

  readonly screen = signal<KioskScreen>('booting');
  readonly statusText = signal('Starting up…');
  readonly errorText = signal('');

  readonly roomName = signal('');
  readonly tellerName = signal('');

  /** The teller's camera, once they have joined and published it. */
  readonly remoteVideo = signal<RemoteTrack | undefined>(undefined);
  readonly remoteAudio = signal<RemoteTrack | undefined>(undefined);
  readonly localVideo = signal<LocalTrackPublication | undefined>(undefined);

  /**
   * Whether this kiosk's own tracks are live. Nobody here can change them - the teller does,
   * server-side - so these follow LiveKit's events rather than any button on this screen.
   */
  readonly micOn = signal(true);
  readonly camOn = signal(true);
  readonly sharing = signal(false);

  /** Set once the customer has asked to finish, so the button can say it was heard. */
  readonly exitRequested = signal(false);

  /** True while reconnecting after being dropped, so the screen can say so. */
  readonly reconnecting = signal(false);

  /** Read from the settings at boot; see KioskConfig for why it is off by default. */
  readonly showScreenShareIndicator = signal(false);

  /** Browsers block audio until a gesture. When this is true the UI has to ask for one. */
  readonly needsAudioGesture = signal(false);

  readonly canStart = computed(() => this.screen() === 'idle');

  // ── Device authentication ────────────────────────────────────────────

  async boot(): Promise<void> {
    // Settings first: which kiosk this is, and where the API lives, are not known until now.
    await loadKioskConfig();
    this.showScreenShareIndicator.set(kioskConfig().showScreenShareIndicator);

    const secret = deviceSecret();

    if (!secret) {
      this.fail(
        'This kiosk has not been enrolled. Ask an administrator to enrol it, then give the ' +
          'secret to the shell in kiosk-shell.json, or to a browser in public/kiosk.secret.',
      );
      return;
    }

    try {
      const auth = await this.post<{ accessToken: string }>('/api/auth/kiosk', {
        kioskId: kioskConfig().kioskId,
        secret,
      });

      this.apiToken = auth.accessToken;
      this.screen.set('idle');
      this.statusText.set('');
    } catch (e) {
      this.fail(this.describe(e));
    }
  }

  // ── The session ──────────────────────────────────────────────────────

  async startSession(): Promise<void> {
    this.screen.set('waiting');
    this.statusText.set('Connecting…');
    this.errorText.set('');

    try {
      const session = await this.post<{
        roomName: string;
        kioskToken: string;
      }>('/api/sessions', { kioskId: kioskConfig().kioskId }, this.apiToken);

      this.roomName.set(session.roomName);
      await this.join(session.kioskToken);

      this.statusText.set('Waiting for a teller…');
    } catch (e) {
      this.screen.set('idle');
      this.errorText.set(this.describe(e));
    }
  }

  private async join(token: string, roomName?: string): Promise<void> {
    // On a rejoin the room name is already known; on a first join startSession set it.
    if (roomName) this.roomName.set(roomName);

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
        if (track.kind === Track.Kind.Video) {
          this.remoteVideo.set(track);
        } else {
          // Attached by <vtm-audio-sink>, to an element that is really in the document.
          this.remoteAudio.set(track);
        }
      })
      .on(RoomEvent.TrackUnsubscribed, (track: RemoteTrack) => {
        // The sink and the tile detach their own elements when the signal clears.
        if (track.kind === Track.Kind.Video) this.remoteVideo.set(undefined);
        else this.remoteAudio.set(undefined);
      })
      .on(RoomEvent.ParticipantConnected, (p) => this.onTellerPresent(p))
      .on(RoomEvent.ParticipantDisconnected, () => {
        this.tellerName.set('');
        this.remoteVideo.set(undefined);
        // The teller dropping out is not the end of the session - they may be transferring it.
        this.screen.set('waiting');
        this.statusText.set('The teller has left. Reconnecting you…');
      })
      .on(RoomEvent.LocalTrackPublished, (pub) => {
        if (pub.kind === Track.Kind.Video && pub.source === Track.Source.Camera) {
          this.localVideo.set(pub);
        }
      })
      .on(RoomEvent.AudioPlaybackStatusChanged, () =>
        this.needsAudioGesture.set(!room.canPlaybackAudio),
      )
      // The teller mutes this kiosk server-side, so the only honest source for these is
      // what LiveKit reports back about our own tracks.
      .on(RoomEvent.LocalTrackPublished, () => this.syncLocalState())
      // The browser's "is sharing your screen" bar has a Stop sharing button. It stays: it is
      // how the customer knows their screen is being watched, and stopping is their right. When
      // they use it the SDK unpublishes the share (measured in LocalParticipant.handleTrackEnded),
      // so this is where the teller gets told, instead of watching the tile quietly vanish.
      .on(RoomEvent.LocalTrackUnpublished, (pub) => {
        this.syncLocalState();
        if (pub.source !== Track.Source.ScreenShare) return;

        if (this.tellerStoppingShare) return;

        void this.channel.call('screenshare.ended', { by: 'customer' }).catch(() => {
          // The teller's own tile disappears either way; the message is a courtesy on top.
        });
      })
      // (publication, participant) - the participant is the second argument.
      .on(RoomEvent.TrackMuted, (_pub, p) => {
        if (p === room.localParticipant) this.syncLocalState();
      })
      .on(RoomEvent.TrackUnmuted, (_pub, p) => {
        if (p === room.localParticipant) this.syncLocalState();
      })
      // Quality reports arrive over the signal connection, so a cut cable produces no report
      // at all - measured. This catches the other shape of trouble: a link that is alive but
      // losing packets, where the SDK will not reconnect and nothing else would say so.
      .on(RoomEvent.ConnectionQualityChanged, (quality, p) => {
        if (p === room.localParticipant) {
          this.reconnecting.set(quality === ConnectionQuality.Lost);
        }
      })
      .on(RoomEvent.SignalReconnecting, () => this.reconnecting.set(true))
      .on(RoomEvent.Reconnecting, () => this.reconnecting.set(true))
      .on(RoomEvent.Reconnected, () => {
        // A signal reconnect keeps everything, but re-attaching is cheap and covers the
        // case where this room object was replaced underneath us.
        this.reconnecting.set(false);
        this.channel.attach(room);
        this.announce();
      })
      .on(RoomEvent.Disconnected, () => {
        // Measured: a `server-leave` ends as Disconnected and the SDK does NOT come back by
        // itself. Resetting here is what used to drop a customer who was mid-transaction on
        // to the idle screen while their session was still open and a teller still waiting.
        void this.rejoin();
      });

    this.registerCommands();
    this.channel.attach(room);

    await room.connect(kioskConfig().liveKitUrl, token, {
      // How long to wait for a peer connection to come UP. It does not govern how long the SDK
      // takes to notice one has gone DOWN - measured: lowering this changed nothing about the
      // ~15s before a cut cable is reported. That delay is tracked as K-14.
      peerConnectionTimeout: 10_000,
    });
    // ParticipantConnected only fires for people who arrive AFTER we do. The ring goes out the
    // moment the session is created, before this kiosk has finished connecting, so a quick
    // teller is often in the room first - and then no event ever comes, and the customer sits
    // on "waiting" while the teller sits in the call. Measured: it happened on every run with a
    // slower kiosk (a headed browser, WebView2) and never with a fast one. Look, don't wait.
    const already = [...room.remoteParticipants.values()][0];
    if (already) this.onTellerPresent(already);

    await room.localParticipant.enableCameraAndMicrophone();

    // The teller's own video is the thing whose stopping means the link is gone. A camera the
    // teller turned off returns undefined, which suspends the check rather than crying wolf.
    this.watchdog.start(() => {
      const el = document.querySelector<HTMLVideoElement>('.call__remote video');
      return el && !el.paused ? el.currentTime : undefined;
    });

    this.syncLocalState();
  }

  /**
   * The teller is in the room - whether they arrived after us, or were there before we were.
   *
   * The call starts when the teller arrives, not when their camera does. A teller with video
   * off is still on the call, and a customer left staring at "waiting" while someone is talking
   * to them is the worse failure.
   */
  private onTellerPresent(p: RemoteParticipant): void {
    this.tellerName.set(p.name || p.identity);
    this.screen.set('incall');
    this.statusText.set('');
    // They may be rejoining and know nothing of what happened while they were away.
    this.announce();
  }

  /** Reads our own publications back, rather than assuming a call did what it was asked. */
  private syncLocalState(): void {
    const me = this.room?.localParticipant;
    if (!me) return;

    const live = (source: Track.Source) => {
      const pub = me.getTrackPublication(source);
      return !!pub && !pub.isMuted;
    };

    this.micOn.set(live(Track.Source.Microphone));
    this.camOn.set(live(Track.Source.Camera));
    this.sharing.set(live(Track.Source.ScreenShare));
  }

  // ── Commands the teller can run here ─────────────────────────────────

  /**
   * The screen share has to happen on this side: a server cannot create a track, and the
   * browser will not start a capture for anyone but the page itself.
   *
   * Registered before connect, so a command that arrives immediately is not missed.
   */
  private registerCommands(): void {
    this.channel.handle('screenshare.start', async () => {
      await this.room?.localParticipant.setScreenShareEnabled(true);
      this.syncLocalState();
    });

    this.channel.handle('screenshare.stop', async () => {
      this.tellerStoppingShare = true;
      try {
        await this.room?.localParticipant.setScreenShareEnabled(false);
      } finally {
        this.tellerStoppingShare = false;
      }
      this.syncLocalState();
    });

    // The kiosk owns its state. Nothing is retained on the data channel, so a console that
    // reconnected - or joined late - has no way to have heard about it. It asks instead.
    this.channel.handle('state.get', async () => this.snapshot());
  }

  private snapshot(): KioskState {
    return {
      version: 1,
      screen: this.screen() === 'incall' ? 'incall' : this.screen() === 'waiting' ? 'waiting' : 'idle',
      sharing: this.sharing(),
      micOn: this.micOn(),
      camOn: this.camOn(),
      exitRequested: this.exitRequested(),
    };
  }

  /** A hint that something changed. The console still asks for the truth. */
  private announce(): void {
    void this.channel.broadcast(TOPICS.kioskState, this.snapshot());
  }

  /**
   * The customer asking to finish. It is a request, not an action: the teller may be part way
   * through something, and hanging up under them is the kiosk's decision to make least of all.
   */
  async requestExit(): Promise<void> {
    if (!this.channel.peer()) return;

    this.exitRequested.set(true);

    try {
      await this.channel.call('exit.request', undefined);
    } catch (e) {
      // The teller's console did not answer. Leave the button saying it was asked - the
      // customer has done their part, and a red error here helps nobody standing at a kiosk.
      // It is still logged: silence here is what made this hard to find the first time.
      console.error('exit.request was not acknowledged', e);
    }
  }

  // ── In-call ──────────────────────────────────────────────────────────
  //
  // There are no mute, camera or screen controls here on purpose. Nobody is standing at a
  // kiosk to manage a call; the teller holds all of that, and every one of those actions
  // reaches this room either server-side or through the RPC handlers above.

  /** Must be called from a real click. Calling it from a timer does nothing. */
  async enableAudio(): Promise<void> {
    await this.room?.startAudio();
    this.needsAudioGesture.set(false);
  }

  async endSession(): Promise<void> {
    const room = this.roomName();
    await this.room?.disconnect();

    if (room) {
      // Best effort: the customer walking away must not be blocked by a failed request.
      try {
        await this.del(`/api/sessions/${room}`, this.apiToken);
      } catch {
        /* the empty-timeout will collect it */
      }
    }

    this.reset();
  }

  /**
   * Get back into the room we were thrown out of.
   *
   * A session that is still open belongs to a customer who is still standing there and a teller
   * who is still waiting, so being dropped is not a reason to clear the screen. The API hands
   * out a fresh token for the same room; only a 404 - the session really has ended - sends this
   * kiosk back to idle.
   */
  private async rejoin(): Promise<void> {
    const roomName = this.roomName();

    // Not in a session, or deliberately ending one. Nothing to come back to.
    if (!roomName || this.screen() === 'idle') {
      this.reset();
      return;
    }

    this.reconnecting.set(true);
    this.statusText.set('Reconnecting…');

    // Back off, but never so far that someone is left staring at a frozen screen.
    const delay = Math.min(1000 * 2 ** this.rejoinAttempt, 10_000);
    this.rejoinAttempt += 1;

    clearTimeout(this.rejoinTimer);
    this.rejoinTimer = setTimeout(async () => {
      try {
        const res = await this.post<{ roomName: string; token: string; status: string }>(
          `/api/sessions/${roomName}/rejoin`,
          {},
          this.apiToken,
        );

        await this.join(res.token, roomName);

        this.rejoinAttempt = 0;
        this.reconnecting.set(false);
        this.statusText.set('');
      } catch (e) {
        // A 404 means the session is genuinely over - stop trying and go back to idle.
        if (e instanceof Error && e.message.includes('404')) {
          this.rejoinAttempt = 0;
          this.reset();
          return;
        }

        void this.rejoin();
      }
    }, delay);
  }

  private reset(): void {
    this.watchdog.stop();
    clearTimeout(this.rejoinTimer);
    this.rejoinAttempt = 0;
    this.reconnecting.set(false);
    this.room?.removeAllListeners();
    this.room = undefined;
    this.roomName.set('');
    this.tellerName.set('');
    this.remoteVideo.set(undefined);
    this.remoteAudio.set(undefined);
    this.localVideo.set(undefined);
    this.sharing.set(false);
    this.exitRequested.set(false);
    this.needsAudioGesture.set(false);
    this.statusText.set('');
    this.screen.set('idle');
  }

  // ── Plumbing ─────────────────────────────────────────────────────────

  private async post<T>(path: string, body: unknown, token?: string): Promise<T> {
    const res = await fetch(kioskConfig().apiBaseUrl + path, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body),
    });

    return this.unwrap<T>(res);
  }

  private async del(path: string, token: string): Promise<void> {
    await fetch(kioskConfig().apiBaseUrl + path, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${token}` },
    });
  }

  private async unwrap<T>(res: Response): Promise<T> {
    const body = (await res.json().catch(() => null)) as ApiEnvelope<T> | null;

    if (!res.ok || !body?.success || !body.data) {
      const detail = body?.errors
        ? Object.values(body.errors).flat().join(' ')
        : body?.message;
      // The status is part of the message on purpose: rejoin has to tell "the session has
      // ended" (404, stop) from "the API is having a moment" (anything else, keep trying).
      throw new Error(detail ? `${detail} (${res.status})` : `Request failed (${res.status}).`);
    }

    return body.data;
  }

  private describe(e: unknown): string {
    if (e instanceof TypeError) {
      // fetch throws TypeError when it cannot reach the host at all.
      return 'Cannot reach the VTM service. Check that the API is running.';
    }

    return e instanceof Error ? e.message : 'Something went wrong.';
  }

  private fail(message: string): void {
    this.errorText.set(message);
    this.screen.set('error');
  }
}
