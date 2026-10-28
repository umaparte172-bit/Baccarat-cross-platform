import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { listPages } from './cdp.js';
import { PageSession } from './page.js';

/**
 * A calibration capture: what the table page looks like, so the selectors in the profile can be checked or fixed
 * without anyone needing the user's account. It records element roles and short labels only. Page addresses are cut
 * back to the site (no path, query or fragment, which can carry session tokens), and fields that look like
 * balances, names or accounts are not read.
 */

const collector = function () {
  const SKIP = /balance|user|name|email|account|profile|wallet|avatar|chat/i;
  const BANNER = /^(PLACE YOUR BETS|BETS (OPEN|CLOSING|CLOSED|ACCEPTED)|DEALING|WAIT|WINS?|NO MORE BETS)/i;
  const roles: Record<string, { count: number; visible: number; samples: string[] }> = {};
  const nodes = document.querySelectorAll('[data-role]');
  for (let i = 0; i < nodes.length && i < 4000; i++) {
    const el = nodes[i] as HTMLElement;
    const role = el.getAttribute('data-role') || '';
    const entry = roles[role] || (roles[role] = { count: 0, visible: 0, samples: [] });
    entry.count++;
    const rect = el.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) entry.visible++;
    if (entry.samples.length < 6 && !SKIP.test(role)) {
      entry.samples.push(String(el.innerText || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 30));
    }
  }
  const banners: string[] = [];
  const all = document.querySelectorAll('div,span,p');
  for (let i = 0; i < all.length && banners.length < 12; i++) {
    const el = all[i] as HTMLElement;
    if (el.children.length > 0) continue;
    const t = String(el.textContent || '').trim();
    if (t.length > 0 && t.length < 40 && BANNER.test(t)) {
      const holder = el.closest('[data-role]');
      banners.push(t + (holder ? '  [data-role=' + holder.getAttribute('data-role') + ']' : ''));
    }
  }
  return { roles, banners, nodes: nodes.length };
};

export interface InspectionResult {
  path: string;
  tabs: number;
  frames: number;
  withRoles: number;
}

function site(url: string): string {
  try {
    const u = new URL(url);
    return `${u.protocol}//${u.host}`;
  } catch {
    return '(unknown)';
  }
}

export async function inspectTables(port: number, outDir: string, now = new Date()): Promise<InspectionResult> {
  const pages = await listPages(port);
  const tabs: unknown[] = [];
  let frames = 0;
  let withRoles = 0;
  for (const target of pages) {
    let session: PageSession | null = null;
    try {
      session = await PageSession.attach(target, 700);
      const found = await session.evaluateEverywhere<ReturnType<typeof collector>>(`(${collector.toString()})()`);
      frames += found.length;
      const useful = found.filter((f) => f.value.nodes > 0);
      withRoles += useful.length;
      tabs.push({ site: site(target.url), frames: found.length, framesWithRoles: useful.map((f) => ({ origin: f.origin, ...f.value })) });
    } catch (e) {
      tabs.push({ site: site(target.url), error: (e as Error).message });
    } finally {
      session?.close();
    }
  }
  mkdirSync(outDir, { recursive: true });
  const stamp = now.toISOString().replace(/[:.]/g, '-');
  const path = join(outDir, `table-inspection-${stamp}.json`);
  writeFileSync(path, JSON.stringify({ tool: 'TRADE247 table inspection', taken: now.toISOString(), tabs }, null, 2), 'utf8');
  return { path, tabs: pages.length, frames, withRoles };
}
