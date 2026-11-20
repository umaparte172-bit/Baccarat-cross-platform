import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';
import type { AddressInfo, Socket } from 'node:net';

export type Behavior = 'ok' | 'unauthorized' | 'html500' | 'hang' | 'redirect' | 'echo-token';

export interface Hit {
  method: string;
  url: string;
  headers: IncomingHttpHeaders;
  body: any;
}

/** A local stand-in for api.telegram.org that records every request and can misbehave on demand. */
export interface FakeTelegram {
  baseUrl: string;
  hits: Hit[];
  /** Bodies of the messages received on /bot<token>/sendMessage. */
  texts(): string[];
  setBehavior(b: Behavior): void;
  /** Queue an update for getUpdates (update_id is assigned). */
  pushUpdate(update: Record<string, unknown>): void;
  close(): Promise<void>;
}

export async function fakeTelegram(initial: Behavior = 'ok'): Promise<FakeTelegram> {
  let behavior = initial;
  const hits: Hit[] = [];
  const updates: Array<Record<string, unknown> & { update_id: number }> = [];
  let nextUpdate = 1;
  const sockets = new Set<Socket>();

  const server: Server = createServer((req, res) => {
    const chunks: Buffer[] = [];
    req.on('data', (c: Buffer) => chunks.push(c));
    req.on('end', () => {
      let body: any = null;
      try {
        body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
      } catch {
        body = Buffer.concat(chunks).toString('utf8');
      }
      hits.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers, body });
      const send = (status: number, type: string, payload: string): void => {
        res.writeHead(status, { 'content-type': type });
        res.end(payload);
      };
      if (behavior === 'ok' && req.url?.endsWith('/getUpdates')) {
        const offset = Number(body?.offset ?? 0);
        const pending = updates.filter((u) => u.update_id >= offset);
        // Like Telegram's long poll, but short: answer at once when there is something, otherwise after a moment.
        const answer = (): void => send(200, 'application/json', JSON.stringify({ ok: true, result: updates.filter((u) => u.update_id >= offset) }));
        if (pending.length) return answer();
        return void setTimeout(answer, 30);
      }
      switch (behavior) {
        case 'ok':
          return send(200, 'application/json', JSON.stringify({ ok: true, result: { message_id: hits.length } }));
        case 'unauthorized':
          return send(401, 'application/json', JSON.stringify({ ok: false, error_code: 401, description: 'Unauthorized' }));
        case 'html500':
          return send(500, 'text/html', '<html><body>Internal Server Error</body></html>');
        case 'redirect':
          res.writeHead(302, { location: '/somewhere-else' });
          return void res.end();
        case 'echo-token':
          return send(401, 'application/json', JSON.stringify({ ok: false, description: `bad request to ${req.url}` }));
        case 'hang':
          return; // never answer
      }
    });
  });
  server.on('connection', (s) => {
    sockets.add(s);
    s.on('close', () => sockets.delete(s));
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;

  return {
    baseUrl: `http://127.0.0.1:${port}`,
    hits,
    texts: () => hits.filter((h) => h.url.endsWith('/sendMessage')).map((h) => String(h.body?.text ?? '')),
    setBehavior: (b) => {
      behavior = b;
    },
    pushUpdate: (update) => {
      updates.push({ ...update, update_id: nextUpdate++ });
    },
    close: () =>
      new Promise<void>((resolve) => {
        for (const s of sockets) s.destroy();
        server.close(() => resolve());
      }),
  };
}

export const TOKEN = '123456789:AAFakeTokenForTestsOnly_0123456789';
export const CHAT = '4242';
