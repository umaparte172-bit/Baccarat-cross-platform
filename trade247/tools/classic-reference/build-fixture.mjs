// Combines the scenarios and the C# traces into the fixture the TypeScript tests replay.
//   node tools/classic-reference/make-scenarios.mjs > scenarios.json
//   dotnet run --project tools/classic-reference -c Release -- scenarios.json traces.json
//   node tools/classic-reference/build-fixture.mjs scenarios.json traces.json test/fixtures/classic-reference.json
import { readFileSync, writeFileSync } from 'node:fs';

const [scenariosPath, tracesPath, out] = process.argv.slice(2);
const scenarios = JSON.parse(readFileSync(scenariosPath, 'utf8')).slice(0, 280);
const traces = new Map(JSON.parse(readFileSync(tracesPath, 'utf8')).map((t) => [t.id, t.rows]));
const fixture = scenarios.map((s) => ({
  ...s,
  // pick, stake, bet placed, W/L, profit change, total profit - one row per hand, "|" between hands.
  trace: traces.get(s.id).map((r) => r.join(',')).join('|'),
}));
writeFileSync(out, JSON.stringify(fixture));
console.log(`${fixture.length} scenarios, ${fixture.reduce((n, f) => n + f.trace.split('|').length, 0)} hands`);
