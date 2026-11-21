import type { Item } from "../api";
import { CATEGORY_LABELS, formatQty, formatValue } from "../format";
import { ItemIcon } from "./ItemIcon";

interface Props {
  items: Item[];
  divine: number;
  showSource?: boolean;
  showCategory?: boolean;
}

export function ItemTable({ items, divine, showSource, showCategory }: Props) {
  if (items.length === 0) return <div className="empty">Không có item nào.</div>;
  const total = items.reduce((s, i) => s + i.totalChaos, 0);
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th className="icon-cell" />
            <th>Item</th>
            {showCategory && <th>Loại</th>}
            {showSource && <th>Ở tab</th>}
            <th className="num">SL</th>
            <th className="num">Đơn giá</th>
            <th className="num">Tổng</th>
          </tr>
        </thead>
        <tbody>
          {items.map((i) => (
            <tr key={`${i.sourceKey ?? ""}|${i.itemKey}`}>
              <td className="icon-cell">
                <ItemIcon src={i.icon} />
              </td>
              <td className={i.unitChaos === 0 ? "dim-cell" : ""}>{i.name}</td>
              {showCategory && <td className="dim-cell">{CATEGORY_LABELS[i.category] ?? i.category}</td>}
              {showSource && <td className="dim-cell">{i.sourceName}</td>}
              <td className="num dim-cell">{formatQty(i.quantity)}</td>
              <td className="num dim-cell">{i.unitChaos > 0 ? formatValue(i.unitChaos, divine) : "–"}</td>
              <td className="num">{formatValue(i.totalChaos, divine)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="table-foot">
        <span>{items.length} dòng</span>
        <strong>{formatValue(total, divine)}</strong>
      </div>
    </div>
  );
}
