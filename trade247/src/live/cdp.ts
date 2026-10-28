/**
 * A small Chrome DevTools Protocol client (no dependency): lists the open tabs of a Chrome that the user started
 * with --remote-debugging-port, and talks to one tab over its WebSocket. It only ever connects to 127.0.0.1.
 */

export interface CdpTarget {
  id: string;
  type: string;
  url: string;
  title: string;
  webSocketDebuggerUrl?: string;
}

export class CdpError extends Error {}

const LOOPBACK = '127.0.0.1';

/** The page tabs of the Chrome listening on this debugging port. */
export async function listPages(port: number, timeoutMs = 2500): Promise<CdpTarget[]> {
  let response: Response;
  try {
    response = await fetch(`http://${LOOPBACK}:${port}/json/list`, { signal: AbortSignal.timeout(timeoutMs) });
  } catch {
    throw new CdpError(`Cannot reach Chrome on port ${port}. Your usual Chrome cannot be read: click "Open Chrome for the table" (or double-click the Start-Chrome-for-Trade247 launcher) and open the table in the window that opens`);
  }
  if (!response.ok) throw new CdpError(`Chrome on port ${port} answered HTTP ${response.status}`);
  const all = (await response.json()) as CdpTarget[];
  return all.filter((t) => t.type === 'page' && t.webSocketDebuggerUrl && !t.url.startsWith('devtools://'));
}

export type CdpEventHandler = (method: string, params: any, sessionId: string | undefined) => void;

interface Pending {
  resolve: (value: any) => void;
  reject: (error: Error) => void;
  timer: NodeJS.Timeout;
}

export class CdpConnection {
  onEvent: CdpEventHandler = () => {};
  onClose: (reason: string) => void = () => {};
  closed = false;

  private nextId = 1;
  private readonly pending = new Map<number, Pending>();

  private constructor(private readonly ws: WebSocket) {
    ws.addEventListener('message', (event) => this.receive(String(event.data)));
    ws.addEventListener('close', () => this.shutdown('The connection to Chrome closed'));
    ws.addEventListener('error', () => this.shutdown('The connection to Chrome failed'));
  }

  static open(url: string, timeoutMs = 4000): Promise<CdpConnection> {
    const parsed = new URL(url);
    if (parsed.protocol !== 'ws:' || parsed.hostname !== LOOPBACK) {
      return Promise.reject(new CdpError('Only a Chrome on this computer (127.0.0.1) can be used'));
    }
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url);
      const timer = setTimeout(() => {
        ws.close();
        reject(new CdpError('Chrome did not accept the connection'));
      }, timeoutMs);
      ws.addEventListener('open', () => {
        clearTimeout(timer);
        resolve(new CdpConnection(ws));
      });
      ws.addEventListener('error', () => {
        clearTimeout(timer);
        reject(new CdpError('Chrome refused the connection'));
      });
    });
  }

  send<T = any>(method: string, params: object = {}, sessionId?: string, timeoutMs = 5000): Promise<T> {
    if (this.closed) return Promise.reject(new CdpError('The connection to Chrome is closed'));
    const id = this.nextId++;
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new CdpError(`${method} timed out`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject: (e) => reject(e), timer });
      const message: Record<string, unknown> = { id, method, params };
      if (sessionId) message.sessionId = sessionId;
      try {
        this.ws.send(JSON.stringify(message));
      } catch (e) {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(new CdpError((e as Error).message));
      }
    });
  }

  close(): void {
    if (this.closed) return;
    this.shutdown('closed');
    try {
      this.ws.close();
    } catch {
      /* already gone */
    }
  }

  private receive(raw: string): void {
    let message: any;
    try {
      message = JSON.parse(raw);
    } catch {
      return;
    }
    if (typeof message.id === 'number') {
      const waiting = this.pending.get(message.id);
      if (!waiting) return;
      this.pending.delete(message.id);
      clearTimeout(waiting.timer);
      if (message.error) waiting.reject(new CdpError(String(message.error.message ?? 'Chrome reported an error')));
      else waiting.resolve(message.result ?? {});
    } else if (typeof message.method === 'string') {
      this.onEvent(message.method, message.params ?? {}, message.sessionId);
    }
  }

  private shutdown(reason: string): void {
    if (this.closed) return;
    this.closed = true;
    for (const [, p] of this.pending) {
      clearTimeout(p.timer);
      p.reject(new CdpError(reason));
    }
    this.pending.clear();
    this.onClose(reason);
  }
}
