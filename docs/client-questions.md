# TRADE247-V1: what we still need from the client

Status 2026-10-28. Built and tested: the IHG engine (checked against all 315 hands of his document), the **seven classic
strategies** of the Windows application (Martingale, Grinding, Sequence, Odd Even, ODD Diff, ODD Looser, ODD is God; checked
against his written cases and a reference engine), the risk layer, reports, Telegram alerts, the Backtest, the **Live table**
feed, **real betting on a live table** (off by default), and the app as a **desktop program** and a single-file program for
Windows and Apple-silicon Macs. What follows is what only the client can supply, in the order it blocks the work.

## A. Needed to finish

**1. AME-AI formulas.** The PRD names the parts but gives no maths. Please send the formulas (a spreadsheet, a script, or
10 to 20 worked rounds with the expected numbers):
* Frequency, Lag (recency weights) and Inertia, and how they combine into the confidence score.
* What each rhythm period p = 1..4 predicts (Streak, Chop, 3-cycle, 2-2): for example, is the 3-cycle's prediction "the same as 3 rounds ago"?
* How "rhythm fit" is calculated, and which entropy (Shannon, over which transitions, which log base).
* Whether the 12-round window is fixed or the page's "scan window" setting (default 12, minimum 6).
* What the page's "stable units", "recovery rate", "max step" and "flat / dynamic" do to the stake.
* What a "virtual probe" is and when the 4-hand freeze ends.

The old Windows application has none of this (no such strings in it), so it cannot be recovered from there. If he has no
formulas, we write a one-page interpretation with examples and he approves it in writing before we code it.

**2. JJ strategy.** On his page with five presets (JJ20 to JJ80) but not in the PRD. Either send the stake formula behind the
five numbers of each preset (hands per cycle, increase from %, up to %, stake cap, reset after hands), or confirm JJ is out of V1.

**3. "Virtual Bet - start after N losses".** Once when a session starts, or before every run (wait for N losses, bet one run, wait again)?

**4. A capture of the real table page** (the old selectors are from 2023). Five minutes:
1. Open `Start-Chrome-for-Trade247` (it opens its own Chrome window), sign in to Roobet and open an Evolution baccarat table.
2. While a round is open for betting, run `trade247 --inspect-table` in a terminal, and send the small file it saves (element names and
   short labels only: no balances, names or page addresses beyond the site).
3. Tell us which table (Speed Baccarat, Baccarat A, ...) and whether a region or VPN is needed.

With it we write the table profile (road, chips, spots, total) and check it with `trade247 --check-table`, which clicks nothing.

**5. The first real-money test, and who does it.** Betting is built and tested on a mock table, never on a real one. It needs:
* A small balance or a demo table, and his decision on the limits (largest bet, session limit).
* The first bets in **Confirm** mode, watched by him, before **Automatic** is ever used.
* The chip values and the table's minimum and maximum bet.
* His written acceptance that a casino may close an account that bets automatically.

**6. Rules the written cases do not cover.** The classic strategies follow his 19 cases exactly, and a reference engine for
everything in between, but some branches were never shown to us: Grinding after long losing runs, the ODD strategies when the
sides stay level for a long time, and what the old app's "PATTERN" did beyond the IHG rules. Please confirm the descriptions in
the page's strategy help, or send a recording of the old app on those branches.

## B. The anti-detect part of the PRD

The PRD asks for fingerprint spoofing (WebGL, Canvas, user-agent), evading DataDome / Cloudflare / casino bot detection, and
humanized mouse paths. **We are not building those**: their purpose is to defeat a site's bot protection. What V1 does instead:
runs in the user's own, normally signed-in Chrome, reads the page like an ordinary user, and clicks with ordinary mouse events
after a configurable random pause (default 0.5 to 2.5 seconds, as the PRD asks for delays). He should confirm in writing that he
understands the casino can still detect and ban the account.

## C. Defaults we chose (please answer yes, or give the change)

| Topic | What we did | Why we ask |
|---|---|---|
| New shoe | Reset the losing run and the pattern, for every strategy (PRD) | The IHG document continues the run; a preset button exists for that. |
| "Max Unit" field | Both meanings: run-end stake (45, placed then the run closed) and a hard cap (never placed) | The PRD says "cut and reset", the document places 51, 60 and 81 unit bets. Which does the field mean? |
| "Single wins" | A loss that follows a win inside a run, counted to the limit (7) | The PRD says "L-W-L consecutive". |
| Take profit / stop loss | Per session and per day | The PRD says "Daily" only. |
| Ties | Count as hands for pauses and keep-alive; ignored when choosing a side | Not specified. |
| Classic strategies | Martingale multiplier 2; stake lists in units of the Base Unit; a stake cap drops the progression back to the base stake | The old app's defaults were stakes of 100, 200, 300. |
| Keep-Alive | One minimum bet after N idle hands | The old app's defaults were amount **1** and **5** idle hands; our page defaults to 0.1 and 10. Which does he want? |
| History at start | The road already on the table is not played | The old app read the table's history first. Should the pattern start from what is on screen? |
| After an outage | The hands missed are skipped, not replayed; a lost table alerts once | Confirm. |
| Restart after a crash | The bot restarts idle and waits for START, and never resumes betting by itself | Confirm (we advise against automatic resume with real money). |
| A bet that is not confirmed | The session stops and alerts; nothing is retried | Confirm. |

## D. Telegram

* **Revoke the bot token** printed in the PRD (BotFather > /revoke) and send a new one **privately**. Telegram already rejects the
  old one ("Unauthorized"). We did not commit it.
* What counts as "disconnection": the table page left, Chrome closed, or no new result for N seconds? (We alert when the table is not on the page for 15 s.)
* Other alerts wanted? Betting stops (unconfirmed bet, limit reached) already send one.

## E. Delivery and acceptance

* He runs the Windows installer or portable app and (if he has a Mac) the Mac app, and tells us what the first-run prompts show.
* He accepts an unsigned Windows program and an ad-hoc-signed, not notarized Mac app, or pays for signing certificates.
* Windows VPS: edition, RAM, and whether it has a desktop session (the desktop app and a signed-in Chrome need one; the single-file program and the scheduled task do not, but then there is no table to read).
* What "done" means to him: for example a 7-day paper-trading run, a number of sessions, and sign-off on the Backtest figures.
* Payment milestones tied to each deliverable, so the parts waiting on his answers do not hold up payment for the finished ones.

## F. Things we still have to check ourselves (not for the client)

* Run the CI workflow once so the Mac packages are built and their signatures verified, then start the Mac app on a real Apple-silicon Mac.
* Open one exported `.xlsx` in real Excel.
* Register the scheduled task on the real Windows VPS (only a dry run has been done).
* Check the Live feed and the betting clicks against a real Roobet / Evolution table as soon as the capture from item A4 arrives.
