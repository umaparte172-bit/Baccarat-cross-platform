# What the original Windows app (Baccarat (Evolution) 2.0) tells us for TRADE247

Date: 2026-10-28. Static analysis only: nothing from the supplied package was run. Sources: the string table recovered
earlier by `tools/recover_static_strings.py` (582 strings, pinned to the installed executable's SHA-256), the
strategy files, the configuration files, the batch files and the empty bets CSV. The main program's logic is protected,
so **no formulas or method bodies were recovered**: this document lists what the strings and files establish, and says
what they do not. Credential-like literals that sit among the strings are not repeated here.

## 1. How the original worked

| Topic | Evidence | Meaning for TRADE247 |
|---|---|---|
| Browser | `StartChrome.bat`: Chrome with `--remote-debugging-port=9014` and profile `C:\selenium`; a second one on 9015 / `C:\selenium2`. Selenium 3.141 attaches with a debugger address of `127.0.0.1:9014`. | The user starts Chrome, signs in and opens a table by hand; the program attaches. TRADE247's Live feed works the same way (port 9014 by default). |
| Site | The string `https://www.roobet.com/`; error texts "Roobet->BlackJack->Page Not Found" and "Please Go to the BlackJack Lobby and Enter to Table. Then Click Retry"; "Select Any Table and Click RETRY TABLE". | The target was Evolution live tables inside Roobet. The "Retry Table" button is the original's. |
| Reading results | Absolute XPaths under `//*[@id='root']` and CSS paths with generated class names (for example `content--82383`); `data-role=` attribute parsing; road drawn as SVG; status texts `PLACE YOUR BETS`, `BETS CLOSING`, `BETS CLOSED`, `BETS ACCEPTED`, `DEALING`, `WINS`, `WAIT FOR NEXT GAME`. | Results came from the page (DOM), not a WebSocket. These selectors date from 2023 and will have drifted. |
| Table choice | The words `baccarat` and `classic` (a table is baccarat when its name has them). | A profile can restrict tabs by text (`hostContains`). |
| Placing bets | Clicks on `chipItem` elements (chip values like `1k`), then Player / Banker / Tie spots; states named `PlaceBet`, `DummyBet`, `AutoBet`, `InitialBet`. | Not rebuilt. Betting is a separate decision (section 4). |
| History needed | "Error On Getting Dominor.. Please Wait For Five bets"; "Error On Getting Pattern.. Please Wait For Two bets"; "No History..So Bet Placed On AI Selection"; "Loading History.. Please Wait". | PATTERN looks at the last 2 results, the Dominor selection at the last 5. With no history it bets "AI SELECTION". The original also read the history already on the table at start. |
| Keep-alive | Label "To Prevent Game From Inactive Pausing, Place A Dummy Bet", fields "Amount" (default 1) and "Inactive Count" (default 5). | The PRD's Keep-Alive comes from here: one minimum bet after 5 idle hands, amount 1. |
| Stop reasons | `STOP PROFIT`, `STOP LOSS`, `STOP LOSS COUNT`, `LOW BALANCE`, `PAUSE`, `REBET`, `FAIL`, `STOP AUTO BET`. | Matches the risk layer's stops. |
| Files | Bets log `dd-MM-yy BETS.csv` (columns `time, session_no, sno, bet_on, bet_amount, winner, win_status, balance`) and a sessions log (`started_at, opening_balance, strategy, win/loss conditions, stop_profit, stop_loss, consc_loss_count/action, pause settings, stopped_at, stopped_by, profit, closing_balance`). | TRADE247's CSV/Excel log carries the same information. |
| Strategy model | JSON files: initial / win / loss condition (`bet_on` + amount rule), stop profit, stop loss, stop after N consecutive losses, pause after N losses. Bet-on choices: PLAYER, BANKER, WINNER, LOSER, DOMINOR, PATTERN, AI SELECTION, ODD EVEN, ODD Diff, ODD Looser, ODD is God, manual sequence. Amount rules: set, Multiply, Plus, Reset, Grinding, Ladder, Pattern, Sequence (next / previous / same). | The client's page already exposes the same controls. PATTERN's amount rule has no recovered formula. |
| Labels | The strings `Streak`, `Chop`, `2-2` beside result letters, and tokens `LW`, `WW`, `WLW`, `WINLOSSWIN`, `F2`, `XF2`, `F3`, `XF3`, `L2`. | PATTERN is the Streak / Chop / 2-2 selector, consistent with IHG. The other tokens are pattern codes of unknown meaning (likely the ODD strategies). |

## 2. What the app does not contain

* **AME-AI**: no strings for entropy, confidence, chaos or momentum anywhere. The formulas must come from the client.
* **JJ**: nothing. The presets in his page are the only description.
* **The PATTERN amount rule, shoe-change handling, pause details, the Virtual Bet filter**: protected, not recovered.
* **Telegram**: absent. It is new in the PRD.

## 3. Licence and server (why a no-login rebuild is right)

The original signed in against an account service, kept a token and checked "activated services", "trial" and "expired"
states ("Remaining Trial Bets", "Your Trial Expired.." and similar). Its service address, `buyer-api.otbx.com`, now
returns a domain-for-sale page (checked 2026-10-16), and the download host is the vendor's. The original therefore
cannot work as shipped, and TRADE247 needs none of it.

## 4. Things in the original that TRADE247 must not copy, and the client should know

* **Wallet functions.** The strings include tipping another user and withdrawing to Bitcoin, Ethereum and Litecoin
  addresses, "send_current_balance", and the Roobet profile-menu paths to do it, tied to message names such as `tip`
  and `withdraw`. This shows capability, not use, but nobody should run the original on an account that holds money.
* **Input lock.** It launches `KeyFreeze_x64.exe`, which blocks keyboard and mouse input.
* **Unverified updater.** It downloads `package.zip` and overwrites its own files with no signature or hash check.
* **Hard-coded literals** next to the server addresses (a short name, an identifier and a key-like string).
* **Forced process kills.** `KillPort2.bat` kills whatever owns port 9015.

None of this is in TRADE247.

## 5. What this settles and what it does not

Settled enough to build on: the target (Roobet, Evolution baccarat, attached Chrome), the idea of reading results from
the page, the Keep-Alive shape and defaults, the history-needed numbers, the stop reasons, the log columns.

Still needs the client: AME-AI and JJ formulas, the exact PATTERN stake rule, the Virtual Bet filter, a fresh capture of
a table page (the 2023 selectors have probably moved), and the decision on betting and anti-detection (see
`client-questions.md`).
