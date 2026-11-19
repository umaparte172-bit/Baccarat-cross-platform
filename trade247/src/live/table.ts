import type { BettingProfile } from './profile.js';

/**
 * Scripts that run inside the table's page. They are plain text (not transpiled functions) so no build tool can
 * insert helpers the page does not have. They read; they never click or type.
 */

export interface ChipInfo {
  /** Position among the page's chip elements; the bettor clicks by this index. */
  index: number;
  /** The chip's face value, or null when it could not be read. */
  value: number | null;
  visible: boolean;
}

export interface WindowReading {
  /** The status element exists in this frame. */
  found: boolean;
  /** The status text says bets are open. */
  open: boolean;
  text: string;
  chips: ChipInfo[];
  hasPlayer: boolean;
  hasBanker: boolean;
  /** The total staked on the table, null when the element is missing or has no number. */
  total: number | null;
  balance: number | null;
}

const NUMBER = String.raw`function(t){var m=String(t).replace(/,/g,'').match(/(-?\d+(?:\.\d+)?)\s*([kKmM])?/);if(!m)return null;var v=parseFloat(m[1]);if(m[2])v*=(m[2].toLowerCase()==='k'?1000:1000000);return v;}`;

/** Reads the betting window: status, chips, spots, total. */
export function windowScript(b: BettingProfile): string {
  const arg = JSON.stringify({
    status: b.statusSelector, open: b.openText, chips: b.chipSelector, from: b.chipValueFrom,
    player: b.playerSelector, banker: b.bankerSelector, total: b.betTotalSelector, balance: b.balanceSelector,
  });
  return `(function(p){
    var num=${NUMBER};
    var out={found:false,open:false,text:'',chips:[],hasPlayer:false,hasBanker:false,total:null,balance:null};
    var q=function(s){try{return document.querySelector(s);}catch(e){return null;}};
    var st=q(p.status);
    if(!st)return out;
    out.found=true;
    out.text=String(st.innerText||st.textContent||'').trim().slice(0,80);
    out.open=out.text.toLowerCase().indexOf(p.open.toLowerCase())>=0;
    var chips=[];try{chips=document.querySelectorAll(p.chips);}catch(e){}
    for(var i=0;i<chips.length;i++){
      var c=chips[i],r=c.getBoundingClientRect();
      var raw=p.from==='text'?(c.innerText||c.textContent||''):(c.getAttribute(p.from)||'');
      out.chips.push({index:i,value:num(raw),visible:r.width>0&&r.height>0});
    }
    out.hasPlayer=!!q(p.player);out.hasBanker=!!q(p.banker);
    var t=q(p.total);if(t)out.total=num(t.innerText||t.textContent||'');
    if(p.balance){var b=q(p.balance);if(b)out.balance=num(b.innerText||b.textContent||'');}
    return out;
  })(${arg})`;
}

/** Is the element usable as a click target: present, visible, enabled, and not covered by something else? */
export interface TargetCheck {
  found: boolean;
  visible: boolean;
  disabled: boolean;
  covered: boolean;
}

/** Called with the element as `this`: scrolls it into view and reports whether a click would reach it. */
export const CHECK_TARGET = `async function(){
  this.scrollIntoView({block:'center',inline:'center',behavior:'instant'});
  // Scrolling can move ancestor frames too. Wait for the compositor before
  // measuring and sending input; a DOM rect can precede the hit-test update.
  await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
  var r=this.getBoundingClientRect();
  var visible=r.width>0&&r.height>0;
  var disabled=!!(this.disabled||this.getAttribute('aria-disabled')==='true');
  var top=visible?document.elementFromPoint(r.left+r.width/2,r.top+r.height/2):null;
  var covered=visible&&!(top===this||this.contains(top));
  return {found:true,visible:visible,disabled:disabled,covered:covered};
}`;
