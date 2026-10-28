# Test fixtures

* `ihg-reference.json` - the worked hands of the client's IHG document (315 hands), used by `ihg.oracle.test.ts`.
* `classic-reference.json` - 280 scenarios (35,337 hands) of the seven classic strategies, with every pick, stake, bet,
  result, profit change and total profit as the **original C# engine** produced them. `classic.reference.test.ts` requires the
  TypeScript engines to reproduce them exactly (money to a millionth).

## Where `classic-reference.json` came from

The C# / .NET project that held the first implementation of these strategies was removed from the repository after its logic
was ported. It is kept in git history, in the tag **`legacy-csharp`**. That tag also contains the small runner that produced the
traces (`trade247/tools/classic-reference`: `make-scenarios.mjs`, `Program.cs`, `build-fixture.mjs`, with the steps at the top of
`build-fixture.mjs`). To regenerate the file, check out the tag in a separate folder and run it with the .NET 10 SDK:

```bash
git worktree add ../legacy legacy-csharp
```

The fixture is plain data, so the tests do not need .NET.
