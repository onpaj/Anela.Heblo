import React from 'react';
import { Link } from 'react-router-dom';
import { Package, Truck, BarChart3 } from 'lucide-react';
import { useScreenView } from '../../telemetry/useScreenView';
import { usePackingDashboard } from '../../api/hooks/usePackingDashboard';

interface BaleniTile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
}

const TILES: BaleniTile[] = [
  {
    id: 'baleni',
    title: 'Balení',
    description: 'Zabalení zásilky pro odeslání',
    href: '/baleni/baleni',
    icon: Package,
  },
  {
    id: 'zasilky',
    title: 'Zásilky',
    description: 'Přehled zásilek a jejich stav',
    href: '/baleni/zasilky',
    icon: Truck,
  },
  {
    id: 'statistiky',
    title: 'Statistiky',
    description: 'Statistiky balicí stanice',
    href: '/baleni/statistiky',
    icon: BarChart3,
  },
];

const StatCard: React.FC<{
  label: string;
  value: React.ReactNode;
  loading: boolean;
  syncTime?: string | null;
}> = ({ label, value, loading, syncTime }) => (
  <div className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 shadow-soft dark:shadow-soft-dark">
    <p className="text-sm text-neutral-gray dark:text-graphite-muted mb-2">{label}</p>
    {loading ? (
      <div className="h-10 w-24 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded animate-pulse" />
    ) : (
      <>
        <p className="text-4xl font-bold text-primary-blue dark:text-graphite-accent">{value}</p>
        {syncTime && (
          <p className="text-xs text-neutral-gray dark:text-graphite-muted mt-2">
            sync {new Date(syncTime).toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
          </p>
        )}
      </>
    )}
  </div>
);

const BaleniHome: React.FC = () => {
  useScreenView('Baleni', 'BaleniHome');
  const { data, isLoading } = usePackingDashboard();

  return (
    <div className="pt-4 space-y-8">
      <section>
        <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text mb-4">Přehled</h2>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
          <StatCard
            label="Vyřizuje se (Shoptet)"
            value={data?.ordersBeingProcessedCount ?? '—'}
            loading={isLoading}
            syncTime={data?.ordersBeingPackedCountLastSync}
          />
          <StatCard
            label="Balí se (Shoptet)"
            value={data?.ordersBeingPackedCount ?? '—'}
            loading={isLoading}
            syncTime={data?.ordersBeingPackedCountLastSync}
          />
          <StatCard
            label="Zabaleno dnes"
            value={data?.totalOrdersPackedToday ?? '—'}
            loading={isLoading}
          />
        </div>

        <div className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 shadow-soft dark:shadow-soft-dark">
          <p className="text-sm text-neutral-gray dark:text-graphite-muted mb-4">Zabaleno dnes podle baličů</p>
          {isLoading ? (
            <div className="space-y-3">
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-8 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded animate-pulse" />
              ))}
            </div>
          ) : data && data.packedByPacker.length > 0 ? (
            <ul className="space-y-2">
              {data.packedByPacker.map((p) => (
                <li
                  key={p.packerId ?? p.packerName}
                  className="flex items-center justify-between py-2 px-3 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-lg"
                >
                  <span className="text-sm font-medium text-neutral-slate dark:text-graphite-text">{p.packerName}</span>
                  <span className="text-sm font-bold text-primary-blue dark:text-graphite-accent">{p.orderCount}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-neutral-gray dark:text-graphite-muted italic">Dnes zatím nikdo nezabalil žádnou objednávku.</p>
          )}
        </div>
      </section>

      <section>
        <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text mb-4">Navigace</h2>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          {TILES.map(({ id, title, description, href, icon: Icon }) => (
            <Link
              key={id}
              to={href}
              data-testid={`baleni-tile-${id}`}
              className="flex flex-col items-center justify-center gap-4 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 shadow-soft dark:shadow-soft-dark hover:border-primary-blue hover:shadow-hover transition-all min-h-[160px]"
            >
              <div className="w-16 h-16 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-xl flex items-center justify-center">
                <Icon className="h-8 w-8 text-primary-blue dark:text-graphite-accent" />
              </div>
              <div className="text-center">
                <p className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">{title}</p>
                <p className="text-sm text-neutral-gray dark:text-graphite-muted mt-1">{description}</p>
              </div>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
};

export default BaleniHome;
