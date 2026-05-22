import { useState, useEffect } from 'react';
import { useGiftSetting, useSetGiftSetting } from '../../../api/hooks/useGiftSetting';

function GiftsTab() {
  const { data, isLoading, error } = useGiftSetting();
  const { mutate: saveSetting, isPending } = useSetGiftSetting();

  const [isEnabled, setIsEnabled] = useState(false);
  const [thresholdCzk, setThresholdCzk] = useState(0);
  const [text, setText] = useState('');

  useEffect(() => {
    if (data) {
      setIsEnabled(data.isEnabled);
      setThresholdCzk(data.thresholdCzk);
      setText(data.text);
    }
  }, [data]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32" role="status">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="mx-4 mt-4 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 text-sm">
        Nepodařilo se načíst nastavení dárků. Zkuste obnovit stránku.
      </div>
    );
  }

  const handleSave = () => {
    saveSetting({ isEnabled, thresholdCzk, text });
  };

  return (
    <div className="px-4 py-4 max-w-lg space-y-6">
      <p className="text-sm text-gray-500">
        Když je součet objednávky v CZK dosáhne prahu, vytiskne se badge na expediční seznam.
      </p>

      {/* Enable toggle */}
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-gray-700">Aktivní</span>
        <button
          type="button"
          role="switch"
          aria-checked={isEnabled}
          onClick={() => setIsEnabled((v) => !v)}
          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
            isEnabled ? 'bg-indigo-600' : 'bg-gray-200'
          }`}
        >
          <span
            className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
              isEnabled ? 'translate-x-6' : 'translate-x-1'
            }`}
          />
        </button>
      </div>

      {/* Threshold */}
      <div>
        <label htmlFor="threshold" className="block text-sm font-medium text-gray-700 mb-1">
          Práh (CZK)
        </label>
        <input
          id="threshold"
          type="number"
          min={1}
          max={999999}
          value={thresholdCzk}
          onChange={(e) => setThresholdCzk(Number(e.target.value))}
          disabled={!isEnabled}
          className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-400"
        />
      </div>

      {/* Text */}
      <div>
        <label htmlFor="gift-text" className="block text-sm font-medium text-gray-700 mb-1">
          Text badge (max 30 znaků)
        </label>
        <input
          id="gift-text"
          type="text"
          maxLength={30}
          value={text}
          onChange={(e) => setText(e.target.value)}
          disabled={!isEnabled}
          placeholder="DÁREK ZDARMA"
          className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-400"
        />
        <p className="mt-1 text-xs text-gray-400">{text.length} / 30</p>
      </div>

      {/* Save button */}
      <button
        type="button"
        onClick={handleSave}
        disabled={isPending}
        className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
      >
        {isPending ? 'Ukládám…' : 'Uložit'}
      </button>
    </div>
  );
}

export default GiftsTab;
