using Baccarat.Core;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var session = new Session();
Check(!session.Undo(), "Empty undo should be harmless");
session.Record(Outcome.Banker);
session.Record(Outcome.Player);
session.Record(Outcome.Tie);
Check(session.Rounds.Count == 3 && session.Count(Outcome.Banker) == 1, "Counts must match results");
Check(session.Undo(), "Undo should remove last result");
session.Record(Outcome.Banker);
Check(session.Rounds[^1].Number == 3 && session.Count(Outcome.Tie) == 0, "Undo must keep numbering consistent");
Check(session.ToCsv().Split('\n').Length == 4 && session.ToCsv().Contains("3,Banker,"), "CSV must include all rounds");
try { session.Record((Outcome)999); throw new Exception("Invalid result accepted"); }
catch (ArgumentOutOfRangeException) { }
session.Clear();
Check(session.Rounds.Count == 0, "Clear must empty results");
Console.WriteLine("PASS: record, counts, undo, numbering, export, invalid input and clear");
