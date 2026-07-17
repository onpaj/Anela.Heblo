import { CheckCircle, Loader2, AlertCircle } from 'lucide-react';
import { ScannedBox } from './receiveMachine';

interface ScannedBoxListProps {
  boxes: ScannedBox[];
  savedCount: number;
}

const BoxRow = ({ box }: { box: ScannedBox }) => {
  const isAlert = box.status === 'error' || box.status === 'duplicate';
  return (
    <li
      role={isAlert ? 'alert' : undefined}
      className="flex items-center gap-2 border-t border-border-light dark:border-graphite-border py-2 first:border-t-0"
    >
      <span className="font-mono text-sm text-neutral-slate dark:text-graphite-text">{box.code}</span>
      <span className="flex-1" />
      {box.status === 'saving' && (
        <Loader2 className="h-4 w-4 animate-spin text-neutral-gray dark:text-graphite-muted" />
      )}
      {box.status === 'saved' && (
        <span className="flex items-center gap-1 text-sm text-green-600 dark:text-emerald-400">
          <CheckCircle className="h-4 w-4" /> Uloženo
        </span>
      )}
      {isAlert && (
        <span className="flex items-center gap-1 text-right text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4 flex-shrink-0" /> {box.message}
        </span>
      )}
    </li>
  );
};

const ScannedBoxList = ({ boxes, savedCount }: ScannedBoxListProps) => (
  <div className="space-y-2">
    <p className="text-sm text-neutral-gray dark:text-graphite-muted">
      Naskenováno: <span className="font-semibold text-neutral-slate dark:text-graphite-text">{savedCount}</span>
    </p>
    {boxes.length > 0 && (
      <ul data-testid="receive-box-list" className="rounded-xl border border-border-light dark:border-graphite-border px-3">
        {boxes.map((box) => (
          <BoxRow key={box.id} box={box} />
        ))}
      </ul>
    )}
  </div>
);

export default ScannedBoxList;
