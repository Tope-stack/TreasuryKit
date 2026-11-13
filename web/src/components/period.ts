import { useSearchParams } from 'react-router-dom';

/** The selected month (YYYY-MM) lives in the URL so pages can be bookmarked and shared. */
export function useMonth(): { month: string; from: string; to: string; setMonth: (month: string) => void } {
  const [params, setParams] = useSearchParams();
  const month = params.get('month') ?? new Date().toISOString().slice(0, 7);
  const [year, mon] = month.split('-').map(Number) as [number, number];
  const last = new Date(Date.UTC(year, mon, 0)).getUTCDate();
  return {
    month,
    from: `${month}-01`,
    to: `${month}-${String(last).padStart(2, '0')}`,
    setMonth: (value) =>
      setParams((current) => {
        current.set('month', value);
        return current;
      }),
  };
}
