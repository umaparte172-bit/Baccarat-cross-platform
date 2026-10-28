import type { Result } from '../engine/types.js';
import { CdpConnection, CdpError, type CdpTarget } from './cdp.js';
import { probeScript, type BettingProfile, type LiveProfile } from './profile.js';
import { CHECK_TARGET, windowScript, type TargetCheck, type WindowReading } from './table.js';

/** One frame of the tab where scripts can run. */
export interface FrameContext {
  /** undefined = the tab itself, otherwise the id of the attached frame process. */
  session: string | undefined;
  id: number;
  origin: string;
  frameId: string;
}

/** A frame that lives in its own process: which session talks to it, and which one holds the <iframe> that shows it. */
interface RemoteFrame {
  frameId: string;
  parent: string | undefined;
}

export interface RoadReading {
  /** The container exists in some frame of the tab. */
  container: boolean;
  items: Result[];
  /** A cell no rule recognised: the reading cannot be trusted. */
  unreadable: boolean;
  /** Where the road was found, without any address details. */
  origin: string;
}

const key = (session: string | undefined, id: number): string => `${session ?? ''}:${id}`;

/**
 * One browser tab, with every frame in it. The table is usually inside nested frames from another site, so all
 * frames are enabled for scripting and the script is run in each one.
 */
export class PageSession {
  private readonly contexts = new Map<string, FrameContext>();
  private readonly remote = new Map<string, RemoteFrame>();

  private constructor(
    readonly conn: CdpConnection,
    readonly target: CdpTarget,
  ) {
    conn.onEvent = (method, params, sessionId) => this.onEvent(method, params, sessionId);
  }

