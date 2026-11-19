import { readFileSync } from 'node:fs';
import { Script, createContext, runInContext } from 'node:vm';
import { describe, expect, it } from 'vitest';

const html = readFileSync(new URL('../ui/index.html', import.meta.url), 'utf8');
const script = html.split('<script>')[1]!.split('</script>')[0]!;

it('parses the entire dashboard script', () => {
  expect(() => new Script(script)).not.toThrow();
});

describe('Compare Settings controls', () => {
  function fixture() {
    const elements = new Map<string, any>();
    const requests: any[] = [];
    const copied: any[] = [];
    const element = (key: string) => {
      if (!elements.has(key)) elements.set(key, {
        value: '', textContent: '', innerHTML: '', style: {}, disabled: false,
        classList: { add() {} },
        querySelectorAll(selector: string) {
          const attribute = selector.slice(1, -1);
          return Array.from(this.innerHTML.matchAll(new RegExp(attribute + '="([^"]+)"', 'g')), (m: any) => element(selector + m[1]));
        },
        dataset: key.startsWith('[data-use]') ? { use: key.slice(10) } : key.startsWith('[data-sort]') ? { sort: key.slice(11) } : {},
      });
      return elements.get(key)!;
    };
    const settings = { unit_value: 1, entry_loss_filter: 0, single_wins_virtual: 0, banker_cover: false, hard_cap_mode: 'off', hard_cap_units: 100, stop_loss: 0, take_profit: 0 };
    const params = { source: 'sim', trials: 2, shoes: 2, seed: 5 };
    const context = createContext({
      $: element, cur: { settings }, collect: () => ({ ...settings }), btParams: () => params,
      fm: (n: number) => String(n), fillForm: (s: any) => copied.push(s), toast() {},
      api: async (path: string, body: any) => {
        requests.push({ path, body });
        return path === '/api/backtest/start' ? { ok: true, id: String(requests.length) } : {
          finished: true, done: 2, total: 2, result: { ruin_pct: 0, mean: 2, median: 1, p10: -1, p90: 3, profitable_pct: 50, mdd_worst: 2, max_stake_worst: 3, return_on_turnover_pct: 5 },
        };
      },
    });
    runInContext('let btJob=null,sweepRunning=false,sweepCancel=false;' + script.slice(script.indexOf('const sweepValues'), script.indexOf('const num =')), context);
    element('#sw_unit').value = '1, 2';
    element('#sw_capital').value = '1000';
    return { element, requests, copied, params };
  }

  it('converts the same dollar capital per unit, renders numeric results, and USE only copies', async () => {
    const { element, requests, copied } = fixture();
    await element('#btCompare').onclick();
    const starts = requests.filter(x => x.path === '/api/backtest/start');
    expect(starts.map(x => x.body.params.bankroll_units)).toEqual([1000, 500]);
    expect(starts.map(x => x.body.params.seed)).toEqual([5, 5]);
    expect(element('#btOut').innerHTML).not.toContain('NaN');
    expect(element('#btMsg').textContent).toBe('Finished · 2 combinations');
    element('[data-sort]unit').onclick();
    element('[data-use]1').onclick();
    expect(copied).toHaveLength(1);
    expect(copied[0].unit_value).toBe(1); // Better dollar P10 ranks first when ruin rates tie.
    expect(requests.every(x => !x.path.includes('/apply'))).toBe(true);
  });

  it.each([['#sw_unit', '1, bad'], ['#sw_vb', '0.5'], ['#sw_bc', '2'], ['#sw_hc', '0.2']])('rejects invalid %s before starting work', async (id, value) => {
    const { element, requests } = fixture();
    element(id).value = value;
    await element('#btCompare').onclick();
    expect(requests).toHaveLength(0);
    expect(element('#btMsg').textContent.length).toBeGreaterThan(0);
  });

  it('enforces the comparison trial limit before starting work', async () => {
    const { element, requests, params } = fixture();
    params.trials = 501;
    await element('#btCompare').onclick();
    expect(requests).toHaveLength(0);
    expect(element('#btMsg').textContent).toContain('500');
  });
});

// Exercise the actual chart logic without a graphical desktop. Selenium additionally tests real canvases.
describe('P&L chart interaction model', () => {
  function fixture() {
    const elements = new Map<string, any>();
    const callbacks: Record<string, (event: any) => void> = {};
    const coordinates: number[] = [];
    const ctx = new Proxy({} as any, { get: (_obj, key) => (...args: any[]) => {
      if (['moveTo', 'lineTo', 'rect', 'fillRect', 'strokeRect'].includes(String(key))) coordinates.push(...args);
    } });
    const element = (selector: string) => {
      if (!elements.has(selector)) {
        const classes = new Set(selector === '#pnlChart' ? ['hide'] : []);
        elements.set(selector, {
          classList: { contains: (c: string) => classes.has(c), add: (c: string) => classes.add(c), remove: (c: string) => classes.delete(c) },
          textContent: '', clientWidth: 800, dataset: { chartRange: '0' },
          setAttribute() {}, focus() {}, setPointerCapture() {},
          getBoundingClientRect: () => ({ width: 800, height: 280, left: 0 }),
          getContext: () => ctx, addEventListener() {},
        });
      }
      return elements.get(selector)!;
    };
    const context = createContext({
      $: element, curSession: 1, cur: { settings: { start_balance: 1000 } },
      allRows: [1, -4, 0.095].map((pnl, i) => ({ n: i + 1, session_pnl: String(pnl), time: '12:00' })),
      fm: (n: number) => String(n), money: (n: number) => String(n),
      document: { querySelectorAll: () => [element('[data-chart-range]')], addEventListener: (name: string, fn: any) => { callbacks[name] = fn; } },
      window: { devicePixelRatio: 2, addEventListener() {} }, setInterval() {}, setTimeout() {}, clearTimeout() {},
    });
    const chart = script.slice(script.indexOf('// The chart consumes'), script.indexOf('async function tick()'));
    runInContext(chart, context);
    return { element, callbacks, context, coordinates };
  }

  it('shows exact P&L, extrema and drawdown; zooms and closes on Escape', () => {
    const { element, callbacks, coordinates } = fixture();
    element('#pnlChartBtn').onclick();
    expect(element('#chartStats').textContent).toBe('Now $0.095 · Highest $1 · Lowest $-4 · Largest fall $5 · Hands 3');
    element('#chartZoomIn').onclick();
    element('#chartZoomOut').onclick();
    element('[data-chart-range]').onclick();
    expect(coordinates.length).toBeGreaterThan(0);
    expect(coordinates.every(Number.isFinite)).toBe(true);
    callbacks.keydown!({ key: 'Escape' });
    expect(element('#pnlChart').classList.contains('hide')).toBe(true);
  });

  it('clears the old chart when a new session starts', () => {
    const { element, context } = fixture();
    element('#pnlChartBtn').onclick();
    element('#chartClose').onclick();
    runInContext('curSession=2; allRows=[]', context);
    element('#pnlChartBtn').onclick();
    expect(element('#chartStats').textContent).toBe('Now $0 · Highest $0 · Lowest $0 · Largest fall $0 · Hands 0');
  });
});
