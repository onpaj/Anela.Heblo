import { useReducer, useRef, useCallback, useEffect } from 'react';
import { useNavigate, useParams, useLocation } from 'react-router-dom';
import { Check, Package } from 'lucide-react';
import StepSection, { StepState } from './StepSection';
import ScannedBoxList from './ScannedBoxList';
import StepInput from './StepInput';
import LotSessionHeader from './LotSessionHeader';
import CatalogAutocomplete from '../../common/CatalogAutocomplete';
import { CatalogItemDto, ProductType, CreateMaterialContainerItem } from '../../../api/generated/api-client';
import { useCreateMaterialContainers, useLastUsedLotForMaterial } from '../../../api/hooks/useMaterialContainers';
import { ErrorCodes } from '../../../types/errors';
import { useScreenView } from '../../../telemetry/useScreenView';
import { ScanShell } from '../shell/ScanShell';
import { useScanScreen } from '../shell/useScanScreen';
import {
  init,
  receiveReducer,
  savedCount,
  ReceiveMode,
  ReceiveState,
} from './receiveMachine';

const CODE_FORMAT = /^M\d{8}$/;
const INVALID_FORMAT_MESSAGE = 'Neplatný formát kódu (očekáváno M + 8 číslic).';
const CONNECTION_ERROR_MESSAGE = 'Chyba připojení.';
const GENERIC_SAVE_ERROR_MESSAGE = 'Chyba při ukládání kontejneru.';
const UNKNOWN_CODE_MESSAGE = 'Neznámý štítek – nejprve jej vygenerujte v aplikaci.';
const LOT_LABEL = 'Šarže (z etikety dodavatele)';
const SCAN_LABEL = 'Kód kontejneru (Mxxxxxxxx)';

interface ReceiveScreenProps {
  mode: ReceiveMode;
}

