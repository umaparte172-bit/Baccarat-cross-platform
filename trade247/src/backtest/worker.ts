import { parentPort, workerData } from 'node:worker_threads';
import { runTrial, trialSeed, type TrialResult } from './trial.js';
import type { Settings } from '../engine/settings.js';
import type { Result } from '../engine/types.js';

/** What the job manager hands to each worker thread. */
export interface WorkerInput {
  settings: Settings;
  /** Base seed of the whole run; session i always uses trialSeed(seed, i), whichever worker plays it. */
  seed: number;
  /** Total number of sessions in the run. */
  trials: number;
  /** This worker plays sessions index, index + workers, index + 2 * workers, ... */
  index: number;
  workers: number;
  shoes: { kind: 'sim'; count: number } | { kind: 'paste'; shoes: Result[][] };
  bankrollUnits: number;
  curvePoints: number;
}

export type WorkerMessage = { type: 'progress'; done: number } | { type: 'done'; results: TrialResult[] };

/** Shared by the worker thread and the in-process fallback. */
export function playSessions(input: WorkerInput, onProgress: (done: number) => void, cancelled: () => boolean = () => false): TrialResult[] {
  const results: TrialResult[] = [];
  let pending = 0;
  let lastReport = Date.now();
  for (let i = input.index; i < input.trials; i += input.workers) {
    if (cancelled()) break;
    results.push(
      runTrial({
        settings: input.settings,
        shoes: input.shoes.kind === 'sim' ? { kind: 'sim', count: input.shoes.count, seed: trialSeed(input.seed, i) } : input.shoes,
        bankrollUnits: input.bankrollUnits,
        curvePoints: input.curvePoints,
      }),
    );
    pending++;
    const now = Date.now();
    if (now - lastReport >= 50) {
      onProgress(pending);
      pending = 0;
      lastReport = now;
    }
  }
  if (pending > 0) onProgress(pending);
  return results;
}

// Only when this file is the entry point of a worker thread.
if (parentPort) {
  const port = parentPort;
  const results = playSessions(workerData as WorkerInput, (done) => port.postMessage({ type: 'progress', done } satisfies WorkerMessage));
  port.postMessage({ type: 'done', results } satisfies WorkerMessage);
}
