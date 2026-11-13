import type { ReactNode } from 'react';
import type { MatchDetails } from '../api/types';
import { num } from '../api/types';
import { ItemTable } from './items';
import { Badge, Money, ScoreBreakdown, statusTone } from './ui';

/** A match with both sides shown, its rule, confidence and the score breakdown that explains it. */
export function MatchCard({ details, actions, select }: { details: MatchDetails; actions?: ReactNode; select?: ReactNode }) {
  const m = details.match;
  return (
    <article className="card match" aria-label={`Match ${m.id}`} data-testid="match-card" data-rule={m.ruleKey}>
      <header className="match-head">
        {select}
        <Badge tone={statusTone(m.status)}>{m.status}</Badge>
        <strong>{m.ruleKey}</strong>
        <span className="muted">
          {m.type} · confidence {num(m.confidence).toFixed(2)} · proposed by {m.proposedBy}
          {m.confirmedBy ? ` · confirmed by ${m.confirmedBy}` : ''}
        </span>
        {num(m.adjustment) !== 0 && (
          <Badge tone="warn">
            Adjustment <Money value={m.adjustment} />
          </Badge>
        )}
      </header>
      <div className="match-sides">
        <ItemTable caption="Bank statement" items={details.statementItems} />
        <ItemTable caption="Expected" items={details.expectedItems} />
      </div>
      <ScoreBreakdown breakdown={m.scoreBreakdown} />
      {m.reason && <p className="muted">Reason: {m.reason}</p>}
      {actions && <footer className="actions">{actions}</footer>}
    </article>
  );
}
