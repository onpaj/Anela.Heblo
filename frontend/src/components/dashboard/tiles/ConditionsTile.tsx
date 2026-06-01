import React from 'react';
import { Thermometer, Droplets } from 'lucide-react';

type Source = 'Live' | 'Partial' | 'Unavailable';

interface ConditionsTileProps {
  data: {
    status?: string;
    data?: {
      innerTemperature: number | null;
      innerHumidity: number | null;
      outerTemperature: number | null;
      outerHumidity: number | null;
      recordedAt: string;
      source: Source;
    };
  };
}

const formatTemp = (v: number | null) =>
  v == null ? '—' : `${v.toFixed(1)} °C`;

const formatHumidity = (v: number | null) =>
  v == null ? '—' : `${v.toFixed(0)} %`;

const SOURCE_LABEL: Record<Source, { label: string; cls: string }> = {
  Live:        { label: 'Živé',       cls: 'bg-green-100 text-green-700' },
  Partial:     { label: 'Částečné',   cls: 'bg-amber-100 text-amber-700' },
  Unavailable: { label: 'Nedostupné', cls: 'bg-red-100 text-red-700' },
};

export const ConditionsTile: React.FC<ConditionsTileProps> = ({ data }) => {
  const d = data.data;
  if (!d) return null;

  const src = SOURCE_LABEL[d.source];
  const recorded = new Date(d.recordedAt);

  return (
    <div className="h-full flex flex-col gap-3 pt-2">
      <div className="flex items-center justify-between text-sm">
        <span className={`px-2 py-0.5 rounded font-medium ${src.cls}`}>
          {src.label}
        </span>
        <span className="text-gray-500">
          {recorded.toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
        </span>
      </div>
      <div className="grid grid-cols-2 gap-3 flex-1">
        <Reading
          icon={<Thermometer className="h-5 w-5 text-orange-500" />}
          label="Vnitřní teplota"
          value={formatTemp(d.innerTemperature)}
        />
        <Reading
          icon={<Thermometer className="h-5 w-5 text-blue-500" />}
          label="Venkovní teplota"
          value={formatTemp(d.outerTemperature)}
        />
        <Reading
          icon={<Droplets className="h-5 w-5 text-orange-400" />}
          label="Vnitřní vlhkost"
          value={formatHumidity(d.innerHumidity)}
        />
        <Reading
          icon={<Droplets className="h-5 w-5 text-blue-400" />}
          label="Venkovní vlhkost"
          value={formatHumidity(d.outerHumidity)}
        />
      </div>
    </div>
  );
};

const Reading: React.FC<{
  icon: React.ReactNode;
  label: string;
  value: string;
}> = ({ icon, label, value }) => (
  <div className="flex items-center gap-2">
    {icon}
    <div className="flex flex-col">
      <span className="text-xs text-gray-500">{label}</span>
      <span className="text-lg font-semibold text-gray-800">{value}</span>
    </div>
  </div>
);
