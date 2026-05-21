import { ShipmentLabelDto } from '../../api/generated/api-client';
import { getAuthenticatedApiClient } from '../../api/client';

export const printLabelPdf = (orderCode: string, label: ShipmentLabelDto): void => {
  if (!label.shipmentGuid || !label.packageName) {
    throw new Error('Invalid label: missing shipmentGuid or packageName');
  }

  const apiClient = getAuthenticatedApiClient(false);
  const baseUrl = (apiClient as any).baseUrl as string;
  const url =
    `${baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/label/pdf` +
    `?shipmentGuid=${encodeURIComponent(label.shipmentGuid)}` +
    `&packageName=${encodeURIComponent(label.packageName)}`;

  void fetch(url)
    .then(res => {
      if (!res.ok) throw new Error(`Label PDF unavailable: ${res.status}`);
      return res.blob();
    })
    .then(blob => {
      const blobUrl = URL.createObjectURL(blob);
      const iframe = document.createElement('iframe');
      iframe.style.display = 'none';
      iframe.src = blobUrl;
      iframe.onload = () => {
        iframe.contentWindow?.print();
        document.body.removeChild(iframe);
        URL.revokeObjectURL(blobUrl);
      };
      document.body.appendChild(iframe);
    })
    .catch(() => {
      // silently ignore — the print simply won't fire if the PDF is unavailable
    });
};
