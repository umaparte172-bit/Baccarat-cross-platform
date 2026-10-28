import type { Result } from '../engine/types.js';
import { CdpConnection, CdpError, type CdpTarget } from './cdp.js';
import { probeScript, type LiveProfile } from './profile.js';

interface Context {
  /** undefined = the tab itself, otherwise the id of the attached frame process. */
  session: string | undefined;
  id: number;
  origin: string;
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
  private readonly contexts = new Map<string, Context>();

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
        // Frames from other sites live in their own process: enable scripting events there and look for nested ones.
        void this.conn.send('Runtime.enable', {}, child).catch(() => {});
        void this.conn
          .send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true }, child)
          .catch(() => {});
        break;
      }
      case 'Target.detachedFromTarget':
        this.dropSession(params.sessionId);
        break;
      case 'Runtime.executionContextCreated': {
        const c = params.context;
        if (c?.auxData?.isDefault === false) return;
        this.contexts.set(key(sessionId, c.id), { session: sessionId, id: c.id, origin: String(c.origin ?? '') });
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

  /** Runs a script in every frame; frames that fail (navigating, gone) are skipped. */
  async evaluateEverywhere<T>(expression: string): Promise<Array<{ origin: string; value: T }>> {
    const settled = await Promise.allSettled(
      [...this.contexts.values()].map(async (ctx) => {
        const r = await this.conn.send<any>(
          'Runtime.evaluate',
          { expression, contextId: ctx.id, returnByValue: true },
          ctx.session,
          3000,
        );
        if (r.exceptionDetails || r.result?.value === undefined) throw new CdpError('no value');
        return { origin: ctx.origin, value: r.result.value as T };
      }),
    );
    const out: Array<{ origin: string; value: T }> = [];
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
}
