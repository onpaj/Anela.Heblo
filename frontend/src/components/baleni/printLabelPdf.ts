import { ShipmentLabelDto } from '../../api/generated/api-client';
import { getAuthenticatedApiClient } from '../../api/client';

export const printLabelPdf = (orderCode: string, label: ShipmentLabelDto): void => {
  if (!label.shipmentGuid || !label.packageName) {
    throw new Error('Invalid label: missing shipmentGuid or packageName');
  }

  const apiClient = getAuthenticatedApiClient(false);
  const baseUrl = (apiClient as any).baseUrl as string;
  const url =
    `${baseUrl}/api/shipment-labels/pdf` +
    `?orderCode=${encodeURIComponent(orderCode)}` +
    `&shipmentGuid=${encodeURIComponent(label.shipmentGuid)}` +
    `&packageName=${encodeURIComponent(label.packageName)}`;

  const iframe = document.createElement('iframe');
  iframe.style.display = 'none';
  iframe.src = url;
  iframe.onload = () => {
    iframe.contentWindow?.print();
    document.body.removeChild(iframe);
  };
  document.body.appendChild(iframe);
};