const ReceiveScreen = ({ mode }: ReceiveScreenProps) => {
  useScreenView('Terminal', 'LotIdentificationReceive', mode);
  const params = useParams<{ id?: string; lineId?: string; material?: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const poLineId = params.lineId ? parseInt(params.lineId, 10) : undefined;

  const [state, dispatch] = useReducer(receiveReducer, mode, (m: ReceiveMode): ReceiveState => {
    if (m === 'po') {
      const code = params.material ? decodeURIComponent(params.material) : '';
      const name = (location.state as { materialName?: string } | null)?.materialName ?? code;
      return init({ mode: 'po', material: { code, name } });
    }
    return init({ mode: 'freeform' });
  });

  const create = useCreateMaterialContainers();
  const { data: lastLot } = useLastUsedLotForMaterial(state.material?.code ?? null);
  const scanIdRef = useRef(0);

  // The screen owns three visible step inputs, which are the scan-capture fields (a hardware wedge
  // types into the focused field). Register with the shell to get the flash channel and to tell the
  // singleton wedge to yield focus while this screen is mounted. onScan is a hardware-trigger fallback
  // routed to the active step.
  const onScanRef = useRef<(code: string) => void>(() => {});
  const { flash, setYieldFocus } = useScanScreen({ onScan: (code) => onScanRef.current(code) });

  useEffect(() => {
    setYieldFocus(true);
    return () => setYieldFocus(false);
  }, [setYieldFocus]);

  const materialCode = state.material?.code ?? '';
  const lotCode = state.lotCode ?? '';

  const handleSelectMaterial = useCallback(
    (item: CatalogItemDto | null) => {
      if (!item?.productCode) return;
      dispatch({ type: 'SELECT_MATERIAL', code: item.productCode, name: item.productName ?? item.productCode });
      flash('ok', item.productCode);
    },
    [flash],
  );

  const handleConfirmLot = useCallback(
    (lot: string) => {
      const value = lot.trim();
      if (!value) return;
      dispatch({ type: 'CONFIRM_LOT', lot: value });
      flash('ok', value);
    },
    [flash],
  );

  const handleScan = useCallback(
    (raw: string) => {
      const code = raw.trim();
      const id = (scanIdRef.current += 1);
      if (!CODE_FORMAT.test(code)) {
        dispatch({ type: 'REJECT_SCAN', id, code, message: INVALID_FORMAT_MESSAGE });
        flash('err', code);
        return;
      }
      dispatch({ type: 'ADD_SCAN', id, code });
      create.mutate(
        { items: [new CreateMaterialContainerItem({ code, materialCode, lotCode, purchaseOrderLineId: poLineId })] },
        {
          onSuccess: (data) => {
            if (data.success) {
              dispatch({ type: 'RESOLVE_SCAN', id, status: 'saved' });
              flash('ok', code);
            } else if (data.errorCode === ErrorCodes.MaterialContainerCodeExists) {
              const status = data.params?.['Status'];
              const assignedMaterial = data.params?.['MaterialCode'];
              const assignedLot = data.params?.['LotCode'];
              const message =
                status === 'Discarded'
                  ? 'Tento kód byl vyřazen, použijte nový štítek.'
                  : `Kód ${code} je již přiřazen k materiálu ${assignedMaterial} / šarži ${assignedLot}.`;
              dispatch({ type: 'RESOLVE_SCAN', id, status: 'duplicate', message });
              flash('warn', code);
            } else if (data.errorCode === ErrorCodes.UnknownMaterialContainerCode) {
              dispatch({ type: 'RESOLVE_SCAN', id, status: 'error', message: UNKNOWN_CODE_MESSAGE });
              flash('err', code);
            } else {
              dispatch({ type: 'RESOLVE_SCAN', id, status: 'error', message: GENERIC_SAVE_ERROR_MESSAGE });
              flash('err', code);
            }
          },
          onError: () => {
            dispatch({ type: 'RESOLVE_SCAN', id, status: 'error', message: CONNECTION_ERROR_MESSAGE });
            flash('err', code);
          },
        },
      );
    },
    [materialCode, lotCode, poLineId, create, flash],
  );

  const handleFinish = () => {
    if (mode === 'po') {
      navigate(`/terminal/lot-identification/po/${params.id}`);
    } else {
      dispatch({ type: 'RESET' });
    }
  };

  // Route a hardware-wedge scan (if one ever reaches the singleton wedge) to the active step.
  onScanRef.current = (code: string) => {
    if (state.activeStep === 'lot') handleConfirmLot(code);
    else if (state.activeStep === 'scan') handleScan(code);
  };

  // --- step states ---------------------------------------------------------
  const materialStepState: StepState =
    state.activeStep === 'material' ? 'active' : state.materialLocked ? 'locked' : 'done';
  const lotStepState: StepState =
    state.activeStep === 'lot' ? 'active' : state.lotCode ? 'done' : 'pending';
  const scanStepState: StepState = state.activeStep === 'scan' ? 'active' : 'pending';

  const materialValue = state.material
    ? new CatalogItemDto({ productCode: state.material.code, productName: state.material.name })
    : null;

  // --- session subject (zone B) --------------------------------------------
  const modeLabel = mode === 'po' ? 'Příjem podle objednávky' : 'Volný příjem';
  const saved = savedCount(state);
  const facts = state.material
    ? `${state.material.name} · ${saved} uloženo`
    : mode === 'po'
      ? 'Naskenujte šarži a kontejnery'
      : 'Vyberte materiál';

  const subject = (
    <LotSessionHeader title={modeLabel} code={state.material?.code} facts={facts} icon={Package} />
  );

  return (
    <ScanShell
      subject={subject}
      actions={[
        {
          label: 'Hotovo',
          onClick: handleFinish,
          variant: 'primary',
          icon: <Check className="h-5 w-5" />,
          testId: 'receive-finish',
        },
      ]}
    >
      <div className="space-y-4">
        {/* Step 1 — material */}
        <StepSection
          step={1}
          title="Materiál"
          state={materialStepState}
          onEdit={materialStepState === 'done' ? () => dispatch({ type: 'EDIT_MATERIAL' }) : undefined}
          testId="receive-step-material"
        >
          {materialStepState === 'active' ? (
            <CatalogAutocomplete
              value={materialValue}
              onSelect={handleSelectMaterial}
              productTypes={[ProductType.Material]}
              size="lg"
              clearable={false}
              placeholder="Vyhledejte materiál…"
            />
          ) : (
            <div>
              <p className="font-semibold text-neutral-slate dark:text-graphite-text">
                {state.material?.name}
              </p>
              <p data-testid="receive-material-code" className="font-mono text-sm text-neutral-gray dark:text-graphite-muted">
                {state.material?.code}
              </p>
            </div>
          )}
        </StepSection>

        {/* Step 2 — supplier lot */}
        <StepSection
          step={2}
          title="Šarže dodavatele"
          state={lotStepState}
          onEdit={lotStepState === 'done' ? () => dispatch({ type: 'EDIT_LOT' }) : undefined}
          testId="receive-step-lot"
        >
          {lotStepState === 'done' ? (
            <p className="font-mono font-semibold text-neutral-slate dark:text-graphite-text">{state.lotCode}</p>
          ) : (
            <StepInput
              key={`lot-${lotStepState === 'active'}`}
              label={LOT_LABEL}
              onScan={handleConfirmLot}
              disabled={lotStepState !== 'active'}
              active={lotStepState === 'active'}
              defaultValue={lastLot?.lotCode ?? ''}
              placeholder="Naskenujte nebo zadejte šarži"
            />
          )}
        </StepSection>

        {/* Step 3 — container scan */}
        <StepSection step={3} title="Štítky kontejnerů" state={scanStepState} testId="receive-step-scan">
          <div className="space-y-3">
            <StepInput
              key={`scan-${scanStepState === 'active'}`}
              label={SCAN_LABEL}
              onScan={handleScan}
              disabled={scanStepState !== 'active'}
              active={scanStepState === 'active'}
              loading={create.isPending}
              placeholder="Naskenujte kód kontejneru"
            />
            {(scanStepState === 'active' || state.boxes.length > 0) && (
              <ScannedBoxList boxes={state.boxes} savedCount={savedCount(state)} />
            )}
          </div>
        </StepSection>
      </div>
    </ScanShell>
  );
};

export default ReceiveScreen;
