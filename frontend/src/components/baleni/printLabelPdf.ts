import { getAuthenticatedApiClient } from '../../api/client';

interface LabelIdentifier {
  shipmentGuid: string;
  packageName: string;
}

export const printLabelPdf = (orderCode: string, label: LabelIdentifier): void => {
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
