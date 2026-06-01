import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import CarrierCoolingMatrix from '../CarrierCoolingMatrix';
import { CarrierGroupDto } from '../../../../api/hooks/useCarrierCooling';

const baseGroups: CarrierGroupDto[] = [
  {
    carrier: 'Zasilkovna',
    rows: [{ deliveryHandling: 'Box', cooling: 'L1', coolingText: null }],
  },
];

describe('CarrierCoolingMatrix', () => {
  it('shows the default badge text as placeholder when no custom text is set', () => {
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={jest.fn()}
        isSaving={false}
        savingRow={null}
      />
    );

    expect(screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA')).toBeInTheDocument();
  });

  it('fires onSetCooling with the custom text on blur', () => {
    const onSetCooling = jest.fn();
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    const input = screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA');
    fireEvent.change(input, { target: { value: 'MRAZ' } });
    fireEvent.blur(input);

    expect(onSetCooling).toHaveBeenCalledWith({
      carrier: 'Zasilkovna',
      deliveryHandling: 'Box',
      cooling: 'L1',
      coolingText: 'MRAZ',
    });
  });

  it('includes the current custom text when a cooling level radio changes', () => {
    const onSetCooling = jest.fn();
    const groupsWithText: CarrierGroupDto[] = [
      { carrier: 'Zasilkovna', rows: [{ deliveryHandling: 'Box', cooling: 'L1', coolingText: 'MRAZ' }] },
    ];
    render(
      <CarrierCoolingMatrix
        groups={groupsWithText}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    fireEvent.click(screen.getByRole('radio', { name: /L2/ }));

    expect(onSetCooling).toHaveBeenCalledWith({
      carrier: 'Zasilkovna',
      deliveryHandling: 'Box',
      cooling: 'L2',
      coolingText: 'MRAZ',
    });
  });

  it('does not fire onSetCooling on blur when the text is unchanged', () => {
    const onSetCooling = jest.fn();
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    const input = screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA');
    fireEvent.blur(input);

    expect(onSetCooling).not.toHaveBeenCalled();
  });
});
