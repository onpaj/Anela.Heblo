import { Thermometer } from 'lucide-react';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';
import CarrierCoolingMatrix from '../../components/customer/cooling/CarrierCoolingMatrix';
import WeatherForecastReport from '../../components/customer/cooling/WeatherForecastReport';
import {
  useCarrierCoolingMatrix,
  useSetCarrierCooling,
} from '../../api/hooks/useCarrierCooling';

function CoolingPage() {
  const { data, isLoading, error } = useCarrierCoolingMatrix();
  const { mutate: setCooling, isPending, variables: savingRow } = useSetCarrierCooling();

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 px-4 py-3">
        <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
          <Thermometer className="h-6 w-6 text-indigo-600" />
          Chlazení
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Nastavení úrovně chlazení pro každého dopravce a typ doručení.
        </p>
      </div>

      <div className="flex-1 overflow-y-auto">
        <WeatherForecastReport />

        {isLoading && (
          <div className="flex items-center justify-center h-32">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
          </div>
        )}

        {error && (
          <div className="mx-4 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 text-sm">
            Nepodařilo se načíst nastavení chlazení. Zkuste obnovit stránku.
          </div>
        )}

        {data && (
          <CarrierCoolingMatrix
            groups={data.groups}
            onSetCooling={setCooling}
            isSaving={isPending}
            savingRow={savingRow ?? null}
          />
        )}
      </div>
    </div>
  );
}

export default CoolingPage;
