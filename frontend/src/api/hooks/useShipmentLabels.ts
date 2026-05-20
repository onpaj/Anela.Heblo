import { useQuery } from '@tanstack/react-query';
import { ErrorCodes, GetShipmentLabelsRequest, ShipmentLabelDto } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

const MESSAGES: Record<string, string> = {
  [ErrorCodes.ShipmentLabelsNoShipmentFound]:
    'Štítek nelze vytisknout — zásilka zatím nebyla vytvořena',
  [ErrorCodes.ShipmentLabelsNotGenerated]: 'Štítky zatím nebyly vygenerovány',
};

const GENERIC_ERROR = 'Štítek se nepodařilo načíst';

const fetchShipmentLabels = async (orderCode: string): Promise<ShipmentLabelDto[]> => {
  const apiClient = getAuthenticatedApiClient(false);
  const response = await apiClient.shipmentLabels_GetLabels(new GetShipmentLabelsRequest({ orderCode }));
  if (!response.success) {
    const message =
      (response.errorCode && MESSAGES[response.errorCode]) ?? GENERIC_ERROR;
    throw new Error(message);
  }
  return response.labels ?? [];
};

export const useShipmentLabels = (orderCode: string | null, enabled: boolean) =>
  useQuery({
    queryKey: [...QUERY_KEYS.shipmentLabels, orderCode],
    queryFn: () => fetchShipmentLabels(orderCode as string),
    enabled: enabled && !!orderCode,
    retry: false,
    gcTime: 0,
  });
