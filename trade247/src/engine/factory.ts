import { ClassicEngine, classicConfig } from './classic.js';
import { AmeEngine } from './ame.js';
import { CycleEngine } from './cycle.js';
import { IhgEngine, ihgConfig } from './ihg.js';
import { isClassic, type Settings } from './settings.js';
import type { StrategyEngine } from './strategy.js';

const CLASSIC_NAMES: Record<string, string> = {
  MARTINGALE: 'Martingale', GRINDING: 'Grinding', SEQUENCE: 'Sequence',
  ODD_EVEN: 'Odd Even', ODD_DIFF: 'ODD Diff', ODD_LOOSER: 'ODD Looser', ODD_IS_GOD: 'ODD is God',
};

/**
 * A setting the bot cannot honour yet must stop the session from starting. Quietly ignoring a
 * stop loss or a keep-alive would be worse than refusing. Each entry disappears as its milestone lands.
 */
export function unsupportedReason(s: Settings): string | null {
  if (isClassic(s.strategy)) {
    const name = CLASSIC_NAMES[s.strategy];
    if (s.stop_consec_loss_count > 0 && s.stop_consec_loss_action === 'WAIT FOR PATTERN') {
      return `"Wait for pattern" is an IHG feature - for ${name} choose STOP BET`;
    }
    const lists = s.strategy === 'SEQUENCE' || s.strategy.startsWith('ODD_');
    if (s.progression === 'list' && !lists) return `${name} uses one Base Unit - choose Auto Progression (Manual Sequence Input is for Sequence and the ODD strategies)`;
    return null;
  }
  if (s.strategy === 'JJ' && s.progression === 'list') return 'JJ uses cycle staking - choose Auto Progression';
  return null;
}

export function createEngine(s: Settings): { engine: StrategyEngine } | { error: string } {
  const error = unsupportedReason(s);
  if (error) return { error };
  if (isClassic(s.strategy)) return { engine: new ClassicEngine(classicConfig(s)) };
  if (s.strategy === 'AME') return { engine: new AmeEngine(s) };
  if (s.strategy === 'JJ') return { engine: new CycleEngine(s) };
  return { engine: new IhgEngine(ihgConfig(s)) };
}