  static async attach(target: CdpTarget, settleMs = 350): Promise<PageSession> {
    if (!target.webSocketDebuggerUrl) throw new CdpError('The tab cannot be inspected');
    const conn = await CdpConnection.open(target.webSocketDebuggerUrl);
    const page = new PageSession(conn, target);
    try {
      await conn.send('Runtime.enable');
      await conn.send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true });
    } catch (e) {
      conn.close();
      throw e;
    }
    // The execution contexts of existing frames are announced right after the commands above.
    await new Promise((r) => setTimeout(r, settleMs));
    return page;
  }

  get closed(): boolean {
    return this.conn.closed;
  }

  close(): void {
    this.conn.close();
  }

  private onEvent(method: string, params: any, sessionId: string | undefined): void {
    switch (method) {
      case 'Target.attachedToTarget': {
        const child: string | undefined = params.sessionId;
        if (!child) return;
        if (params.targetInfo?.targetId) this.remote.set(child, { frameId: String(params.targetInfo.targetId), parent: sessionId });
        // Frames from other sites live in their own process: enable scripting events there and look for nested ones.
        void this.conn.send('Runtime.enable', {}, child).catch(() => {});
        void this.conn
          .send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true }, child)
          .catch(() => {});
        break;
      }
      case 'Target.detachedFromTarget':
        this.dropSession(params.sessionId);
        this.remote.delete(params.sessionId);
        break;
      case 'Runtime.executionContextCreated': {
        const c = params.context;
        if (c?.auxData?.isDefault === false) return;
        this.contexts.set(key(sessionId, c.id), { session: sessionId, id: c.id, origin: String(c.origin ?? ''), frameId: String(c.auxData?.frameId ?? '') });
        break;
      }
      case 'Runtime.executionContextDestroyed':
        this.contexts.delete(key(sessionId, params.executionContextId));
        break;
      case 'Runtime.executionContextsCleared':
        this.dropSession(sessionId);
        break;
    }
  }

  private dropSession(session: string | undefined): void {
    for (const [k, c] of this.contexts) if (c.session === session) this.contexts.delete(k);
  }

  /** Which table providers have a frame in this tab (by the site of the frame). */
  providers(): string[] {
    const found = new Set<string>();
    for (const c of this.contexts.values()) {
      const o = c.origin.toLowerCase();
      if (o.includes('pragmaticplay')) found.add('Pragmatic Play');
      else if (o.includes('evolution') || o.includes('evo-games') || o.includes('evolive')) found.add('Evolution');
    }
    return [...found];
  }

  /** Runs a script in every frame; frames that fail (navigating, gone) are skipped. */
  async evaluateEverywhere<T>(expression: string): Promise<Array<{ origin: string; value: T }>> {
    return (await this.evaluateInFrames<T>(expression)).map(({ frame, value }) => ({ origin: frame.origin, value }));
  }

  /** Like evaluateEverywhere, but says which frame each answer came from (so a click can be aimed at it). */
  async evaluateInFrames<T>(expression: string): Promise<Array<{ frame: FrameContext; value: T }>> {
    const settled = await Promise.allSettled(
      [...this.contexts.values()].map(async (ctx) => {
        const r = await this.conn.send<any>(
          'Runtime.evaluate',
          { expression, contextId: ctx.id, returnByValue: true },
          ctx.session,
          3000,
        );
        if (r.exceptionDetails || r.result?.value === undefined) throw new CdpError('no value');
        return { frame: ctx, value: r.result.value as T };
      }),
    );
    const out: Array<{ frame: FrameContext; value: T }> = [];
    for (const s of settled) if (s.status === 'fulfilled') out.push(s.value);
    return out;
  }

  /** The road as the best frame shows it, or null when no frame has the table. */
  async readRoad(profile: LiveProfile): Promise<RoadReading | null> {
    const found = await this.evaluateEverywhere<{ container: boolean; items: string[] }>(probeScript(profile));
    const tables = found.filter((f) => f.value.container);
    if (tables.length === 0) return null;
    // If more than one frame has a table (a lobby preview next to the game), the one with the longest road wins.
    tables.sort((a, b) => b.value.items.length - a.value.items.length);
    const best = tables[0]!;
    const items: Result[] = [];
    let unreadable = false;
    for (const i of best.value.items) {
      if (i === 'P' || i === 'B' || i === 'T') items.push(i);
      else unreadable = true;
    }
    return { container: true, items, unreadable, origin: best.origin };
  }

  // ---------------------------------------------------------------- the betting window and real mouse clicks

  /** The frame that holds the betting window: the one with the status element (and the most chips, if several). */
  async readWindow(b: BettingProfile): Promise<WindowSnapshot | null> {
    const found = await this.evaluateInFrames<WindowReading>(windowScript(b));
    const hits = found.filter((f) => f.value.found);
    if (hits.length === 0) return null;
    hits.sort((x, y) => y.value.chips.length - x.value.chips.length);
    return { frame: hits[0]!.frame, reading: hits[0]!.value };
  }

  async bringToFront(): Promise<void> {
    await this.conn.send('Page.bringToFront');
  }

  /**
   * A real mouse click (move, press, release) on the element, at its centre. The point is worked out in the tab's own
   * coordinates: inside a frame from another site the element's position is relative to that frame, so the position
   * of each <iframe> that holds it is added up the chain. Refuses rather than clicks when the element is hidden,
   * disabled, or covered by something else.
   */
  async clickElement(frame: FrameContext, selector: string, index = 0): Promise<void> {
    // The session is always explicit: undefined means the tab itself, which is not the same as "this frame".
    const send = <T = any>(session: string | undefined, method: string, params: object = {}): Promise<T> =>
      this.conn.send<T>(method, params, session, 4000);

    const found = await send<any>(frame.session, 'Runtime.evaluate', {
      expression: `document.querySelectorAll(${JSON.stringify(selector)})[${Math.max(0, Math.floor(index))}]`,
      contextId: frame.id,
    });
    const objectId: string | undefined = found.result?.objectId;
    if (!objectId) throw new CdpError(`Cannot find "${selector}" on the table`);

    const checked = await send<any>(frame.session, 'Runtime.callFunctionOn', { objectId, functionDeclaration: CHECK_TARGET, returnByValue: true });
    const check = checked.result?.value as TargetCheck | undefined;
    if (!check?.visible) throw new CdpError(`"${selector}" is not visible`);
    if (check.disabled) throw new CdpError(`"${selector}" is disabled`);
    if (check.covered) throw new CdpError(`"${selector}" is covered by something else on the page`);

    const quads = await send<any>(frame.session, 'DOM.getContentQuads', { objectId });
    const quad: number[] | undefined = quads.quads?.[0];
    if (!quad || quad.length < 8) throw new CdpError(`Cannot locate "${selector}" on the screen`);
    let x = (quad[0]! + quad[2]! + quad[4]! + quad[6]!) / 4;
    let y = (quad[1]! + quad[3]! + quad[5]! + quad[7]!) / 4;

    // Add the position of every <iframe> that sits between this frame's process and the tab.
    let session = frame.session;
    for (let hops = 0; session !== undefined; hops++) {
      const remote = this.remote.get(session);
      if (!remote || hops > 8) throw new CdpError('Cannot work out where the table frame is on the page');
      await send(remote.parent, 'Page.enable');
      await send(remote.parent, 'DOM.enable');
      const owner = await send<any>(remote.parent, 'DOM.getFrameOwner', { frameId: remote.frameId });
      const box = await send<any>(remote.parent, 'DOM.getBoxModel', { backendNodeId: owner.backendNodeId });
      const content: number[] | undefined = box.model?.content;
      if (!content || content.length < 8) throw new CdpError('Cannot measure the frame that holds the table');
      x += content[0]!;
      y += content[1]!;
      session = remote.parent;
    }

    const point = { x: Math.round(x), y: Math.round(y) };
    const mouse = (type: string, extra: object = {}): Promise<unknown> =>
      this.conn.send('Input.dispatchMouseEvent', { type, x: point.x, y: point.y, button: 'left', ...extra }, undefined, 4000);
    await mouse('mouseMoved', { button: 'none' });
    await mouse('mousePressed', { buttons: 1, clickCount: 1 });
    await mouse('mouseReleased', { buttons: 0, clickCount: 1 });
  }
}

export interface WindowSnapshot {
  frame: FrameContext;
  reading: WindowReading;
}
