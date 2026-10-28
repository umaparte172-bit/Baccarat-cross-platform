// Writes the scenario list the C# runner plays: the client's cases plus seeded random sequences.
//   node tools/classic-reference/make-scenarios.mjs > scenarios.json
function mulberry32(a) { return () => { a |= 0; a = (a + 0x6d2b79f5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const rng = mulberry32(20261028);
const pickFrom = (list) => list[Math.floor(rng() * list.length)];
const randomOutcomes = (n, tieRate) => Array.from({ length: n }, () => { const r = rng(); return r < tieRate ? 'T' : r < tieRate + (1 - tieRate) / 2 ? 'P' : 'B'; }).join('');

const modes = ['Martingale', 'Grinding', 'Sequence', 'Odd Even', 'ODD Diff', 'ODD Looser', 'ODD is God'];
const units = [1, 0.5, 2, 0.1];
const stakeLists = [[1], [1, 2, 3], [1, 2, 4, 8], [2, 4, 6], [5, 10], [1, 1, 2, 3, 5]];
const sideLists = [['B', 'P', 'P'], ['P'], ['B', 'P', 'P', 'B', 'B', 'P', 'B', 'P'], ['P', 'B']];
const scenarios = [];
for (let i = 0; i < 700; i++) {
  const mode = modes[i % modes.length];
  const unit = pickFrom(units);
  const stakes = pickFrom(stakeLists).map((x) => +(x * unit).toFixed(6));
  scenarios.push({
    id: `r${i}`, mode, first: pickFrom(['P', 'B']), unit,
    multiplier: pickFrom([2, 2, 3, 1.5]), stakes, sides: pickFrom(sideLists),
    outcomes: randomOutcomes(40 + Math.floor(rng() * 160), pickFrom([0, 0.1, 0.1, 0.3])),
  });
}
console.log(JSON.stringify(scenarios));
