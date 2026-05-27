import React from 'react';
import { Link } from 'react-router-dom';
import { Package, Truck, BarChart3 } from 'lucide-react';
import { useScreenView } from '../../telemetry/useScreenView';

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

const BaleniHome: React.FC = () => {
  useScreenView('Baleni', 'BaleniHome');
  return (
    <div className="pt-4">
      <h1 className="text-2xl font-bold text-neutral-slate mb-6">Vyberte operaci</h1>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {TILES.map(({ id, title, description, href, icon: Icon }) => (
          <Link
            key={id}
            to={href}
            data-testid={`baleni-tile-${id}`}
            className="flex flex-col items-center justify-center gap-4 bg-white border border-border-light rounded-xl p-6 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[160px]"
          >
            <div className="w-16 h-16 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
              <Icon className="h-8 w-8 text-primary-blue" />
            </div>
            <div className="text-center">
              <p className="text-lg font-semibold text-neutral-slate">{title}</p>
              <p className="text-sm text-neutral-gray mt-1">{description}</p>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
};

export default BaleniHome;
