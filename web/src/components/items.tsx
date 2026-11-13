import type { ItemDetail } from '../api/types';
import { Money } from './ui';

/** Statement lines or expected transactions in a compact table. */
export function ItemTable({ items, caption }: { items: ItemDetail[]; caption: string }) {
  return (
    <table className="items">
      <caption>{caption}</caption>
      <thead>
        <tr>
          <th scope="col">Date</th>
          <th scope="col">Description</th>
          <th scope="col">Reference</th>
          <th scope="col" className="num">
            Amount
          </th>
        </tr>
      </thead>
      <tbody>
        {items.map((item) => (
          <tr key={item.id}>
            <td>{item.date}</td>
            <td>
              {item.description || '—'}
              {item.counterparty && <div className="muted">{item.counterparty}</div>}
            </td>
            <td>{item.reference ?? '—'}</td>
            <td className="num">
              <Money value={item.amount} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
