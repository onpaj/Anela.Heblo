import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Save, X } from 'lucide-react';
import {
  ClassificationRule,
  CreateClassificationRuleRequest,
  UpdateClassificationRuleRequest,
  useClassificationRuleTypes,
  useAccountingTemplates,
} from '../../../api/hooks/useInvoiceClassification';

interface RuleFormProps {
  rule?: ClassificationRule | null;
  onSubmit: (data: CreateClassificationRuleRequest | UpdateClassificationRuleRequest) => Promise<void>;
  onCancel: () => void;
  isLoading: boolean;
  prefillCompanyName?: string;
}

const RuleForm: React.FC<RuleFormProps> = ({ rule, onSubmit, onCancel, isLoading, prefillCompanyName }) => {
  const { t } = useTranslation();
  const { data: ruleTypes = [], isLoading: ruleTypesLoading } = useClassificationRuleTypes();
  const { data: accountingTemplates = [], isLoading: accountingTemplatesLoading } = useAccountingTemplates();
  const [formData, setFormData] = useState({
    name: '',
    ruleTypeIdentifier: '',
    pattern: '',
    accountingTemplateCode: '',
    isActive: true,
  });

  useEffect(() => {
    if (rule) {
      setFormData({
        name: rule.name || '',
        ruleTypeIdentifier: rule.ruleTypeIdentifier || '',
        pattern: rule.pattern || '',
        accountingTemplateCode: rule.accountingTemplateCode || '',
        isActive: rule.isActive || false,
      });
    } else if (ruleTypes.length > 0) {
      // Find COMPANY_NAME rule type if it exists, otherwise use first one
      const companyNameRule = ruleTypes.find(rt => rt.identifier === 'COMPANY_NAME');
      const selectedRuleType = companyNameRule || ruleTypes[0];
      
      setFormData({
        name: prefillCompanyName ? `Rule for ${prefillCompanyName}` : '',
        ruleTypeIdentifier: selectedRuleType?.identifier || '',
        pattern: prefillCompanyName ? prefillCompanyName : '',
        accountingTemplateCode: '',
        isActive: true,
      });
    }
  }, [rule, ruleTypes, prefillCompanyName]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (rule) {
      onSubmit({
        id: rule.id,
        ...formData,
      } as UpdateClassificationRuleRequest);
    } else {
      onSubmit(formData as CreateClassificationRuleRequest);
    }
  };

  const handleInputChange = (field: string, value: any) => {
    setFormData(prev => ({
      ...prev,
      [field]: value,
    }));
  };

  const getCurrentRuleType = () => {
    return ruleTypes.find(rt => rt.identifier === formData.ruleTypeIdentifier);
  };

  const getPatternPlaceholder = () => {
    switch (formData.ruleTypeIdentifier) {
      case 'ICO':
        return t('invoiceClassification.placeholders.ico', 'e.g., 12345678');
      case 'COMPANY_NAME':
        return t('invoiceClassification.placeholders.companyName', 'e.g., Company.*s.r.o.');
      case 'DESCRIPTION':
        return t('invoiceClassification.placeholders.description', 'e.g., software.*development');
      case 'ITEM_DESCRIPTION':
        return t('invoiceClassification.placeholders.itemDescription', 'e.g., consulting.*services');
      case 'AMOUNT':
        return t('invoiceClassification.placeholders.amount', 'e.g., >=1000 or <500');
      default:
        return '';
    }
  };

  const getPatternHelp = () => {
    const currentRuleType = getCurrentRuleType();
    if (currentRuleType) {
      return currentRuleType.description;
    }
    
    switch (formData.ruleTypeIdentifier) {
      case 'ICO':
        return t('invoiceClassification.help.ico', 'Enter exact ICO number for matching');
      case 'COMPANY_NAME':
        return t('invoiceClassification.help.companyName', 'Use regex patterns. .* matches any text');
      case 'DESCRIPTION':
        return t('invoiceClassification.help.description', 'Use regex patterns to match invoice description');
      case 'ITEM_DESCRIPTION':
        return t('invoiceClassification.help.itemDescription', 'Matches against any invoice line item description');
      case 'AMOUNT':
        return t('invoiceClassification.help.amount', 'Use operators: >=, <=, >, <, = followed by amount');
      default:
        return '';
    }
  };

  return (
    <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
      <div className="mb-4">
        <h2 className="text-xl font-semibold text-gray-900">
          {rule 
            ? t('invoiceClassification.editRule', 'Edit Classification Rule')
            : t('invoiceClassification.createRule', 'Create Classification Rule')
          }
        </h2>
      </div>

      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <div>
            <label htmlFor="name" className="block text-sm font-medium text-gray-700 mb-1">
              {t('invoiceClassification.form.name', 'Rule Name')} <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              id="name"
              value={formData.name}
              onChange={(e) => handleInputChange('name', e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
              placeholder={t('invoiceClassification.form.namePlaceholder', 'Enter a descriptive name for this rule')}
              required
            />
          </div>

          <div>
            <label htmlFor="type" className="block text-sm font-medium text-gray-700 mb-1">
              {t('invoiceClassification.form.type', 'Rule Type')} <span className="text-red-500">*</span>
            </label>
            <select
              id="type"
              value={formData.ruleTypeIdentifier}
              onChange={(e) => handleInputChange('ruleTypeIdentifier', e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
              required
              disabled={ruleTypesLoading}
            >
              {ruleTypesLoading ? (
                <option value="">{t('common.loading', 'Loading...')}</option>
              ) : (
                ruleTypes.map(ruleType => (
                  <option key={ruleType.identifier} value={ruleType.identifier}>
                    {ruleType.displayName}
                  </option>
                ))
              )}
            </select>
          </div>
        </div>

        <div>
          <label htmlFor="pattern" className="block text-sm font-medium text-gray-700 mb-1">
            {t('invoiceClassification.form.pattern', 'Pattern')} <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            id="pattern"
            value={formData.pattern}
            onChange={(e) => handleInputChange('pattern', e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
            placeholder={getPatternPlaceholder()}
            required
          />
          <p className="mt-1 text-sm text-gray-500">
            {getPatternHelp()}
          </p>
        </div>

        <div>
          <label htmlFor="accountingTemplateCode" className="block text-sm font-medium text-gray-700 mb-1">
            {t('invoiceClassification.form.prescription', 'Accounting Prescription')} <span className="text-red-500">*</span>
          </label>
          <select
            id="accountingTemplateCode"
            value={formData.accountingTemplateCode}
            onChange={(e) => handleInputChange('accountingTemplateCode', e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
            required
            disabled={accountingTemplatesLoading}
          >
            <option value="">
              {accountingTemplatesLoading 
                ? t('common.loading', 'Loading...') 
                : t('invoiceClassification.form.selectPrescription', 'Select accounting prescription...')
              }
            </option>
            {accountingTemplates.map(template => (
              <option key={template.code} value={template.code}>
                {template.code} - {template.name}
              </option>
            ))}
          </select>
          <p className="mt-1 text-sm text-gray-500">
            {formData.accountingTemplateCode && !accountingTemplatesLoading && accountingTemplates.length > 0 && (
              (() => {
                const selectedTemplate = accountingTemplates.find(t => t.code === formData.accountingTemplateCode);
                return selectedTemplate ? `${selectedTemplate.description} (${selectedTemplate.accountCode})` : '';
              })()
            )}
            {!formData.accountingTemplateCode && !accountingTemplatesLoading && (
              t('invoiceClassification.form.prescriptionHelp', 'Select the accounting code that should be applied to matching invoices')
            )}
          </p>
        </div>

        <div className="flex items-center">
          <input
            type="checkbox"
            id="isActive"
            checked={formData.isActive}
            onChange={(e) => handleInputChange('isActive', e.target.checked)}
            className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
          />
          <label htmlFor="isActive" className="ml-2 block text-sm text-gray-900">
            {t('invoiceClassification.form.active', 'Rule is active')}
          </label>
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t border-gray-200">
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            <X className="w-4 h-4 mr-2 inline" />
            {t('common.cancel', 'Cancel')}
          </button>
          <button
            type="submit"
            disabled={isLoading}
            className="inline-flex items-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
          >
            <Save className="w-4 h-4 mr-2" />
            {isLoading 
              ? t('common.saving', 'Saving...') 
              : (rule ? t('common.update', 'Update') : t('common.create', 'Create'))
            }
          </button>
        </div>
      </form>
    </div>
  );
};

export default RuleForm;