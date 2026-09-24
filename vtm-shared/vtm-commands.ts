/**
 * The teller ↔ kiosk contract.
 *
 * Both apps import this file. Neither declares a method name of its own, which is the point:
 * a name that exists on only one side is a runtime failure nobody sees until a teller presses
 * the button. Here it is a compile error.
 *
 * Adding a command is one entry in COMMANDS and one in Payloads. See
 * docs/06-kiosk-command-set.md.
 */

/** What the kiosk is doing. The kiosk owns this; everyone else asks for it. */
export interface KioskState {
  /** Bumped when the shape changes, so an old kiosk talking to a new console is detectable. */
  version: 1;
  screen: 'idle' | 'waiting' | 'incall';
  sharing: boolean;
  micOn: boolean;
  camOn: boolean;
  /** Set once the customer has asked to finish. */
  exitRequested: boolean;
}

/**
 * Class 1 and 2 come from PROJECT.md D-010.
 *
 * **1** — UI and state. Peer to peer, not logged: what matters legally is the committed
 * transaction, not the keystrokes leading to it.
 *
 * **2** — real-world side effects (card reader, printer, cash drawer). These can happen in a
 * session that is then abandoned, leaving no submission and therefore no record, so they go
 * through the API and are logged whether or not anything is submitted.
 */
interface CommandMeta {
  /** 1 = peer to peer, not logged. 2 = through the API and logged. See D-010. */
  cls: 1 | 2;
  direction: 'to-kiosk' | 'to-teller';
}

export const COMMANDS: Record<string, CommandMeta> = {
  'screenshare.start': { cls: 1, direction: 'to-kiosk' },
  'screenshare.stop': { cls: 1, direction: 'to-kiosk' },
  'state.get': { cls: 1, direction: 'to-kiosk' },
  'exit.request': { cls: 1, direction: 'to-teller' },
  'screenshare.ended': { cls: 1, direction: 'to-teller' },
};

/** The names, taken from the payload map so the two can never drift apart. */
export type CommandName = keyof Payloads;

/** Request and response types, per command. `void` means no payload. */
export interface Payloads {
  'screenshare.start': { req: void; res: void };
  'screenshare.stop': { req: void; res: void };
  'state.get': { req: void; res: KioskState };
  'exit.request': { req: void; res: void };

  /**
   * The kiosk's screen share stopped without the teller asking for it - in practice the
   * customer pressed the browser's own "Stop sharing". That bar is the browser telling the
   * customer their screen is being watched, and it stays: stopping is their right. This is
   * how the teller finds out, rather than watching the tile quietly disappear.
   */
  'screenshare.ended': { req: { by: 'customer' }; res: void };
}

/** Broadcast topics. Unlike RPC these are fire-and-forget and nobody has to be listening. */
export const TOPICS = {
  /** The kiosk announcing its state changed. Not a substitute for state.get - see the channel. */
  kioskState: 'vtm.kiosk-state',
} as const;
