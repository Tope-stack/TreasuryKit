import { useId, useState, type ReactNode } from 'react';
import { ApiError } from '../api/client';
import { num } from '../api/types';

const money = new Intl.NumberFormat('en-NG', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function Money({ value, currency }: { value: number | string | null | undefined; currency?: string }) {
  const amount = num(value);
  return (
    <span className={`money ${amount < 0 ? 'negative' : ''}`}>
      {currency ? `${currency} ` : ''}
      {money.format(amount)}
    </span>
  );
}

export function Badge({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'good' | 'warn' | 'bad' | 'info' }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

export function statusTone(status: string): 'neutral' | 'good' | 'warn' | 'bad' | 'info' {
  if (['Completed', 'Confirmed', 'Settled', 'Resolved'].includes(status)) return 'good';
  if (['Failed', 'Rejected', 'Reversed'].includes(status)) return 'bad';
  if (['BalanceMismatch', 'Proposed', 'Open', 'Ignored'].includes(status)) return 'warn';
  return 'info';
}

export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) return null;
  const text = error instanceof ApiError ? `${error.message} (${error.code})` : String(error);
  return (
    <div role="alert" className="banner error">
      {text}
    </div>
  );
}

export function Notice({ children }: { children: ReactNode }) {
  return (
    <div role="status" className="banner info">
      {children}
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

/** Asks for a reason or note before an action; the text is required unless `optional`. */
export function TextPrompt({
  label,
  action,
  onSubmit,
  optional = false,
  busy = false,
}: {
  label: string;
  action: string;
  onSubmit: (text: string) => void;
  optional?: boolean;
  busy?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const id = useId();
  if (!open) {
    return (
      <button type="button" className="secondary" onClick={() => setOpen(true)}>
        {action}
      </button>
    );
  }
  return (
    <form
      className="inline-form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit(text.trim());
        setOpen(false);
        setText('');
      }}
    >
      <label htmlFor={id}>{label}</label>
      <input id={id} value={text} onChange={(e) => setText(e.target.value)} autoFocus required={!optional} />
      <button type="submit" disabled={busy || (!optional && !text.trim())}>
        {action}
      </button>
      <button type="button" className="link" onClick={() => setOpen(false)}>
        Cancel
      </button>
    </form>
  );
}

export function FilePicker({ label, accept, onFile, busy }: { label: string; accept: string; onFile: (file: File) => void; busy?: boolean }) {
  const id = useId();
  return (
    <div className="file-picker">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type="file"
        accept={accept}
        disabled={busy}
        onChange={(e) => {
          const file = e.target.files?.[0];
          if (file) onFile(file);
          e.target.value = '';
        }}
      />
    </div>
  );
}

/** Score breakdown of a match or candidate, e.g. base 0.80 · date 0.10 · name 0.10. */
export function ScoreBreakdown({ breakdown }: { breakdown: Record<string, number | string> }) {
  const entries = Object.entries(breakdown);
  if (entries.length === 0) return null;
  return (
    <dl className="breakdown" aria-label="Score breakdown">
      {entries.map(([k, v]) => (
        <div key={k}>
          <dt>{k}</dt>
          <dd>{num(v).toLocaleString('en-NG', { maximumFractionDigits: 4 })}</dd>
        </div>
      ))}
    </dl>
  );
}
