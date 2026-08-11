import React from "react";
import { X } from "lucide-react";

// Verified against mind-elixir's own key map in node_modules/mind-elixir/dist/MindElixir.js
// (the handler object containing `Enter: (`, plus the undo plugin's ⌘Z/⌘⇧Z/⌘Y listener).
// Do not add a row without finding it there — a help sheet that lies is worse than none.
const SHORTCUTS: Array<[string, string]> = [
  ["klik", "vybrat uzel"],
  ["dvojklik / F2", "psát do uzlu"],
  ["Enter", "nový uzel vedle vybraného (u kořene: nové podřízené)"],
  ["⇧Enter", "nový uzel před vybraný"],
  ["Tab", "nový uzel pod vybraný"],
  ["⌘Enter", "vložit nadřazený uzel"],
  ["⌫", "smazat vybraný uzel i s podřízenými"],
  ["↑ ↓ ← →", "chodit po mapě"],
  ["⌥↑ / ⌥↓", "posunout mezi sourozenci"],
  ["⌘Z / ⌘⇧Z", "zpět / znovu"],
  ["⌘= / ⌘− / ⌘0", "přiblížit / oddálit / původní velikost"],
  ["F1", "vycentrovat"],
  ["mezerník + tažení", "posunout plátno"],
  ["tažení uzlu", "přesunout pod jiný uzel, nebo přeřadit mezi sourozenci (podle místa puštění)"],
  ["⌘S", "uložit mapu"],
];

const KEY_CLASS =
  "font-mono text-[11.5px] bg-gray-100 border border-b-2 border-gray-300 rounded px-1.5 py-0.5 whitespace-nowrap dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text";

export interface MindMapHelpSheetProps {
  onClose: () => void;
}

const MindMapHelpSheet: React.FC<MindMapHelpSheetProps> = ({ onClose }) => (
  <div
    className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
    onClick={onClose}
    data-testid="mindmap-help-sheet"
  >
    <div
      className="flex max-h-[82%] w-full max-w-xl flex-col overflow-hidden rounded-xl bg-white shadow-lg dark:bg-graphite-surface dark:shadow-soft-dark"
      onClick={(e) => e.stopPropagation()}
    >
      <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-graphite-border">
        <h2 className="text-sm font-semibold dark:text-graphite-text">Jak s mapou pracovat</h2>
        <button
          type="button"
          onClick={onClose}
          aria-label="Zavřít"
          className="text-gray-400 hover:text-gray-600 dark:text-graphite-faint dark:hover:text-graphite-muted"
        >
          <X className="h-5 w-5" />
        </button>
      </div>

      <div className="overflow-y-auto px-5 py-4">
        <table className="w-full border-collapse text-sm">
          <tbody>
            {SHORTCUTS.map(([keys, description]) => (
              <tr key={keys} className="border-b border-gray-100 last:border-0 dark:border-graphite-border/50">
                <td className="w-48 py-1.5 pr-3 align-top">
                  <span className={KEY_CLASS}>{keys}</span>
                </td>
                <td className="py-1.5 align-top text-gray-700 dark:text-graphite-muted">{description}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="mt-4 text-xs text-gray-500 dark:text-graphite-muted">
          Rozložení mapy se dopočítává automaticky — kořen je uprostřed a větve se střídavě rozrůstají doprava a
          doleva. Uzly lze přetahovat pod jiné uzly; jejich poloha se neukládá. Větev sbalíte kolečkem na jejím
          okraji. Zkratky <b>⌘←</b> a <b>⌘→</b> přepnou celou mapu na jednostranné rozložení — zpět ji vrátíte
          pomocí <b>⌘↑</b>; toto nastavení se neukládá.
        </p>
      </div>

      <div className="px-5 pb-4">
        <button
          type="button"
          onClick={onClose}
          className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Jasně
        </button>
      </div>
    </div>
  </div>
);

export default MindMapHelpSheet;
