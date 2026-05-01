import React from "react";
import { AudienceType, LeafletLength } from "../../api/generated/api-client";

interface LeafletFormProps {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
  isLoading: boolean;
  onTopicChange: (topic: string) => void;
  onAudienceChange: (audience: AudienceType) => void;
  onLengthChange: (length: LeafletLength) => void;
  onSubmit: () => void;
}

const MAX_TOPIC_LENGTH = 200;

export default function LeafletForm({
  topic,
  audience,
  length,
  isLoading,
  onTopicChange,
  onAudienceChange,
  onLengthChange,
  onSubmit,
}: LeafletFormProps) {
  const isSubmitDisabled = isLoading || !topic.trim();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!isSubmitDisabled) {
      onSubmit();
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {/* Topic */}
      <div>
        <label
          htmlFor="leaflet-topic"
          className="block text-sm font-medium text-gray-700"
        >
          Téma
        </label>
        <input
          type="text"
          id="leaflet-topic"
          maxLength={MAX_TOPIC_LENGTH}
          required
          value={topic}
          onChange={(e) => onTopicChange(e.target.value)}
          placeholder="např. Bisabolol pro citlivou pleť"
          className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
        />
        <span className="text-xs text-gray-500 text-right block">
          {topic.length}/{MAX_TOPIC_LENGTH}
        </span>
      </div>

      {/* Audience */}
      <fieldset className="space-y-2">
        <legend className="block text-sm font-medium text-gray-700">
          Cílová skupina
        </legend>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="audience"
            value={AudienceType.EndConsumer}
            checked={audience === AudienceType.EndConsumer}
            onChange={() => onAudienceChange(AudienceType.EndConsumer)}
          />
          Koncový zákazník
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="audience"
            value={AudienceType.B2B}
            checked={audience === AudienceType.B2B}
            onChange={() => onAudienceChange(AudienceType.B2B)}
          />
          B2B
        </label>
      </fieldset>

      {/* Length */}
      <fieldset className="space-y-2">
        <legend className="block text-sm font-medium text-gray-700">
          Délka
        </legend>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="length"
            value={LeafletLength.Short}
            checked={length === LeafletLength.Short}
            onChange={() => onLengthChange(LeafletLength.Short)}
          />
          Krátký (~200 slov)
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="length"
            value={LeafletLength.Medium}
            checked={length === LeafletLength.Medium}
            onChange={() => onLengthChange(LeafletLength.Medium)}
          />
          Střední (~400 slov)
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="length"
            value={LeafletLength.Long}
            checked={length === LeafletLength.Long}
            onChange={() => onLengthChange(LeafletLength.Long)}
          />
          Dlouhý (~700 slov)
        </label>
      </fieldset>

      {/* Submit */}
      <button
        type="submit"
        disabled={isSubmitDisabled}
        aria-busy={isLoading}
        className="w-full px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed hover:bg-blue-700"
      >
        Vygenerovat leták
      </button>
    </form>
  );
}
