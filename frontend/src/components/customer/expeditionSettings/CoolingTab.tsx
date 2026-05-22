import CarrierCoolingMatrix from '../cooling/CarrierCoolingMatrix';
import WeatherForecastReport from '../cooling/WeatherForecastReport';
import {
  useCarrierCoolingMatrix,
  useSetCarrierCooling,
} from '../../../api/hooks/useCarrierCooling';

function CoolingTab() {
  const { data, isLoading, error } = useCarrierCoolingMatrix();
  const { mutate: setCooling, isPending, variables: savingRow } = useSetCarrierCooling();

  return (
    <>
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
    </>
  );
}

export default CoolingTab;
