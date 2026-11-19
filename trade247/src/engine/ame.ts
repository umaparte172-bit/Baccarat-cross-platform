import { BANKER_PAYOUT, roundUpToChip, toMicro } from './money.js';
import type { Settings } from './settings.js';
import type { BetPlan, EngineStatus, HandResult, StrategyEngine } from './strategy.js';
import type { Result, Side } from './types.js';

const PERIOD_NAMES: Record<number, string> = { 1: 'Streak', 2: 'Chop', 3: '3-cycle', 4: '2-2' };
const clip01 = (n: number): number => Math.max(0, Math.min(1, n));
const entropy = (p: number): number => p <= 0 || p >= 1 ? 0 : -(p * Math.log2(p) + (1 - p) * Math.log2(1 - p));

export interface MomentumScore {
  period: number; pick: Side; frequency: number; lag: number; inertia: number;
  confidence: number; entropy: number; fit: number; volatility: number; ready: boolean;
}

export function scoreMomentum(decisions: Side[]): MomentumScore {
  const score: MomentumScore = {
    period: 0, pick: decisions.at(-1) ?? 'P', frequency: 0, lag: 0, inertia: 0,
    confidence: 0, entropy: 0, fit: 0, volatility: 1, ready: false,
  };
  const n = decisions.length;
  if (n < 6) return score;
  score.ready = true;
  const repeats = decisions.slice(1).filter((v, i) => v === decisions[i]).length / (n - 1);
  score.entropy = entropy(repeats);
  let best: { key: number; period: number; mean: number; weighted: number; run: number } | null = null;
  for (let period = 1; period <= 4; period++) {
    const count = n - period;
    if (count < 4) continue;
    const matches = Array.from({ length: count }, (_, i) => decisions[i + period] === decisions[i]);
    const mean = matches.filter(Boolean).length / count;
    const weights = matches.map((_, i) => 1.2 ** i);
    const total = weights.reduce((a, b) => a + b, 0);
    const weighted = weights.reduce((sum, w, i) => sum + (matches[i] ? w : 0), 0) / total;
    let run = 0;
    for (let i = matches.length - 1; i >= 0 && matches[i]; i--) run++;
    const key = 0.5 * mean + 0.5 * weighted;
    if (!best || key > best.key + 1e-12) best = { key, period, mean, weighted, run };
  }
  if (!best) return score;
  score.period = best.period;
  score.pick = decisions[n - best.period]!;
  score.frequency = clip01((best.mean - 0.5) * 2);
  score.lag = clip01((best.weighted - 0.5) * 2);
  score.inertia = clip01(best.run / 6);
  score.fit = best.weighted;
  score.volatility = 1 - score.lag;
  score.confidence = 100 * (0.35 * score.frequency + 0.35 * score.lag + 0.30 * score.inertia);
  return score;
}

interface AmeConfig {
  unitMicro: number; chipMicro: number; window: number; minConfidence: number; freezeHands: number;
  stableUnits: number; recoveryRate: number; maxStep: number; chaosEntropy: number; chaosFit: number;
  maxUnits: number; progression: 'dynamic' | 'flat' | 'list'; chain: number[];
  shoePolicy: Settings['shoe_change_policy']; endPolicy: Settings['end_shoe_policy']; endHand: number;
}

const configOf = (s: Settings): AmeConfig => ({
  unitMicro: toMicro(s.unit_value), chipMicro: toMicro(s.min_chip), window: s.ame_window,
  minConfidence: s.ame_min_confidence, freezeHands: s.ame_freeze_hands,
  stableUnits: s.ame_stable_units, recoveryRate: s.ame_recovery_rate, maxStep: s.ame_max_step,
  chaosEntropy: s.ame_chaos_entropy, chaosFit: s.ame_chaos_fit,
  maxUnits: s.max_bet_mode === 'off' ? 1e9 : s.max_bet_mode === 'doc' ? 45 : s.max_bet_units,
  progression: s.progression === 'list' || s.progression === 'flat' ? s.progression : 'dynamic',
  chain: s.chain.length ? s.chain : [1], shoePolicy: s.shoe_change_policy,
  endPolicy: s.end_shoe_policy, endHand: s.end_shoe_hand,
});

interface AmeState {
  decisions: Side[]; deficit: number; lastUnits: number; chainIndex: number; freezeLeft: number;
  hands: number; runOpen: boolean; runsClosed: number; runsCut: number;
}

export class AmeEngine implements StrategyEngine {
  readonly name = 'AME';
  private cfg: AmeConfig;
  private s: AmeState;

  constructor(settings: Settings) {
    this.cfg = configOf(settings);
    this.s = this.fresh();
  }

  private fresh(): AmeState {
    return { decisions: [], deficit: 0, lastUnits: this.cfg.stableUnits, chainIndex: 0, freezeLeft: 0, hands: 0, runOpen: false, runsClosed: 0, runsCut: 0 };
  }

  configure(settings: Settings): void { this.cfg = configOf(settings); }

  private score(): MomentumScore { return scoreMomentum(this.s.decisions.slice(-this.cfg.window)); }

  private reason(score: MomentumScore, paused: boolean): string {
    if (paused) return 'Paused - watching only';
    if (this.s.freezeLeft > 0) return 'chaos freeze';
    if (score.ready && score.entropy >= this.cfg.chaosEntropy && score.fit < this.cfg.chaosFit) return 'chaos freeze';
    if (!score.ready) return 'warming up';
    if (score.confidence < this.cfg.minConfidence) return 'low confidence';
    if (this.cfg.endPolicy !== 'off' && this.s.hands >= this.cfg.endHand) return 'end of shoe';
    return '';
  }

