import { IhgEngine, ihgConfig } from './ihg.js';
import type { Settings } from './settings.js';
import type { StrategyEngine } from './strategy.js';

/**
 * A setting the bot cannot honour yet must stop the session from starting. Quietly ignoring a
 * stop loss or a keep-alive would be worse than refusing. Each entry disappears as its milestone lands.
 */
export function unsupportedReason(s: Settings): string | null {
  if (s.strategy !== 'IHG') return `${s.strategy === 'AME' ? 'AME-AI' : 'JJ'} is not available yet - choose IHG`;
  if (s.progression === 'list') return 'Manual Sequence Input is not available yet - use Auto Progression';
  // Waiting for the client: does the filter apply once per session or before every run?
  if (s.entry_loss_filter > 0) return 'Virtual Bet entry filter is not available yet - untick it';
  return null;
}

export function createEngine(s: Settings): { engine: StrategyEngine } | { error: string } {
  const error = unsupportedReason(s);
  if (error) return { error };
  return { engine: new IhgEngine(ihgConfig(s)) };
}
