import React from 'react';
import { Link } from 'react-router-dom';
import { Package, ClipboardList, Tag, PackageSearch, PackagePlus, ChevronRight } from 'lucide-react';
import { useScreenView } from '../../telemetry/useScreenView';

interface WorkflowTile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
  comingSoon: boolean;
}

const WORKFLOWS: WorkflowTile[] = [
  {
    id: 'box-check',
    title: 'Kontrola boxu',
    description: 'Naskenujte kód boxu a zobrazte jeho obsah a historii',
    href: '/terminal/box-check',
    icon: PackageSearch,
    comingSoon: false,
  },
  {
    id: 'box-fill',
    title: 'Plnění boxu',
    description: 'Naskenujte box, přidejte produkty a odešlete do přepravy',
    href: '/terminal/box-fill',
    icon: PackagePlus,
    comingSoon: false,
  },
  {
    id: 'receive',
    title: 'Příjem boxu',
    description: 'Naskenujte kód boxu a potvrďte příjem zásilky do skladu',
    href: '/terminal/receive',
    icon: Package,
    comingSoon: false,
  },
  {
    id: 'stocktake',
    title: 'Inventura',
    description: 'Inventarizace materiálu a surovin po šaržích',
    href: '/terminal/stocktake',
    icon: ClipboardList,
    comingSoon: true,
  },
  {
    id: 'lot-identification',
    title: 'Identifikace šarže',
    description: 'Evidujte šarže při příjmu a sledujte spotřebu ve výrobě',
    href: '/terminal/lot-identification',
    icon: Tag,
    comingSoon: false,
  },
];

const TerminalHome: React.FC = () => {
  useScreenView('Terminal', 'TerminalHome');
  return (
    <div className="space-y-3 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate">Vyberte operaci</h1>
      {WORKFLOWS.map(({ id, title, description, href, icon: Icon, comingSoon }) => (
        <Link
          key={id}
          to={href}
          data-testid={`workflow-tile-${id}`}
          className="flex items-center gap-4 bg-white border border-border-light rounded-xl p-4 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[72px]"
        >
          <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
            <Icon className="h-6 w-6 text-primary-blue" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-base font-semibold text-neutral-slate">{title}</p>
            <p className="text-sm text-neutral-gray mt-0.5">{description}</p>
            {comingSoon && (
              <span className="text-xs text-neutral-gray italic">Brzy k dispozici</span>
            )}
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray flex-shrink-0" />
        </Link>
      ))}
    </div>
  );
};

export default TerminalHome;
