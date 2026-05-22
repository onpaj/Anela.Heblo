import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import PackingShipmentDoneView from '../PackingShipmentDoneView';
import type { PackingOrder, ScanShipment } from '../../../api/hooks/useScanPackingOrder';

function makeOrder(overrides: Partial<PackingOrder> = {}): PackingOrder {
  return {
    code: 'ORD001',
    customerName: 'Jana Nováková',
    shippingMethodName: 'Zásilkovna',
    shippingAddress: {
      street: 'Hlavní 12',
      city: 'Praha',
      zip: '11000',
    },
    cooling: 'None',
    isCooled: false,
    customerNote: null,
    eshopNote: null,
    eligibility: { isEligible: true, warningTitle: null, warningBody: null },
    items: [],
    ...overrides,
  };
}

function makeShipment(overrides: Partial<ScanShipment> = {}): ScanShipment {
  return {
    shipmentGuid: 'guid-1',
    packages: [
      { name: 'PKG-1', trackingNumber: 'TR-1', labelUrl: null, labelZpl: null },
      { name: 'PKG-2', trackingNumber: 'TR-2', labelUrl: null, labelZpl: null },
    ],
    alreadyExisted: false,
    ...overrides,
  };
}

describe('PackingShipmentDoneView', () => {
  it('renders the "Zakázka byla vyexpedována" header', () => {
    render(
      <PackingShipmentDoneView order={makeOrder()} shipment={makeShipment()} onReprint={() => {}} />
    );
    expect(screen.getByTestId('packing-shipment-done')).toBeInTheDocument();
    expect(screen.getByText('Zakázka byla vyexpedována')).toBeInTheDocument();
  });

  it('renders order code, customer name, shipping method, and tracking numbers', () => {
    render(
      <PackingShipmentDoneView order={makeOrder()} shipment={makeShipment()} onReprint={() => {}} />
    );
    expect(screen.getByText('ORD001')).toBeInTheDocument();
    expect(screen.getByText('Jana Nováková')).toBeInTheDocument();
    expect(screen.getByText('Zásilkovna')).toBeInTheDocument();
    expect(screen.getByText('TR-1, TR-2')).toBeInTheDocument();
  });

  it('renders the address block when shippingAddress has street + city + zip', () => {
    render(
      <PackingShipmentDoneView order={makeOrder()} shipment={makeShipment()} onReprint={() => {}} />
    );
    const block = screen.getByTestId('packing-shipment-done-address');
    expect(block).toHaveTextContent('Hlavní 12');
    expect(block).toHaveTextContent('11000 Praha');
  });

  it('omits the address row when shippingAddress is null', () => {
    render(
      <PackingShipmentDoneView
        order={makeOrder({ shippingAddress: null })}
        shipment={makeShipment()}
        onReprint={() => {}}
      />
    );
    expect(screen.queryByTestId('packing-shipment-done-address')).not.toBeInTheDocument();
    expect(screen.queryByText('Adresa')).not.toBeInTheDocument();
  });

  it('omits the address row when every address field is null', () => {
    render(
      <PackingShipmentDoneView
        order={makeOrder({ shippingAddress: { street: null, city: null, zip: null } })}
        shipment={makeShipment()}
        onReprint={() => {}}
      />
    );
    expect(screen.queryByTestId('packing-shipment-done-address')).not.toBeInTheDocument();
  });

  it('calls onReprint when the reprint button is clicked', () => {
    const onReprint = jest.fn();
    render(
      <PackingShipmentDoneView
        order={makeOrder()}
        shipment={makeShipment()}
        onReprint={onReprint}
      />
    );
    fireEvent.click(screen.getByTestId('reprint-label-button'));
    expect(onReprint).toHaveBeenCalledTimes(1);
  });

  it('falls back to package name when trackingNumber is null', () => {
    render(
      <PackingShipmentDoneView
        order={makeOrder()}
        shipment={makeShipment({
          packages: [
            { name: 'PKG-A', trackingNumber: null, labelUrl: null, labelZpl: null },
            { name: 'PKG-B', trackingNumber: 'TR-B', labelUrl: null, labelZpl: null },
          ],
        })}
        onReprint={() => {}}
      />
    );
    expect(screen.getByText('PKG-A, TR-B')).toBeInTheDocument();
  });
});
