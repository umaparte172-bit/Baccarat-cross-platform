import { BANKER_PAYOUT, roundUpToChip, toMicro } from './money.js';
import type { Settings } from './settings.js';
import type { BetPlan, EngineStatus, HandResult, StrategyEngine } from './strategy.js';
import type { Result, Side } from './types.js';

export const CYCLE_PRESETS = {
  JJ20: [10, 5, 20, 2, 60], JJ30: [10, 10, 350, 10, 100], JJ50: [10, 10, 98, 12, 250],
  JJ70: [10, 10, 10, 0, 0], JJ80: [100, 20, 20, 0, 0],
} as const;

interface CycleConfig {
  unitMicro: number; chipMicro: number; cycleHands: number; pctMin: number; pctMax: number;
  maxMult: number; maxHands: number; ramp: number; maxUnits: number; shoePolicy: Settings['shoe_change_policy'];
}

function configOf(s: Settings): CycleConfig {
  const preset = s.cycle_preset === 'custom' ? null : CYCLE_PRESETS[s.cycle_preset];
  return {
    unitMicro: toMicro(s.unit_value), chipMicro: toMicro(s.min_chip),
    cycleHands: preset?.[0] ?? s.cycle_hands, pctMin: preset?.[1] ?? s.cycle_pct_min,
    pctMax: preset?.[2] ?? s.cycle_pct_max,
    maxMult: preset ? (preset[3] || 1e9) : (s.cycle_max_mult > 0 ? s.cycle_max_mult : 1e9),
    maxHands: preset?.[4] ?? s.cycle_max_hands, ramp: s.cycle_ramp,
    maxUnits: s.max_bet_mode === 'off' ? 1e9 : s.max_bet_mode === 'doc' ? 45 : s.max_bet_units,
    shoePolicy: s.shoe_change_policy,
  };
}

interface CycleState {
  decisions: Side[]; cycle: Side[] | null; cycleIndex: number; pattern: string;
  multiplier: number; cycleDone: number; cyclePnlMicro: number; formulaHands: number; badCycles: number;
  runsClosed: number; runsCut: number;
}

