import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Result } from '../engine/types.js';

/**
 * How to read a table from the page. Selectors drift when the provider changes its markup, so the profile can be
 * replaced without a new program: put a `live-profile.json` in the data folder.
 */
export interface LiveRule {
  /** The cell's text equals this (case-insensitive). */
  exact?: string;
  /** The cell's text, aria-label or class contains this (case-insensitive). */
  contains?: string;
  result: Result;
}

export interface LiveProfile {
  name: string;
  /** A frame is "the table" when this element exists in it. */
  containerSelector: string;
  /** The result cells inside the container, oldest first. */
  itemSelector: string;
  rules: LiveRule[];
  /** Optional: only tabs whose address contains this text are considered. */
  hostContains: string;
}

/** Evolution baccarat bead road, as seen on a Roobet table (text P / B / T per cell, oldest first). Unverified today. */
export const EVOLUTION_BEAD_ROAD: LiveProfile = {
  name: 'Evolution baccarat (bead road)',
  containerSelector: '[data-role="Bead-road"]',
  itemSelector: '[data-role="roadItem"]',
  rules: [
    { exact: 'P', result: 'P' },
    { exact: 'B', result: 'B' },
    { exact: 'T', result: 'T' },
  ],
  hostContains: '',
};

export const PROFILE_FILE = 'live-profile.json';

const SELECTOR_MAX = 500;

function selectorProblem(name: string, value: unknown): string | null {
  if (typeof value !== 'string' || value.trim() === '') return `"${name}" must be a non-empty CSS selector`;
  if (value.length > SELECTOR_MAX) return `"${name}" is too long`;
  return null;
}

/** Checks a profile read from a file; the page script only ever receives values that passed this. */
export function parseProfile(raw: unknown): LiveProfile | { error: string } {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return { error: 'The profile must be a JSON object' };
  const o = raw as Record<string, unknown>;
  for (const key of ['containerSelector', 'itemSelector']) {
    const problem = selectorProblem(key, o[key]);
    if (problem) return { error: problem };
  }
  if (!Array.isArray(o.rules) || o.rules.length === 0 || o.rules.length > 50) {
    return { error: '"rules" must list between 1 and 50 rules' };
  }
  const rules: LiveRule[] = [];
  for (const r of o.rules as unknown[]) {
    const rule = r as Record<string, unknown>;
    if (typeof rule !== 'object' || rule === null) return { error: 'Each rule must be an object' };
    if (rule.result !== 'P' && rule.result !== 'B' && rule.result !== 'T') return { error: 'Each rule needs "result": "P", "B" or "T"' };
    const hasExact = typeof rule.exact === 'string' && rule.exact.length > 0 && rule.exact.length <= 80;
    const hasContains = typeof rule.contains === 'string' && rule.contains.length > 0 && rule.contains.length <= 80;
    if (hasExact === hasContains) return { error: 'Each rule needs exactly one of "exact" or "contains"' };
    rules.push(hasExact ? { exact: rule.exact as string, result: rule.result } : { contains: rule.contains as string, result: rule.result });
  }
  const host = o.hostContains === undefined ? '' : o.hostContains;
  if (typeof host !== 'string' || host.length > 200) return { error: '"hostContains" must be text' };
  return {
    name: typeof o.name === 'string' && o.name ? o.name.slice(0, 80) : 'Custom profile',
    containerSelector: o.containerSelector as string,
    itemSelector: o.itemSelector as string,
    rules,
    hostContains: host,
  };
}

/** The profile in the data folder if there is one (a broken file is an error, never silently ignored), else the built-in one. */
export function loadProfile(dataDir: string): LiveProfile | { error: string } {
  const path = join(dataDir, PROFILE_FILE);
  if (!existsSync(path)) return EVOLUTION_BEAD_ROAD;
  let raw: unknown;
  try {
    raw = JSON.parse(readFileSync(path, 'utf8'));
  } catch (e) {
    return { error: `${PROFILE_FILE} is not valid JSON: ${(e as Error).message}` };
  }
  const parsed = parseProfile(raw);
  return 'error' in parsed ? { error: `${PROFILE_FILE}: ${parsed.error}` } : parsed;
}

/**
 * The script run inside the page. It must be self-contained: it is sent to the browser as text.
 * Returns { container, items } where items are 'P' | 'B' | 'T' or '?' for a cell no rule recognises.
 */
export function probeScript(profile: LiveProfile): string {
  const fn = function (p: { containerSelector: string; itemSelector: string; rules: LiveRule[] }) {
    const out: { container: boolean; items: string[]; error?: string } = { container: false, items: [] };
    try {
      const container = document.querySelector(p.containerSelector);
      if (!container) return out;
      out.container = true;
      const nodes = container.querySelectorAll(p.itemSelector);
      for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i] as HTMLElement;
        const text = String(node.innerText || node.textContent || '').trim().toLowerCase();
        const hay = [text, node.getAttribute('aria-label') || '', node.getAttribute('class') || ''].join(' ').toLowerCase();
        let found = '?';
        for (let k = 0; k < p.rules.length; k++) {
          const r = p.rules[k]!;
          if (r.exact !== undefined && text === r.exact.toLowerCase()) found = r.result;
          else if (r.contains !== undefined && hay.indexOf(r.contains.toLowerCase()) >= 0) found = r.result;
          if (found !== '?') break;
        }
        out.items.push(found);
      }
    } catch (e) {
      out.error = String(e);
    }
    return out;
  };
  const arg = { containerSelector: profile.containerSelector, itemSelector: profile.itemSelector, rules: profile.rules };
  return `(${fn.toString()})(${JSON.stringify(arg)})`;
}

export type RoadChange =
  | { kind: 'same' }
  | { kind: 'grew'; fresh: Result[] }
  /** The road is shorter or no longer starts with what we saw: a new shoe, or a misread. */
  | { kind: 'reset' };

/** Compares two readings of the road. Pure; the feed decides what a reset means. */
export function diffRoad(previous: readonly Result[], current: readonly Result[]): RoadChange {
  const prefixMatches = previous.length <= current.length && previous.every((r, i) => current[i] === r);
  if (!prefixMatches) return { kind: 'reset' };
  return current.length === previous.length ? { kind: 'same' } : { kind: 'grew', fresh: current.slice(previous.length) };
}
