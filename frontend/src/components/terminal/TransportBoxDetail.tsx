import React, { useState } from 'react';
import TransportBoxStateBadge from '../transport/box-detail/components/TransportBoxStateBadge';
import {
  TransportBoxDto,
  TransportBoxItemDto,
  TransportBoxStateLogDto,
} from '../../api/generated/api-client';
import { formatDate, formatDateTime } from '../../utils/formatters';

type Tab = 'contents' | 'history';

const ContentsTab: React.FC<{ items: TransportBoxItemDto[] }> = ({ items }) => {
  if (items.length === 0) {
    return (
      <p className="text-sm text-neutral-gray py-6 text-center">
        Box neobsahuje žádné položky
      </p>
    );
  }
  return (
    <div className="space-y-2">
      {items.map((item) => (
        <div
          key={item.id}
          className="bg-white border border-border-light rounded-xl p-3"
        >
          <div className="flex justify-between gap-2">
            <span className="font-medium text-neutral-slate">
              {item.productName}
            </span>
            <span className="font-semibold text-neutral-slate whitespace-nowrap">
              {item.amount}
            </span>
          </div>
          <div className="text-xs text-neutral-gray">{item.productCode}</div>
          {(item.lotNumber || item.expirationDate) && (
            <div className="text-xs text-neutral-gray mt-1 flex flex-wrap gap-x-3 gap-y-0.5">
              {item.lotNumber && <span>Šarže: {item.lotNumber}</span>}
              {item.expirationDate && (
                <span>Expirace: {formatDate(item.expirationDate)}</span>
              )}
            </div>
          )}
        </div>
      ))}
    </div>
  );
};

const HistoryTab: React.FC<{ log: TransportBoxStateLogDto[] }> = ({ log }) => {
  if (log.length === 0) {
    return (
      <p className="text-sm text-neutral-gray py-6 text-center">
        Žádná historie změn
      </p>
    );
  }
  const ordered = [...log].sort(
    (a, b) =>
      (b.stateDate ? new Date(b.stateDate).getTime() : 0) -
      (a.stateDate ? new Date(a.stateDate).getTime() : 0),
  );
  return (
    <div className="space-y-2">
      {ordered.map((entry) => (
        <div
          key={entry.id}
          className="bg-white border border-border-light rounded-xl p-3 space-y-1"
        >
          <div className="flex justify-between items-center gap-2">
            <TransportBoxStateBadge state={entry.state ?? ''} size="sm" />
            <span className="text-xs text-neutral-gray">
              {formatDateTime(entry.stateDate)}
            </span>
          </div>
          {entry.user && (
            <div className="text-xs text-neutral-gray">{entry.user}</div>
          )}
          {entry.description && (
            <div className="text-sm text-neutral-slate">{entry.description}</div>
          )}
        </div>
      ))}
    </div>
  );
};

const BoxDetail: React.FC<{ box: TransportBoxDto }> = ({ box }) => {
  const [activeTab, setActiveTab] = useState<Tab>('contents');
  const items = box.items ?? [];
  const log = box.stateLog ?? [];

  const tabClass = (tab: Tab) =>
    `flex-1 py-2 text-sm font-medium border-b-2 transition-colors ${
      activeTab === tab
        ? 'border-primary-blue text-primary-blue'
        : 'border-transparent text-neutral-gray'
    }`;

  return (
    <div className="space-y-3">
      <div className="bg-white border border-border-light rounded-xl p-4 shadow-soft space-y-2">
        <div className="flex items-center justify-between gap-2">
          <span className="text-lg font-bold text-neutral-slate">
            {box.code}
          </span>
          <TransportBoxStateBadge state={box.state ?? ''} size="lg" />
        </div>
        {box.location && (
          <div className="text-sm text-neutral-gray">
            Umístění: {box.location}
          </div>
        )}
        {box.description && (
          <div className="text-sm text-neutral-slate">{box.description}</div>
        )}
        <div className="text-sm text-neutral-gray">
          Počet položek: {items.length}
        </div>
      </div>

      <div className="flex border-b border-border-light">
        <button
          type="button"
          data-testid="tab-contents"
          className={tabClass('contents')}
          onClick={() => setActiveTab('contents')}
        >
          Obsah ({items.length})
        </button>
        <button
          type="button"
          data-testid="tab-history"
          className={tabClass('history')}
          onClick={() => setActiveTab('history')}
        >
          Historie ({log.length})
        </button>
      </div>

      {activeTab === 'contents' ? (
        <ContentsTab items={items} />
      ) : (
        <HistoryTab log={log} />
      )}
    </div>
  );
};

export default BoxDetail;
