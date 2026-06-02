import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Package, ClipboardList, Tag, PackageSearch, PackagePlus } from 'lucide-react';
import { useScreenView } from '../../telemetry/useScreenView';
import { useScanScreen } from './shell/useScanScreen';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';

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

  const navigate = useNavigate();
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching, isError } = useTransportBoxByCodeQuery(scannedCode);
  const { flash } = useScanScreen({ onScan: (code) => setScannedCode(code) });

  // Scan-first routing: the wedge is live on Home, so a scan here resolves the box
  // and jumps straight to the right workflow (the operator re-scans on arrival).
  // NOTE: the state→route mapping below is provisional and should be validated with
  // floor procedure (per the design's open question on scan-first routing).
  React.useEffect(() => {
    if (!scannedCode || isFetching) return;
    if (isError || !box) {
      flash('err', scannedCode);
      setScannedCode(null);
      return;
    }
    if (box.isReceivable === true) {
      flash('ok', box.code ?? scannedCode);
      navigate('/terminal/receive');
    } else if (box.state === 'Opened' || box.state === 'New') {
      flash('ok', box.code ?? scannedCode);
      navigate('/terminal/box-fill');
    } else {
      flash('ok', box.code ?? scannedCode);
      navigate('/terminal/box-check');
    }
    setScannedCode(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scannedCode, isFetching, isError, box]);

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-md mx-auto w-full p-4 space-y-4">
        <h1 className="text-xl font-bold text-neutral-slate">Vyberte operaci</h1>
        <div className="grid grid-cols-2 gap-4">
          {WORKFLOWS.map(({ id, title, description, href, icon: Icon, comingSoon }) => (
            <Link
              key={id}
              to={href}
              data-testid={`workflow-tile-${id}`}
              className="flex flex-col gap-2 bg-white border border-border-light rounded-xl p-4 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[140px]"
            >
              <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
                <Icon className="h-6 w-6 text-primary-blue" />
              </div>
              <p className="text-base font-semibold text-neutral-slate">{title}</p>
              <p className="text-sm text-neutral-gray">{description}</p>
              {comingSoon && (
                <span className="text-xs text-neutral-gray italic mt-auto">Brzy k dispozici</span>
              )}
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
};

export default TerminalHome;
