import { ShipmentLabelDto } from '../../api/generated/api-client';

const openInNewTab = (url: string): void => {
  window.open(url, '_blank', 'noopener,noreferrer');
};

const silentPrintViaBlob = async (url: string): Promise<boolean> => {
  let response: Response;
  try {
    response = await fetch(url);
  } catch {
    return false;
  }
  if (!response.ok) return false;

  const blob = await response.blob();
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
  return true;
};

export const printLabelPdf = (_orderCode: string, label: ShipmentLabelDto): void => {
  const labelUrl = label.labelUrl;
  if (!labelUrl) return;

  void silentPrintViaBlob(labelUrl).then((printed) => {
    if (!printed) openInNewTab(labelUrl);
  });
};
