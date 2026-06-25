import React from 'react';
import { Link } from 'react-router-dom';
import { ClipboardList, PackagePlus, ChevronRight } from 'lucide-react';
import { useScreenView } from '../../../telemetry/useScreenView';

interface Tile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
}

const TILES: Tile[] = [
  {
    id: 'po',
    title: 'Příjem podle objednávky',
    description: 'Vyberte objednávku a štítkujte přijaté kontejnery',
    href: '/terminal/lot-identification/po',
    icon: ClipboardList,
  },
  {
    id: 'freeform',
    title: 'Volný příjem',
    description: 'Štítkujte kontejnery bez vazby na objednávku',
    href: '/terminal/lot-identification/freeform',
    icon: PackagePlus,
  },
];

const LotIdentificationHome = () => {
  useScreenView('Terminal', 'LotIdentificationHome');
  return (
    <div className="space-y-3 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate dark:text-graphite-text">Identifikace šarže</h1>
      {TILES.map(({ id, title, description, href, icon: Icon }) => (
        <Link
          key={id}
          to={href}
          data-testid={`lot-id-tile-${id}`}
          className="flex items-center gap-4 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-4 shadow-soft dark:shadow-soft-dark hover:border-primary-blue dark:hover:border-graphite-accent hover:shadow-hover transition-all min-h-[72px]"
        >
          <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-xl flex items-center justify-center">
            <Icon className="h-6 w-6 text-primary-blue dark:text-graphite-accent" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-base font-semibold text-neutral-slate dark:text-graphite-text">{title}</p>
            <p className="text-sm text-neutral-gray dark:text-graphite-muted mt-0.5">{description}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray dark:text-graphite-muted flex-shrink-0" />
        </Link>
      ))}
    </div>
  );
};

export default LotIdentificationHome;