export class CycleEngine implements StrategyEngine {
  readonly name = 'JJ';
  private cfg: CycleConfig;
  private s: CycleState;
  constructor(settings: Settings) { this.cfg = configOf(settings); this.s = this.fresh(); }
  private fresh(): CycleState { return { decisions: [], cycle: null, cycleIndex: 0, pattern: 'Follow', multiplier: 1, cycleDone: 0, cyclePnlMicro: 0, formulaHands: 0, badCycles: 0, runsClosed: 0, runsCut: 0 }; }
  configure(settings: Settings): void { this.cfg = configOf(settings); }
  private side(): Side {
    if (this.s.cycle) return this.s.cycle[this.s.cycleIndex]!;
    const d = this.s.decisions;
    if (!d.length) return 'P';
    if (d.length === 1) return d[0]!;
    return d.at(-1) === d.at(-2) ? d.at(-1)! : d.at(-2)!;
  }
  private push(x: Side): void {
    if (this.s.cycle) {
      if (this.s.cycle[this.s.cycleIndex] === x) this.s.cycleIndex = (this.s.cycleIndex + 1) % 4;
      else this.s.cycle = null;
    }
    this.s.decisions.push(x);
    if (this.s.decisions.length > 64) this.s.decisions.splice(0, this.s.decisions.length - 32);
    const d = this.s.decisions;
    if (!this.s.cycle && d.length >= 4) {
      const [a, b, c, e] = d.slice(-4);
      if (a === b && c === e && a !== c) { this.s.cycle = [a!, a!, c!, c!]; this.s.cycleIndex = 0; }
    }
    this.s.pattern = this.s.cycle ? '2-2' : d.length < 2 ? 'Follow' : d.at(-1) === d.at(-2) ? 'Streak' : 'Chop';
  }
  plan(paused: boolean): BetPlan {
    const raw = Math.min(this.s.multiplier, this.cfg.maxMult, this.cfg.maxUnits);
    const chipUnits = this.cfg.chipMicro / this.cfg.unitMicro;
    const units = Math.max(chipUnits || 0.000001, chipUnits > 0 ? Math.round(raw / chipUnits) * chipUnits : raw);
    return { mode: paused ? 'VIRTUAL' : 'REAL', side: this.side(), units, stakeMicro: roundUpToChip(Math.round(units * this.cfg.unitMicro), this.cfg.chipMicro), final: false, pattern: this.s.pattern, reason: paused ? 'Paused - watching only' : `cycle ${this.s.cycleDone + 1}/${this.cfg.cycleHands} x${this.s.multiplier.toFixed(2)}` };
  }
  settle(result: Result, paused: boolean): HandResult {
    const plan = this.plan(paused);
    if (result === 'T') {
      if (!paused) this.advanceCycle(0);
      return { plan, result, outcome: paused ? 'V-TIE' : 'TIE', pnlMicro: 0, pattern: this.s.pattern, note: '', singleWins: 0, runNetMicro: this.s.cyclePnlMicro, runClosed: false };
    }
    const won = plan.side === result;
    const pnlMicro = paused ? 0 : won ? Math.round(plan.stakeMicro * (plan.side === 'B' ? BANKER_PAYOUT : 1)) : -plan.stakeMicro;
    this.push(result);
    const advanced = paused ? { note: '', closed: false } : this.advanceCycle(pnlMicro);
    return { plan, result, outcome: paused ? (won ? 'V-WIN' : 'V-LOSS') : won ? 'WIN' : 'LOSS', pnlMicro, pattern: this.s.pattern, note: advanced.note, singleWins: 0, runNetMicro: this.s.cyclePnlMicro, runClosed: advanced.closed };
  }
  private advanceCycle(pnlMicro: number): { note: string; closed: boolean } {
    this.s.cyclePnlMicro += pnlMicro; this.s.cycleDone++; this.s.formulaHands++;
    if (this.s.cycleDone < this.cfg.cycleHands) return { note: '', closed: false };
    const shown = this.s.cyclePnlMicro / this.cfg.unitMicro;
    if (this.s.cyclePnlMicro > 0) {
      this.s.runsClosed++; this.restartFormula();
      return { note: `cycle positive (+${shown.toFixed(2)}u) - formula stops, back to base`, closed: true };
    }
    if (this.cfg.maxHands > 0 && this.s.formulaHands >= this.cfg.maxHands) {
      this.s.runsCut++; this.restartFormula();
      return { note: `hand limit ${this.cfg.maxHands} reached - formula reset`, closed: true };
    }
    const frac = this.cfg.ramp > 1 ? Math.min(1, this.s.badCycles / Math.max(1, this.cfg.ramp - 1)) : 1;
    const pct = this.cfg.pctMin + (this.cfg.pctMax - this.cfg.pctMin) * frac;
    this.s.multiplier = Math.min(this.s.multiplier * (1 + pct / 100), this.cfg.maxMult);
    this.s.badCycles++; this.s.cycleDone = 0; this.s.cyclePnlMicro = 0;
    return { note: `cycle ${shown.toFixed(2)}u - stake +${Math.round(pct * 10) / 10}%`, closed: false };
  }
  private restartFormula(): void { this.s.multiplier = 1; this.s.cycleDone = 0; this.s.cyclePnlMicro = 0; this.s.formulaHands = 0; this.s.badCycles = 0; }
  newShoe(): void { if (this.cfg.shoePolicy === 'continue') return; this.s.decisions = []; this.s.cycle = null; this.s.cycleIndex = 0; this.s.pattern = 'Follow'; if (this.cfg.shoePolicy === 'reset_all') this.cutRun(); }
  cutRun(): void { if (this.s.formulaHands > 0) this.s.runsCut++; this.restartFormula(); }
  reset(): void { this.s = this.fresh(); }
  status(): EngineStatus {
    return Object.assign(
      { run_net_units: Math.round((this.s.cyclePnlMicro / this.cfg.unitMicro) * 100) / 100, single_wins: 0, virtual_mode: false, run_open: this.s.formulaHands > 0 },
      { multiplier: Math.round(this.s.multiplier * 1000) / 1000, cycle_pos: `${this.s.cycleDone}/${this.cfg.cycleHands}`, formula_hands: this.s.formulaHands, pattern: this.s.pattern },
    );
  }
  counters(): { runsClosed: number; runsCut: number } { return { runsClosed: this.s.runsClosed, runsCut: this.s.runsCut }; }
  snapshot(): unknown { return structuredClone(this.s); }
  restore(snapshot: unknown): void { this.s = { ...this.fresh(), ...(snapshot as Partial<CycleState>) }; }
}
