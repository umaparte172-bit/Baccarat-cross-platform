import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/**
 * A stand-in for a live baccarat table, for testing the bettor in a real Chrome. It is laid out the way the real
 * thing is: the game is in a frame from another site, and the betting controls sit in a further frame inside it,
 * with margins, borders and padding on the frames so a click aimed with the wrong offset would miss.
 */

export type MockMode =
  | 'normal'
  /** Clicks on a spot are ignored: nothing is ever placed. */
  | 'reject'
  /** The table takes the chips but its total never changes. */
  | 'noconfirm'
  /** The table doubles every chip. */
  | 'double'
  /** The window closes after the first chip lands on a spot. */
  | 'closeafter1'
  /** A layer appears over the Player spot after the first chip lands, so the next click cannot reach it. */
  | 'coverafter1';

export interface Click {
  target: string;
  value?: string;
  trusted: boolean;
  x: number;
  y: number;
}

export interface MockBetting {
  state: {
    open: boolean;
    status: string;
    total: number;
    balance: number;
    mode: MockMode;
    cells: string[];
    selected: number | null;
    clicks: Click[];
    /** Put a layer on top of the Banker spot, so it cannot be clicked. */
    coverBanker: boolean;
    coverPlayer: boolean;
    /** The side the chips went to, in order. */
    placedOn: string[];
  };
  pageUrl: string;
  /** Wait until the browser has rendered the current status, total and overlays. */
  waitForUi: () => Promise<void>;
  stop: () => Promise<void>;
}

function listen(server: Server): Promise<number> {
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve((server.address() as AddressInfo).port)));
}

const CHIPS = [0.1, 1, 5, 25, 100];

