import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { listPages } from './cdp.js';
import { PageSession } from './page.js';
import { loadProfiles, type LiveProfile } from './profile.js';

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

/**
 * A read-only check of the table profile against the open table: what the program can read of the road and of the
 * betting window. It clicks nothing. Used to calibrate the selectors before any bet is allowed.
 */
export async function checkTable(port: number, dataDir: string): Promise<string[]> {
  const profiles = loadProfiles(dataDir);
  if ('error' in profiles) throw new Error(profiles.error);
  const lines: string[] = [`Profiles tried: ${profiles.map((p) => p.name).join('; ')}`];
  const pages = await listPages(port);
  let tables = 0;
  for (const target of pages) {
    let session: PageSession | null = null;
    try {
      session = await PageSession.attach(target, 700);
      let profile: LiveProfile = profiles[0]!;
      let road = null;
      let window = null;
      for (const candidate of profiles) {
        road = await session.readRoad(candidate);
        window = candidate.betting ? await session.readWindow(candidate.betting) : null;
        if (road || window) {
          profile = candidate;
          break;
        }
      }
      if (!road && !window) continue;
      tables++;
      lines.push(`Tab ${tables}: ${site(target.url)} (profile: ${profile.name}${profile.betting ? ', with betting settings' : ', read only: no betting settings'})`);
      if (road) {
        lines.push(`  Road: ${road.items.length} hands${road.unreadable ? ' (some cells are NOT recognised - fix the rules)' : ''}, last ${road.items.slice(-12).join('') || '-'}`);
      } else lines.push('  Road: not found (check containerSelector / itemSelector)');
      if (profile.betting) {
        if (!window) lines.push('  Betting window: not found (check betting.statusSelector)');
        else {
          const w = window.reading;
          lines.push(`  Status: "${w.text}" -> bets ${w.open ? 'OPEN' : 'closed'}`);
          lines.push(`  Chips: ${w.chips.filter((c) => c.visible).map((c) => c.value ?? '?').join(', ') || 'none found (check betting.chipSelector / chipValueFrom)'}`);
          lines.push(`  Spots: Player ${w.hasPlayer ? 'found' : 'NOT found'}, Banker ${w.hasBanker ? 'found' : 'NOT found'}`);
          lines.push(`  Total on the table: ${w.total ?? 'NOT readable (check betting.betTotalSelector)'}`);
          lines.push(`  Balance: ${profile.betting.balanceSelector ? (w.balance ?? 'NOT readable') : '(not configured)'}`);
        }
      }
    } catch (e) {
      lines.push(`A tab could not be inspected: ${(e as Error).message}`);
    } finally {
      session?.close();
    }
  }
  if (tables === 0) lines.push('No open tab shows a table that matches the profile. Open a baccarat table in that Chrome window and run this again.');
  return lines;
}