  private stake(score: MomentumScore): { units: number; final: boolean } {
    let units: number;
    if (this.cfg.progression === 'flat') units = this.cfg.stableUnits;
    else if (this.cfg.progression === 'list') units = this.cfg.chain[this.s.chainIndex % this.cfg.chain.length]!;
    else if (this.s.deficit <= 1e-9) units = this.cfg.stableUnits;
    else {
      let raw = (this.cfg.stableUnits + this.cfg.recoveryRate * this.s.deficit) * (1 - 0.4 * clip01(score.volatility));
      raw = Math.max(this.cfg.stableUnits, raw);
      raw = Math.min(raw, this.s.lastUnits + this.cfg.maxStep);
      const step = Math.max(0.5, this.cfg.chipMicro / this.cfg.unitMicro || 0);
      units = Math.round(raw / step) * step;
    }
    const final = units >= this.cfg.maxUnits - 1e-9;
    return { units: Math.min(units, this.cfg.maxUnits), final };
  }

  plan(paused: boolean): BetPlan {
    const score = this.score();
    const reason = this.reason(score, paused);
    const sized = this.stake(score);
    const units = sized.units;
    return {
      mode: reason ? 'VIRTUAL' : 'REAL', side: score.pick, units,
      stakeMicro: roundUpToChip(Math.round(units * this.cfg.unitMicro), this.cfg.chipMicro),
      final: sized.final, pattern: `${PERIOD_NAMES[score.period] ?? '-'} ${Math.round(score.confidence)}%`,
      reason: reason || undefined,
    };
  }

  settle(result: Result, paused: boolean): HandResult {
    const plan = this.plan(paused);
    const scoreBefore = this.score();
    this.s.hands++;
    if (result === 'T') return { plan, result, outcome: plan.mode === 'REAL' ? 'TIE' : 'V-TIE', pnlMicro: 0, pattern: plan.pattern, note: '', singleWins: 0, runNetMicro: -Math.round(this.s.deficit * this.cfg.unitMicro), runClosed: false };
    const won = plan.side === result;
    let pnlMicro = 0;
    let runClosed = false;
    let note = '';
    if (plan.mode === 'REAL') {
      pnlMicro = won ? Math.round(plan.stakeMicro * (plan.side === 'B' ? BANKER_PAYOUT : 1)) : -plan.stakeMicro;
      const pnlUnits = pnlMicro / this.cfg.unitMicro;
      this.s.deficit = Math.max(0, this.s.deficit - pnlUnits);
      this.s.runOpen = this.s.deficit > 1e-9;
      if (this.cfg.progression === 'list') {
        if (won || plan.final) this.s.chainIndex = 0;
        else this.s.chainIndex = (this.s.chainIndex + 1) % this.cfg.chain.length;
      }
      if (this.s.deficit <= 1e-9 || plan.final) {
        runClosed = won && this.s.deficit <= 1e-9;
        if (runClosed) this.s.runsClosed++;
        else if (plan.final) { this.s.runsCut++; note = 'max stake reached - reset'; }
        this.s.deficit = 0; this.s.lastUnits = this.cfg.stableUnits; this.s.runOpen = false;
      } else this.s.lastUnits = plan.units;
    }
    if (!paused) {
      const chaos = scoreBefore.ready && scoreBefore.entropy >= this.cfg.chaosEntropy && scoreBefore.fit < this.cfg.chaosFit;
      if (this.s.freezeLeft > 0) this.s.freezeLeft--;
      else if (chaos) this.s.freezeLeft = Math.max(0, this.cfg.freezeHands - 1);
    }
    this.s.decisions.push(result);
    if (this.s.decisions.length > 64) this.s.decisions.splice(0, this.s.decisions.length - 32);
    return { plan, result, outcome: plan.mode === 'REAL' ? (won ? 'WIN' : 'LOSS') : won ? 'V-WIN' : 'V-LOSS', pnlMicro, pattern: this.plan(false).pattern, note, singleWins: 0, runNetMicro: -Math.round(this.s.deficit * this.cfg.unitMicro), runClosed };
  }

  newShoe(): void {
    if (this.cfg.shoePolicy === 'continue') return;
    this.s.decisions = []; this.s.freezeLeft = 0;
    if (this.cfg.shoePolicy === 'reset_all') this.cutRun();
  }
  cutRun(): void { if (this.s.runOpen) this.s.runsCut++; this.s.deficit = 0; this.s.lastUnits = this.cfg.stableUnits; this.s.chainIndex = 0; this.s.runOpen = false; }
  reset(): void { this.s = this.fresh(); }
  status(): EngineStatus {
    const score = this.score();
    return Object.assign(
      { run_net_units: -Math.round(this.s.deficit * 100) / 100, single_wins: 0, virtual_mode: this.plan(false).mode === 'VIRTUAL', run_open: this.s.runOpen },
      { confidence: Math.round(score.confidence * 10) / 10, threshold: this.cfg.minConfidence, rhythm: PERIOD_NAMES[score.period] ?? '-', entropy: Math.round(score.entropy * 100) / 100, freeze_left: this.s.freezeLeft },
    );
  }
  counters(): { runsClosed: number; runsCut: number } { return { runsClosed: this.s.runsClosed, runsCut: this.s.runsCut }; }
  snapshot(): unknown { return structuredClone(this.s); }
  restore(snapshot: unknown): void { this.s = { ...this.fresh(), ...(snapshot as Partial<AmeState>) }; }
}