export async function startMockBetting(): Promise<MockBetting> {
  const state: MockBetting['state'] = {
    open: true, status: 'PLACE YOUR BETS', total: 0, balance: 1000, mode: 'normal', cells: [], selected: null,
    clicks: [], coverBanker: false, coverPlayer: false, placedOn: [],
  };
  const uiState = (s: typeof state) => JSON.stringify([s.status, s.total, s.balance, s.coverBanker, s.coverPlayer]);
  let renderedState = '';

  const readBody = (req: IncomingMessage): Promise<string> =>
    new Promise((resolve) => {
      let text = '';
      req.on('data', (d) => (text += d));
      req.on('end', () => resolve(text));
    });

  const ui = `<!doctype html><meta charset="utf-8"><style>
    body{margin:0;font:14px sans-serif} .chip{display:inline-block;width:44px;height:44px;margin:6px;border-radius:22px;background:#ccc;text-align:center;line-height:44px;cursor:pointer}
    .spot{display:block;width:240px;height:70px;margin:12px;background:#9cf;text-align:center;line-height:70px;cursor:pointer} #banker{background:#f99}
    #cover{position:absolute;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,.3)} #wrap{position:relative;padding:8px}
  </style><body><div id="wrap">
    <div id="status" data-role="status"></div>
    <div id="chips">${CHIPS.map((v) => `<div class="chip" data-role="chipItem" data-value="${v}">${v}</div>`).join('')}</div>
    <div id="playerbox" style="position:relative"><div id="player" class="spot" data-role="player">PLAYER</div><div id="cover2" style="display:none;position:absolute;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,.3)"></div></div>
    <div id="bankerbox" style="position:relative"><div id="banker" class="spot" data-role="banker">BANKER</div><div id="cover" style="display:none"></div></div>
    <div id="total" data-role="total">0</div><div id="balance" data-role="balance">0</div>
  </div><script>
    let generation = 0;
    function render(s) {
      document.getElementById('status').textContent = s.status;
      document.getElementById('total').textContent = s.total ? '$' + s.total : '0';
      document.getElementById('balance').textContent = '$' + s.balance;
      document.getElementById('cover').style.display = s.coverBanker ? 'block' : 'none';
      document.getElementById('cover2').style.display = s.coverPlayer ? 'block' : 'none';
      return fetch('/rendered', {method: 'POST', body: JSON.stringify([s.status, s.total, s.balance, s.coverBanker, s.coverPlayer])});
    }
    // This loopback-only test fixture models a UI that commits each click immediately.
    // Serial synchronous requests prevent chip selection and placement being reordered,
    // and make overlays visible before the next test click. This is not production code.
    const log = (target, value, e) => {
      generation++;
      const request = new XMLHttpRequest();
      request.open('POST', '/click', false);
      request.send(JSON.stringify({target, value, trusted: e.isTrusted, x: e.clientX, y: e.clientY}));
      render(JSON.parse(request.responseText));
    };
    document.querySelectorAll('.chip').forEach(c => c.addEventListener('click', e => log('chip', c.dataset.value, e)));
    document.getElementById('player').addEventListener('click', e => log('player', undefined, e));
    document.getElementById('banker').addEventListener('click', e => log('banker', undefined, e));
    async function tick() {
      const before = generation;
      try {
        const s = await (await fetch('/state')).json();
        if (before === generation) await render(s);
      } catch (e) {}
      setTimeout(tick, 40);
    }
    tick();
  </script></body>`;

  const game = `<!doctype html><meta charset="utf-8"><body style="margin:0">
    <div data-role="Bead-road" id="road"></div>
    <iframe src="/ui" style="margin:20px 0 0 30px;border:3px solid blue;padding:9px;width:420px;height:560px"></iframe>
    <script>
      async function tick(){try{const s=await(await fetch('/state')).json();
        document.getElementById('road').innerHTML=s.cells.map(c=>'<div data-role="roadItem">'+c+'</div>').join('');}catch(e){}}
      setInterval(tick,40);tick();
    </script></body>`;

  const handle = async (req: IncomingMessage, res: ServerResponse): Promise<void> => {
    const url = req.url ?? '/';
    if (url === '/state') {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ status: state.status, total: state.total, balance: state.balance, cells: state.cells, coverBanker: state.coverBanker, coverPlayer: state.coverPlayer }));
    } else if (url === '/rendered' && req.method === 'POST') {
      renderedState = await readBody(req);
      res.end('ok');
    } else if (url === '/click' && req.method === 'POST') {
      const c = JSON.parse(await readBody(req)) as Click;
      state.clicks.push(c);
      if (c.target === 'chip') state.selected = Number(c.value);
      else if ((c.target === 'player' || c.target === 'banker') && state.open && state.selected !== null && state.mode !== 'reject') {
        state.placedOn.push(c.target);
        if (state.mode !== 'noconfirm') state.total = Math.round((state.total + state.selected * (state.mode === 'double' ? 2 : 1)) * 100) / 100;
        if (state.mode === 'coverafter1') state.coverPlayer = true;
        if (state.mode === 'closeafter1') {
          state.open = false;
          state.status = 'BETS CLOSED';
        }
      }
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify(state));
    } else if (url === '/ui') {
      res.setHeader('content-type', 'text/html');
      res.end(ui);
    } else {
      res.setHeader('content-type', 'text/html');
      res.end(game);
    }
  };

  const inner = createServer((req, res) => void handle(req, res));
  const innerPort = await listen(inner);
  const outer = createServer((_req, res) => {
    res.setHeader('content-type', 'text/html');
    res.end(`<!doctype html><title>Casino</title><body style="margin:0"><div style="height:40px;background:#223">lobby</div>
      <iframe src="http://localhost:${innerPort}/" style="margin:37px 0 0 53px;border:5px solid red;padding:11px;width:560px;height:760px"></iframe></body>`);
  });
  const outerPort = await listen(outer);

  return {
    state,
    pageUrl: `http://127.0.0.1:${outerPort}/`,
    waitForUi: async () => {
      const deadline = Date.now() + 5000;
      while (renderedState !== uiState(state)) {
        if (Date.now() > deadline) throw new Error(`Mock UI did not render ${uiState(state)}; last rendered ${renderedState}`);
        await new Promise((resolve) => setTimeout(resolve, 20));
      }
    },
    stop: async () => {
      inner.closeAllConnections();
      outer.closeAllConnections();
      await Promise.all([new Promise((r) => inner.close(r)), new Promise((r) => outer.close(r))]);
    },
  };
}

/** The profile that matches the mock table. */
export const MOCK_BETTING_PROFILE = {
  name: 'Mock table',
  containerSelector: '[data-role="Bead-road"]',
  itemSelector: '[data-role="roadItem"]',
  rules: [{ exact: 'P', result: 'P' }, { exact: 'B', result: 'B' }, { exact: 'T', result: 'T' }],
  hostContains: '',
  betting: {
    statusSelector: '[data-role="status"]',
    openText: 'PLACE YOUR BETS',
    chipSelector: '[data-role="chipItem"]',
    chipValueFrom: 'data-value',
    playerSelector: '[data-role="player"]',
    bankerSelector: '[data-role="banker"]',
    betTotalSelector: '[data-role="total"]',
    balanceSelector: '[data-role="balance"]',
  },
};
