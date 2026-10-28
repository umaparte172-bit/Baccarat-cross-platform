# TRADE247-V1: what we need from the client

Status 2026-10-28. Everything that does not depend on the client is built and committed or ready to commit (IHG engine,
risk layer, reports, Telegram, Backtest, packaging for Windows and Apple-silicon Mac, and a **read-only Live table feed**).
What follows is what only the client can supply. Items are in the order they block the work.

## A. Needed to build the remaining features

**1. AME-AI formulas.** The PRD names the parts but gives no maths. Please send the formulas (a spreadsheet, a script, or
10 to 20 worked rounds with the expected numbers):
* Frequency, Lag (recency weights) and Inertia, and how they combine into the confidence score.
* What each rhythm period p = 1..4 predicts (Streak, Chop, 3-cycle, 2-2): for example, is the 3-cycle's prediction "the same as 3 rounds ago"?
* How "rhythm fit" is calculated, and which entropy (Shannon, over which transitions, which log base).
* Whether the 12-round window is fixed or the page's "scan window" setting (the page defaults to 12, minimum 6).
* What the page's "stable units", "recovery rate", "max step" and "flat / dynamic" do to the stake.
* What a "virtual probe" is and when the 4-hand freeze ends.

The old Windows app has none of this (no such strings in it), so it cannot be recovered from there.
If he has no formulas, we write a one-page interpretation with examples and he approves it in writing before we code it.

**2. JJ strategy.** It is on his page with five presets (JJ20 to JJ80) but is not in the PRD. Either send the stake formula
behind the five numbers of each preset (hands per cycle, increase from %, up to %, stake cap, reset after hands), or confirm JJ is out of V1.

**3. "Virtual Bet - start after N losses".** Does it apply once when a session starts, or before every run (wait for N
losses, bet one run, wait again)? The two behave very differently, so we did not guess.

**4. A capture of the real table page** (the old selectors are from 2023). This is a five-minute job for him:
1. Open `Start-Chrome-for-Trade247` (it opens its own Chrome window), sign in to Roobet and open an Evolution baccarat table.
2. While the table is showing, run `trade247 --inspect-table` in a terminal.
3. Send the small file it saves. It contains element names and short labels only: no balances, names or page addresses beyond the site.
4. Also tell us: is the target still **Roobet with Evolution baccarat**? Which table (Speed Baccarat, Baccarat A, ...)? Region or VPN needed to open it?

With that file the reader is checked against the real page and any selector fixes ship as a small settings file.

**5. Betting on the table: yes or no.** Today every bet is simulated; the Live feed only reads. Placing real bets needs:
* His written decision that V1 may bet, and an account with a small balance for testing (or a demo table). He signs in himself; we never take his password.
* The chip values and minimum / maximum bet of the table.
* Acceptance that the casino may refuse or ban automated play. Betting stays off by default and starts only on his explicit command.

## B. The anti-detect part of the PRD (needs his written acknowledgement)

The PRD asks for fingerprint spoofing (WebGL, Canvas, user-agent), evading DataDome / Cloudflare / casino bot detection,
and humanized mouse paths. **We are not building those**: their purpose is to defeat a site's bot protection. What V1 does
instead: runs in the user's own, normally signed-in Chrome, reads the page like an ordinary user, and (if betting is approved)
clicks with ordinary events and a configurable random delay of 0.5 to 2.5 seconds. He should confirm in writing that he
understands the casino can still detect and ban the account.

## C. Defaults we chose (please answer yes, or give the change)

| Topic | What we did | Why we ask |
|---|---|---|
| New shoe | Reset the losing run and pattern (PRD) | The IHG document continues the run. A preset button exists for the document's way. |
| "Max Unit" field | Both meanings: run-end stake (45, placed then run closed) and a hard cap (never placed) | PRD says cut and reset; the IHG document places 51, 60 and 81 unit bets. Which does the field mean? |
| "Single wins" | A loss that follows a win inside a run, counted to the limit (7) | PRD says "L-W-L consecutive". |
| Take profit / stop loss | Per session and per day | PRD says "Daily" only. |
| Ties | Count as hands for pauses and keep-alive; ignored when choosing a side | Not specified. |
| Chip rounding | Round stakes up to the minimum chip | Not specified. |
| Keep-Alive | One minimum bet after N idle hands | The old app's defaults were amount **1** and **5** idle hands. Our page defaults to 0.1 and 10. Which does he want? What is the table's real inactivity timeout? |
| History at start | The road already on the table is not played; the strategy starts from the next hand | The old app read the table's history first. Should the pattern start from what is on screen? |
| After an outage | The hands missed are skipped, not replayed; a lost table alerts once | Confirm. |
| Restart after crash | The bot restarts idle and waits for START | Should it resume by itself? (We advise not, with real money.) |

## D. Telegram

* **Revoke the bot token** printed in the PRD (BotFather > /revoke) and send a new one **privately**; it was shared in plain text. We did not commit it.
* What counts as "disconnection": the table page left, Chrome closed, or no new result for N seconds? (We alert when the table is not on the page for 15 s.)
* Any other alerts wanted (session started / stopped, crash, restart)?

## E. Delivery and acceptance

* He runs the Windows package and (if he has one) the Mac package, and tells us what the first-run prompts show.
* He confirms he is happy with an unsigned (ad-hoc signed on the Mac) program, or pays for signing certificates.
* Windows VPS: edition, RAM, and whether it has a desktop session (a signed-in Chrome needs one).
* What "done" means to him: for example a 7-day paper-trading run, a number of sessions, and sign-off on the Backtest figures.
* Payment milestones tied to each deliverable, so the parts waiting on his answers do not hold up payment for the finished ones.

## F. Things we still have to check ourselves (not for the client)

* Run the CI workflow once so the macOS package is built, then start it on a real Apple-silicon Mac.
* Open one exported `.xlsx` in real Excel.
* Register the scheduled task on the real Windows VPS (only a dry run has been done).
* Check the Live feed against a real Roobet / Evolution table as soon as the capture from item A4 arrives.
