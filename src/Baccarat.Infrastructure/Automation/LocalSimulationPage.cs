namespace Baccarat.Infrastructure.Automation;

internal static class LocalSimulationPage
{
    internal const string Html = """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Baccarat · DOM training table</title><style>
        *{box-sizing:border-box}body{margin:0;background:#101b2d;color:#eaf3ff;font:15px system-ui}main{max-width:1050px;margin:auto;padding:24px}
        h1{margin:10px 0}.badge{color:#5ee4af;font-weight:700}section{background:#192c48;border:1px solid #345073;border-radius:14px;padding:20px;margin:16px 0}
        button{cursor:pointer;color:inherit;background:#254568;border:1px solid #6580a4;border-radius:6px;padding:10px 16px;margin:4px}button:disabled{opacity:.4;cursor:default}
        .table{display:grid;grid-template-columns:1fr 1.8fr 1fr 1.3fr 1fr;align-items:end;gap:2px;background:linear-gradient(#8d690d,#4b3409);padding:20px 8px 8px;border-radius:16px}
        .table button{height:114px;margin:0;padding:8px 2px;border:1px solid #2253bc;font-weight:800;text-shadow:0 1px #10213e;background:linear-gradient(#1348b3,#102a46);border-radius:4px;display:flex;flex-direction:column;justify-content:flex-end;align-items:center}
        .table small{font:700 12px system-ui;color:#55adff}.table .pp{border-radius:75px 4px 4px 75px;justify-content:center}
        .table .bp{border-radius:4px 75px 75px 4px;justify-content:center}.table .banker,.table .bp{background:linear-gradient(#d90c08,#6d1009);border-color:#bd150d}
        .table .bp small{color:#ffa34e}.table .tie{height:104px;border-radius:55px 55px 4px 4px;background:linear-gradient(#109516,#094709);border-color:#438b04}.table .tie small{color:#6df52a}
        .table [aria-pressed=true]{box-shadow:inset 0 0 0 3px #fdfdfd}.note{color:#adc0da;font-size:12px}#cards{white-space:pre-line;font-size:22px}#wager{color:#ffd78b}#message{min-height:24px}
        label{display:inline-flex;flex-direction:column;gap:5px;margin:8px}input,select{padding:9px;border-radius:5px;border:1px solid #6b7e9c;background:#10213a;color:white;max-width:180px}
        .buttons{display:flex;flex-wrap:wrap}output[hidden]{display:none}
        </style><main><div class="badge">LOCAL SIMULATION · NO MONEY · DOM CONTROL</div><h1 id="session">Baccarat training table</h1>
        <p id="status">Waiting for desktop DOM driver…</p><section><p id="next"></p><p id="balance"></p><p id="wager"></p><p id="chips"></p>
        <div class="table" aria-label="Record result and mark pairs">
        <button class="pp" data-action="player-pair" aria-pressed="false">P PAIR<small>11:1</small></button>
        <button data-action="player">PLAYER<small>1:1</small></button>
        <button class="tie" data-action="tie">TIE<small>8:1</small></button>
        <button class="banker" data-action="banker">BANKER<small>0.95:1</small></button>
        <button class="bp" data-action="banker-pair" aria-pressed="false">B PAIR<small>15:1</small></button></div>
        <p class="note">Reference-layout controls: Player / Banker / Tie record an observed result; pair buttons mark observations. Payout labels reproduce the reference, not enabled side bets. No fabricated player counts or money totals.</p>
        <div class="buttons"><button data-action="deal">Deal simulated hand</button><button data-action="undo">Undo</button><button data-action="shoe">New Shoe</button><button data-action="clear">Clear session…</button></div>
        <div id="cards"></div></section>
        <section id="settings"><h2>Strategy settings</h2><label>Strategy<select id="strategy"><option>PATTERN</option><option>Martingale</option><option>Grinding</option><option>Sequence</option><option>Odd Even</option><option>ODD Diff</option><option>ODD Looser</option><option>ODD is God</option></select></label>
        <label>Base unit<input id="unit" value="1"></label><label>Starting bankroll<input id="bankroll" value="1000"></label><div id="fields"></div><button data-action="apply">Apply settings</button><button data-action="draft">Return to strategy drafts</button></section>
        <section><h2>Controlled simulation</h2><div class="buttons"><button data-action="connect">Connect simulation</button><button data-action="recover">Recover connection</button><button data-action="initial">Prepare initial bet</button><button data-action="approve">Approve pending bet</button><button data-action="auto">Toggle auto-bet</button><button data-action="enable-auto">Enable auto-bet</button><button data-action="disable-auto">Disable auto-bet</button><button data-action="pause">Pause</button></div>
        <p class="note">The desktop driver reads and clicks this page's DOM. No /state or /deal API exists. Auto-bet prepares recommendations; every simulated placement needs approval.</p></section>
        <p id="message" role="status"></p><output id="dom-command" hidden></output></main><script src="/app.js"></script></html>
        """;

    internal const string Script = """
        'use strict';
        let clearArmed=false;
        for(const button of document.querySelectorAll('[data-action]'))button.addEventListener('click',()=>{
          const output=document.getElementById('dom-command');if(output.textContent)return;
          const action=button.dataset.action;
          if(action==='clear'&&!clearArmed&&button.dataset.confirmed!=='true'){clearArmed=true;button.textContent='Confirm clear session';return;}
          delete button.dataset.confirmed;
          clearArmed=false;document.querySelector('[data-action="clear"]').textContent='Clear session…';
          const fields={};for(const input of document.querySelectorAll('[data-field]'))fields[input.dataset.field]=input.value;
          output.textContent=JSON.stringify({action,roundKey:document.body.dataset.round,
            strategy:document.getElementById('strategy').value,unit:document.getElementById('unit').value,
            bankroll:document.getElementById('bankroll').value,fields});
        });
        """;
}
