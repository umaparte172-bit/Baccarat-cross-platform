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
  if (s.entry_loss_filter > 0) return 'Virtual Bet entry filter is not available yet - untick it';
  if (s.keepalive_enabled) return 'Keep-Alive is not available yet - untick it';
  if (s.take_profit > 0) return 'Take Profit is not available yet - clear it';
  if (s.stop_loss > 0) return 'Stop Loss is not available yet - clear it';
  if (s.daily_stop_loss > 0) return 'Daily stop loss is not available yet - set it to 0';
  if (s.stop_consec_loss_count > 0) return 'Stop after N losses is not available yet - set it to 0';
  if (s.pause_consec_loss_count > 0) return 'Pause after N losses is not available yet - set it to 0';
  if (s.end_shoe_policy !== 'off') return 'Near shoe end handling is not available yet - set it to Off';
  return null;
}

export function createEngine(s: Settings): { engine: StrategyEngine } | { error: string } {
  const error = unsupportedReason(s);
  if (error) return { error };
  return { engine: new IhgEngine(ihgConfig(s)) };
}
