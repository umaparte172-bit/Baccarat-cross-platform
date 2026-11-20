/**
 * Every Telegram message the bot sends has the same shape, so it can be read at a glance on a phone:
 *
 *   <icon> <CATEGORY> · <title>
 *   ━━━━━━━━━━━━━━━━
 *   📂 Category: what kind of event this is
 *   ⚙️ Action:   what the bot did about it
 *   🎯 Purpose:  why it did that
 *   (optional detail line)
 *   ━━━━━━━━━━━━━━━━
 *   session figures, time, and which commands help next
 *
 * Plain text only (no Markdown/HTML parse mode), so a figure or a casino message can never break the formatting.
 */

export type AlertKind =
  | 'daily_stop_loss'
  | 'daily_take_profit'
  | 'disconnected'
  | 'reconnected'
  | 'shoe_change'
  | 'bet_problem'
  | 'session_started'
  | 'session_stopped'
  | 'bet_waiting'
  | 'test';

export type AlertCategory = 'RISK CONTROL' | 'PROFIT TARGET' | 'CONNECTION' | 'TABLE EVENT' | 'BETTING' | 'SESSION' | 'SYSTEM';

export interface AlertSpec {
  icon: string;
  category: AlertCategory;
  /** One line for the category: what kind of event it is. */
  categoryText: string;
  title: string;
  action: string;
  purpose: string;
  /** Commands worth suggesting with this alert. */
  next: string[];
}

export const ALERTS: Record<AlertKind, AlertSpec> = {
  daily_stop_loss: {
    icon: '🛑', category: 'RISK CONTROL', categoryText: 'Risk control - daily loss limit',
    title: 'Daily stop loss hit',
    action: 'Session stopped. The day is locked: no new session starts until tomorrow or until the limit is raised.',
    purpose: 'Caps the loss for the day so one bad run cannot drain the account.',
    next: ['/status', '/report'],
  },
  daily_take_profit: {
    icon: '🏁', category: 'PROFIT TARGET', categoryText: 'Profit target - daily take profit',
    title: 'Daily take profit hit',
    action: 'Session stopped and the profit is kept. The day is locked until tomorrow or until the target is raised.',
    purpose: 'Banks the day\'s target instead of giving it back to the table.',
    next: ['/status', '/report'],
  },
  disconnected: {
    icon: '⚠️', category: 'CONNECTION', categoryText: 'Connection - live table',
    title: 'Table connection lost',
    action: 'No bets are placed while disconnected. The bot keeps looking for the table and reconnects by itself.',
    purpose: 'Never bet without seeing the table.',
    next: ['/retry', '/status', '/stop'],
  },
  reconnected: {
    icon: '✅', category: 'CONNECTION', categoryText: 'Connection - live table',
    title: 'Table connection restored',
    action: 'Reading the table again. Hands that were missed during the outage are not replayed.',
    purpose: 'Lets you know the bot is back in control without having to check.',
    next: ['/status'],
  },
  shoe_change: {
    icon: '🔄', category: 'TABLE EVENT', categoryText: 'Table event - shoe change (reshuffle)',
    title: 'New shoe',
    action: '', // depends on the shoe-change setting, see shoeAction()
    purpose: 'Keeps a losing progression from being carried into a fresh shoe.',
    next: ['/status'],
  },
  bet_problem: {
    icon: '❗', category: 'BETTING', categoryText: 'Betting - placement on the table',
    title: 'Bet problem',
    action: 'Session stopped. Nothing is repeated or retried.',
    purpose: 'Money may be on the table that was not confirmed; a person must check it before betting continues.',
    next: ['/status', '/start'],
  },
  session_started: {
    icon: '▶️', category: 'SESSION', categoryText: 'Session - start',
    title: 'Session started',
    action: 'The bot is reading results and following the strategy.',
    purpose: 'Confirms the start, with the settings it uses.',
    next: ['/status', '/pause', '/stop'],
  },
  session_stopped: {
    icon: '⏹️', category: 'SESSION', categoryText: 'Session - stop',
    title: 'Session stopped',
    action: 'No more bets are placed. The log and the report are saved.',
    purpose: 'Tells you the bot is no longer playing, and why.',
    next: ['/report', '/start'],
  },
  bet_waiting: {
    icon: '🟡', category: 'BETTING', categoryText: 'Betting - approval needed (Confirm mode)',
    title: 'Bet waiting for approval',
    action: 'Nothing is placed until you approve. It is skipped automatically when the approval time runs out.',
    purpose: 'In Confirm mode every real bet needs a person to say yes.',
    next: ['/approve', '/skip'],
  },
  test: {
    icon: '🧪', category: 'SYSTEM', categoryText: 'System - alert check',
    title: 'Test message',
    action: 'None. Alerts reach this chat.',
    purpose: 'Confirms the bot token and chat ID are right.',
    next: ['/help', '/status'],
  },
};

export function shoeAction(policy: string): string {
  switch (policy) {
    case 'reset_all':
      return 'Open losing sequence cut; the stake is back to the base unit and pattern reading restarts.';
    case 'reset_pattern':
      return 'Pattern reading restarts for the new shoe; the open money sequence is kept.';
    default:
      return 'Nothing reset: "Shoe change" is set to Continue in the settings, so the open sequence carries on.';
  }
}

export interface AlertContext {
  session: number;
  strategy: string;
  hands: number;
  /** Already formatted money strings. */
  sessionPnl: string;
  todayPnl: string;
  balance: string;
  status: string;
  time: string;
  /** "Paper", "Confirm (real money)", ... */
  betMode?: string;
}

const RULE = '━━━━━━━━━━━━━━━━';

export function formatAlert(kind: AlertKind, detail: string, ctx: AlertContext | null, overrides: Partial<AlertSpec> = {}): string {
  const spec = { ...ALERTS[kind], ...overrides };
  const lines = [
    `${spec.icon} ${spec.category} · ${spec.title}`,
    RULE,
    `📂 Category: ${spec.categoryText}`,
    `⚙️ Action: ${spec.action}`,
    `🎯 Purpose: ${spec.purpose}`,
  ];
  if (detail) lines.push(`ℹ️ Detail: ${detail}`);
  if (ctx) {
    lines.push(
      RULE,
      `📊 Session #${ctx.session} · ${ctx.strategy} · ${ctx.hands} hands${ctx.betMode ? ` · ${ctx.betMode}` : ''}`,
      `💵 Session P&L: ${ctx.sessionPnl} | Today: ${ctx.todayPnl}`,
      `💰 Balance: ${ctx.balance}`,
      `📍 State: ${ctx.status}`,
      `🕒 ${ctx.time}`,
    );
  }
  if (spec.next.length) lines.push(`👉 ${spec.next.join(' · ')} · /help`);
  return lines.join('\n');
}
