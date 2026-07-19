import React from 'react';
import { Link } from 'react-router-dom';
import { ClipboardList, PackagePlus, ChevronRight } from 'lucide-react';
import { useScreenView } from '../../../telemetry/useScreenView';

interface Tile {
  id: string;
  title: string;
  description: string;
  cta: string;
  href: string;
  icon: React.ElementType;
  iconBg: string;
  ctaColor: string;
}

const TILES: Tile[] = [
  {
    id: 'po',
    title: 'Příjem podle objednávky',
    description: 'Vyberte objednávku a štítkujte přijaté kontejnery',
    cta: 'Vybrat objednávku',
    href: '/terminal/lot-identification/po',
    icon: ClipboardList,
    iconBg: 'bg-violet-600',
    ctaColor: 'text-violet-600 dark:text-violet-400',
  },
  {
    id: 'freeform',
    title: 'Volný příjem',
    description: 'Štítkujte kontejnery bez vazby na objednávku',
    cta: 'Začít příjem',
    href: '/terminal/lot-identification/freeform',
    icon: PackagePlus,
    iconBg: 'bg-indigo-600',
    ctaColor: 'text-indigo-600 dark:text-indigo-400',
  },
];

const LotIdentificationHome = () => {
  useScreenView('Terminal', 'LotIdentificationHome');
  return (
    <div className="h-full">
      <div className="max-w-md mx-auto w-full h-full p-4 grid grid-rows-2 gap-4">
        {TILES.map(({ id, title, description, cta, href, icon: Icon, iconBg, ctaColor }) => (
          <Link
            key={id}
            to={href}
            data-testid={`lot-id-tile-${id}`}
            className="relative flex flex-col justify-center gap-2 min-h-0 overflow-hidden bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-2xl p-6 shadow-soft dark:shadow-soft-dark hover:border-primary-blue dark:hover:border-graphite-accent hover:shadow-hover transition-all"
          >
            <div className={`flex-shrink-0 w-14 h-14 rounded-2xl flex items-center justify-center text-white shadow-lg ${iconBg}`}>
              <Icon className="h-7 w-7" />
            </div>
            <p className="mt-1 text-xl font-bold text-neutral-slate dark:text-graphite-text">{title}</p>
            <p className="text-sm text-neutral-gray dark:text-graphite-muted">{description}</p>
            <span className={`mt-1 inline-flex items-center gap-1 text-sm font-semibold ${ctaColor}`}>
              {cta}
              <ChevronRight className="h-4 w-4" />
            </span>
          </Link>
        ))}
      </div>
    </div>
  );
};

export default LotIdentificationHome;
