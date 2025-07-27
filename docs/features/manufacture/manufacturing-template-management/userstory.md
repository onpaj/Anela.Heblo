# User Story: Manufacturing Template Management

## Feature Description
The Manufacturing Template Management feature handles Bill of Materials (BoM) with dynamic recipe scaling capabilities. It manages manufacturing templates with ingredient tracking, proportional scaling calculations, and integration with FlexiBee ERP system for template synchronization. The system supports hierarchical BoM structures and provides flexible scaling algorithms for production planning.

## Business Requirements

### Primary Use Cases
1. **Template Management**: Create, update, and manage manufacturing templates
2. **Recipe Scaling**: Dynamically scale recipes based on production requirements
3. **Ingredient Tracking**: Manage ingredient requirements and costs
4. **BoM Hierarchy**: Handle complex multi-level Bill of Materials
5. **ERP Integration**: Synchronize templates with FlexiBee ERP system

### User Stories
- As a production planner, I want to manage manufacturing templates so I can define production recipes
- As a production manager, I want to scale recipes dynamically so I can adjust for different batch sizes
- As a cost analyst, I want to track ingredient costs so I can calculate production costs
- As an ERP administrator, I want to synchronize BoM data so I can maintain data consistency

## Technical Requirements

### Domain Models

#### ManufactureTemplate
```csharp
public class ManufactureTemplate : AuditedAggregateRoot<string>
{
    public string TemplateId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public string Unit { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Version { get; set; } = "1.0";
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiryDate { get; set; }
    
    // Navigation Properties
    public virtual ICollection<TemplateIngredient> Ingredients { get; set; } = new List<TemplateIngredient>();
    public virtual ICollection<TemplateScalingRule> ScalingRules { get; set; } = new List<TemplateScalingRule>();
    public virtual ICollection<TemplateVersion> Versions { get; set; } = new List<TemplateVersion>();
    
    // Computed Properties
    public decimal TotalIngredientCost => Ingredients.Sum(i => i.Amount * i.CostPerUnit);
    public decimal CostPerUnit => BaseAmount > 0 ? TotalIngredientCost / BaseAmount : 0;
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.UtcNow;
    public int IngredientCount => Ingredients.Count;
    
    // Business Methods
    public ScaledTemplate ScaleToAmount(decimal targetAmount)
    {
        if (BaseAmount <= 0)
            throw new BusinessException("Cannot scale template with zero base amount");
            
        var scalingFactor = targetAmount / BaseAmount;
        var scaledIngredients = new List<ScaledIngredient>();
        
        foreach (var ingredient in Ingredients)
        {
            var scaledAmount = ingredient.Amount * scalingFactor;
            var applicableRule = ScalingRules.FirstOrDefault(r => r.IngredientCode == ingredient.IngredientCode);
            
            if (applicableRule != null)
            {
                scaledAmount = ApplyScalingRule(scaledAmount, applicableRule);
            }
            
            scaledIngredients.Add(new ScaledIngredient
            {
                IngredientCode = ingredient.IngredientCode,
                IngredientName = ingredient.IngredientName,
                OriginalAmount = ingredient.Amount,
                ScaledAmount = scaledAmount,
                Unit = ingredient.Unit,
                CostPerUnit = ingredient.CostPerUnit,
                TotalCost = scaledAmount * ingredient.CostPerUnit,
                ScalingFactor = scalingFactor
            });
        }
        
        return new ScaledTemplate
        {
            TemplateId = TemplateId,
            ProductCode = ProductCode,
            BaseAmount = BaseAmount,
            TargetAmount = targetAmount,
            ScalingFactor = scalingFactor,
            ScaledIngredients = scaledIngredients,
            TotalCost = scaledIngredients.Sum(i => i.TotalCost),
            ScalingDate = DateTime.UtcNow
        };
    }
    
    public ScaledTemplate ScaleByIngredient(string ingredientCode, decimal targetIngredientAmount)
    {
        var ingredient = Ingredients.FirstOrDefault(i => i.IngredientCode == ingredientCode);
        if (ingredient == null)
            throw new BusinessException($"Ingredient {ingredientCode} not found in template");
            
        if (ingredient.Amount <= 0)
            throw new BusinessException("Cannot scale by ingredient with zero amount");
            
        var ingredientScalingFactor = targetIngredientAmount / ingredient.Amount;
        var targetAmount = BaseAmount * ingredientScalingFactor;
        
        return ScaleToAmount(targetAmount);
    }
    
    public void AddIngredient(string ingredientCode, string ingredientName, decimal amount, string unit, decimal costPerUnit)
    {
        if (Ingredients.Any(i => i.IngredientCode == ingredientCode))
            throw new BusinessException($"Ingredient {ingredientCode} already exists in template");
            
        var ingredient = new TemplateIngredient
        {
            TemplateId = TemplateId,
            IngredientCode = ingredientCode,
            IngredientName = ingredientName,
            Amount = amount,
            Unit = unit,
            CostPerUnit = costPerUnit,
            IsOptional = false,
            SortOrder = Ingredients.Count + 1
        };
        
        Ingredients.Add(ingredient);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateIngredient(string ingredientCode, decimal amount, decimal costPerUnit)
    {
        var ingredient = Ingredients.FirstOrDefault(i => i.IngredientCode == ingredientCode);
        if (ingredient == null)
            throw new BusinessException($"Ingredient {ingredientCode} not found");
            
        ingredient.Amount = amount;
        ingredient.CostPerUnit = costPerUnit;
        ingredient.LastModificationTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void RemoveIngredient(string ingredientCode)
    {
        var ingredient = Ingredients.FirstOrDefault(i => i.IngredientCode == ingredientCode);
        if (ingredient != null)
        {
            Ingredients.Remove(ingredient);
            LastModificationTime = DateTime.UtcNow;
        }
    }
    
    public void AddScalingRule(string ingredientCode, ScalingRuleType ruleType, decimal parameter1, decimal? parameter2 = null)
    {
        var existingRule = ScalingRules.FirstOrDefault(r => r.IngredientCode == ingredientCode);
        if (existingRule != null)
        {
            ScalingRules.Remove(existingRule);
        }
        
        var rule = new TemplateScalingRule
        {
            TemplateId = TemplateId,
            IngredientCode = ingredientCode,
            RuleType = ruleType,
            Parameter1 = parameter1,
            Parameter2 = parameter2,
            IsActive = true
        };
        
        ScalingRules.Add(rule);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public TemplateValidationResult Validate()
    {
        var result = new TemplateValidationResult();
        
        if (string.IsNullOrEmpty(ProductCode))
            result.AddError("Product code is required");
            
        if (BaseAmount <= 0)
            result.AddError("Base amount must be greater than zero");
            
        if (!Ingredients.Any())
            result.AddError("Template must have at least one ingredient");
            
        if (IsExpired)
            result.AddWarning("Template has expired");
            
        // Validate ingredients
        foreach (var ingredient in Ingredients)
        {
            if (ingredient.Amount <= 0)
                result.AddWarning($"Ingredient {ingredient.IngredientCode} has zero or negative amount");
                
            if (ingredient.CostPerUnit < 0)
                result.AddWarning($"Ingredient {ingredient.IngredientCode} has negative cost");
        }
        
        return result;
    }
    
    private decimal ApplyScalingRule(decimal scaledAmount, TemplateScalingRule rule)
    {
        switch (rule.RuleType)
        {
            case ScalingRuleType.Linear:
                return scaledAmount;
                
            case ScalingRuleType.Step:
                var stepSize = rule.Parameter1;
                return Math.Ceiling(scaledAmount / stepSize) * stepSize;
                
            case ScalingRuleType.Minimum:
                return Math.Max(scaledAmount, rule.Parameter1);
                
            case ScalingRuleType.Maximum:
                return Math.Min(scaledAmount, rule.Parameter1);
                
            case ScalingRuleType.Range:
                var min = rule.Parameter1;
                var max = rule.Parameter2 ?? decimal.MaxValue;
                return Math.Max(min, Math.Min(max, scaledAmount));
                
            case ScalingRuleType.Percentage:
                var percentage = rule.Parameter1 / 100m;
                return scaledAmount * percentage;
                
            default:
                return scaledAmount;
        }
    }
}
```

#### TemplateIngredient
```csharp
public class TemplateIngredient : AuditedEntity<int>
{
    public string TemplateId { get; set; } = "";
    public string IngredientCode { get; set; } = "";
    public string IngredientName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Unit { get; set; } = "";
    public decimal CostPerUnit { get; set; }
    public bool IsOptional { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public string? LotRequirement { get; set; }
    public DateTime? ExpirationDate { get; set; }
    
    // Navigation Properties
    public virtual ManufactureTemplate Template { get; set; } = null!;
    
    // Computed Properties
    public decimal TotalCost => Amount * CostPerUnit;
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow;
    public decimal CostPercentage => Template?.TotalIngredientCost > 0 
        ? (TotalCost / Template.TotalIngredientCost) * 100 : 0;
}
```

#### TemplateScalingRule
```csharp
public class TemplateScalingRule : AuditedEntity<int>
{
    public string TemplateId { get; set; } = "";
    public string IngredientCode { get; set; } = "";
    public ScalingRuleType RuleType { get; set; }
    public decimal Parameter1 { get; set; }
    public decimal? Parameter2 { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    
    // Navigation Properties
    public virtual ManufactureTemplate Template { get; set; } = null!;
}

public enum ScalingRuleType
{
    Linear,      // Direct proportional scaling
    Step,        // Round up to step size
    Minimum,     // Ensure minimum amount
    Maximum,     // Cap at maximum amount
    Range,       // Keep within range
    Percentage   // Scale by percentage
}
```

#### ScaledTemplate (Value Object)
```csharp
public class ScaledTemplate : ValueObject
{
    public string TemplateId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal ScalingFactor { get; set; }
    public List<ScaledIngredient> ScaledIngredients { get; set; } = new();
    public decimal TotalCost { get; set; }
    public DateTime ScalingDate { get; set; }
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return TemplateId;
        yield return ProductCode;
        yield return BaseAmount;
        yield return TargetAmount;
        yield return ScalingFactor;
        yield return TotalCost;
    }
    
    public decimal CostPerUnit => TargetAmount > 0 ? TotalCost / TargetAmount : 0;
    public bool IsScaled => Math.Abs(ScalingFactor - 1.0m) > 0.001m;
    public string ScalingDescription => $"Scaled {ScalingFactor:P2} from base amount {BaseAmount} to {TargetAmount}";
}
```

#### ScaledIngredient (Value Object)
```csharp
public class ScaledIngredient : ValueObject
{
    public string IngredientCode { get; set; } = "";
    public string IngredientName { get; set; } = "";
    public decimal OriginalAmount { get; set; }
    public decimal ScaledAmount { get; set; }
    public string Unit { get; set; } = "";
    public decimal CostPerUnit { get; set; }
    public decimal TotalCost { get; set; }
    public decimal ScalingFactor { get; set; }
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return IngredientCode;
        yield return OriginalAmount;
        yield return ScaledAmount;
        yield return CostPerUnit;
        yield return ScalingFactor;
    }
    
    public decimal AmountDifference => ScaledAmount - OriginalAmount;
    public decimal CostDifference => TotalCost - (OriginalAmount * CostPerUnit);
    public bool IsExactScale => Math.Abs(ScaledAmount - (OriginalAmount * ScalingFactor)) < 0.001m;
}
```

### Application Services

#### IManufactureTemplateAppService
```csharp
public interface IManufactureTemplateAppService : IApplicationService
{
    Task<ManufactureTemplateDto> GetTemplateAsync(string templateId);
    Task<PagedResultDto<ManufactureTemplateDto>> GetTemplatesAsync(GetTemplatesQuery query);
    Task<ManufactureTemplateDto> CreateTemplateAsync(CreateTemplateDto input);
    Task<ManufactureTemplateDto> UpdateTemplateAsync(string templateId, UpdateTemplateDto input);
    Task DeleteTemplateAsync(string templateId);
    
    Task<ScaledTemplateDto> ScaleTemplateAsync(ScaleTemplateRequest request);
    Task<ScaledTemplateDto> GetScaledTemplateAsync(string templateId, decimal targetAmount);
    Task<List<ScaledTemplateDto>> ScaleMultipleTemplatesAsync(BatchScaleRequest request);
    
    Task<ManufactureTemplateDto> AddIngredientAsync(string templateId, AddIngredientDto input);
    Task<ManufactureTemplateDto> UpdateIngredientAsync(string templateId, string ingredientCode, UpdateIngredientDto input);
    Task<ManufactureTemplateDto> RemoveIngredientAsync(string templateId, string ingredientCode);
    
    Task<ManufactureTemplateDto> AddScalingRuleAsync(string templateId, AddScalingRuleDto input);
    Task<ManufactureTemplateDto> RemoveScalingRuleAsync(string templateId, string ingredientCode);
    
    Task<TemplateValidationResultDto> ValidateTemplateAsync(string templateId);
    Task<List<ManufactureTemplateDto>> GetTemplatesByProductAsync(string productCode);
    Task SynchronizeWithERPAsync(List<string> templateIds);
    
    Task<TemplateCostAnalysisDto> GetCostAnalysisAsync(string templateId);
    Task<List<IngredientUsageReportDto>> GetIngredientUsageReportAsync(DateTime fromDate, DateTime toDate);
}
```

#### ManufactureTemplateAppService Implementation
```csharp
[Authorize]
public class ManufactureTemplateAppService : ApplicationService, IManufactureTemplateAppService
{
    private readonly IManufactureTemplateRepository _templateRepository;
    private readonly IFlexiManufactureRepository _flexiRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ManufactureTemplateAppService> _logger;

    public ManufactureTemplateAppService(
        IManufactureTemplateRepository templateRepository,
        IFlexiManufactureRepository flexiRepository,
        ICatalogRepository catalogRepository,
        IMemoryCache cache,
        ILogger<ManufactureTemplateAppService> logger)
    {
        _templateRepository = templateRepository;
        _flexiRepository = flexiRepository;
        _catalogRepository = catalogRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ManufactureTemplateDto> GetTemplateAsync(string templateId)
    {
        var cacheKey = $"template_{templateId}";
        
        if (_cache.TryGetValue(cacheKey, out ManufactureTemplateDto cachedTemplate))
        {
            return cachedTemplate;
        }

        var template = await _templateRepository.GetAsync(templateId);
        var templateDto = ObjectMapper.Map<ManufactureTemplate, ManufactureTemplateDto>(template);
        
        _cache.Set(cacheKey, templateDto, TimeSpan.FromMinutes(30));
        return templateDto;
    }

    public async Task<ScaledTemplateDto> ScaleTemplateAsync(ScaleTemplateRequest request)
    {
        var template = await _templateRepository.GetAsync(request.TemplateId);
        
        ScaledTemplate scaledTemplate;
        
        if (!string.IsNullOrEmpty(request.IngredientCode))
        {
            scaledTemplate = template.ScaleByIngredient(request.IngredientCode, request.TargetAmount);
        }
        else
        {
            scaledTemplate = template.ScaleToAmount(request.TargetAmount);
        }
        
        var result = ObjectMapper.Map<ScaledTemplate, ScaledTemplateDto>(scaledTemplate);
        
        _logger.LogInformation("Scaled template {TemplateId} from {BaseAmount} to {TargetAmount}", 
            request.TemplateId, template.BaseAmount, request.TargetAmount);
        
        return result;
    }

    public async Task<ManufactureTemplateDto> CreateTemplateAsync(CreateTemplateDto input)
    {
        var templateId = input.TemplateId ?? Guid.NewGuid().ToString();
        
        var template = new ManufactureTemplate
        {
            Id = templateId,
            TemplateId = templateId,
            ProductCode = input.ProductCode,
            ProductName = input.ProductName,
            Description = input.Description,
            BaseAmount = input.BaseAmount,
            Unit = input.Unit,
            EffectiveDate = input.EffectiveDate ?? DateTime.UtcNow,
            ExpiryDate = input.ExpiryDate
        };

        // Add ingredients
        foreach (var ingredientDto in input.Ingredients ?? new List<CreateIngredientDto>())
        {
            template.AddIngredient(
                ingredientDto.IngredientCode,
                ingredientDto.IngredientName,
                ingredientDto.Amount,
                ingredientDto.Unit,
                ingredientDto.CostPerUnit);
        }

        await _templateRepository.InsertAsync(template);
        
        _logger.LogInformation("Created manufacturing template {TemplateId} for product {ProductCode}", 
            templateId, input.ProductCode);

        // Clear cache
        _cache.Remove($"templates_product_{input.ProductCode}");
        
        return ObjectMapper.Map<ManufactureTemplate, ManufactureTemplateDto>(template);
    }

    public async Task<ManufactureTemplateDto> AddIngredientAsync(string templateId, AddIngredientDto input)
    {
        var template = await _templateRepository.GetAsync(templateId);
        
        template.AddIngredient(
            input.IngredientCode,
            input.IngredientName,
            input.Amount,
            input.Unit,
            input.CostPerUnit);
        
        await _templateRepository.UpdateAsync(template);
        
        // Clear cache
        _cache.Remove($"template_{templateId}");
        
        return ObjectMapper.Map<ManufactureTemplate, ManufactureTemplateDto>(template);
    }

    public async Task<ManufactureTemplateDto> AddScalingRuleAsync(string templateId, AddScalingRuleDto input)
    {
        var template = await _templateRepository.GetAsync(templateId);
        
        template.AddScalingRule(
            input.IngredientCode,
            input.RuleType,
            input.Parameter1,
            input.Parameter2);
        
        await _templateRepository.UpdateAsync(template);
        
        _logger.LogInformation("Added scaling rule {RuleType} for ingredient {IngredientCode} in template {TemplateId}", 
            input.RuleType, input.IngredientCode, templateId);
        
        // Clear cache
        _cache.Remove($"template_{templateId}");
        
        return ObjectMapper.Map<ManufactureTemplate, ManufactureTemplateDto>(template);
    }

    public async Task<TemplateValidationResultDto> ValidateTemplateAsync(string templateId)
    {
        var template = await _templateRepository.GetAsync(templateId);
        var validationResult = template.Validate();
        
        return ObjectMapper.Map<TemplateValidationResult, TemplateValidationResultDto>(validationResult);
    }

    public async Task SynchronizeWithERPAsync(List<string> templateIds)
    {
        _logger.LogInformation("Synchronizing {Count} templates with ERP", templateIds.Count);
        
        foreach (var templateId in templateIds)
        {
            try
            {
                var erpTemplate = await _flexiRepository.GetManufactureTemplateAsync(templateId);
                var domainTemplate = await _templateRepository.GetAsync(templateId);
                
                // Update template from ERP data
                domainTemplate.ProductName = erpTemplate.ProductName;
                domainTemplate.Description = erpTemplate.Description;
                domainTemplate.BaseAmount = erpTemplate.Amount;
                
                // Sync ingredients
                await SyncTemplateIngredients(domainTemplate, erpTemplate.Ingredients);
                
                await _templateRepository.UpdateAsync(domainTemplate);
                
                // Clear cache
                _cache.Remove($"template_{templateId}");
                
                _logger.LogInformation("Synchronized template {TemplateId} with ERP", templateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to synchronize template {TemplateId} with ERP", templateId);
            }
        }
    }

    public async Task<TemplateCostAnalysisDto> GetCostAnalysisAsync(string templateId)
    {
        var template = await _templateRepository.GetAsync(templateId);
        
        var analysis = new TemplateCostAnalysisDto
        {
            TemplateId = templateId,
            ProductCode = template.ProductCode,
            BaseAmount = template.BaseAmount,
            TotalCost = template.TotalIngredientCost,
            CostPerUnit = template.CostPerUnit,
            IngredientCount = template.IngredientCount
        };
        
        // Calculate ingredient cost breakdown
        analysis.IngredientCosts = template.Ingredients.Select(i => new IngredientCostDto
        {
            IngredientCode = i.IngredientCode,
            IngredientName = i.IngredientName,
            Amount = i.Amount,
            CostPerUnit = i.CostPerUnit,
            TotalCost = i.TotalCost,
            CostPercentage = i.CostPercentage
        }).OrderByDescending(i => i.TotalCost).ToList();
        
        return analysis;
    }

    private async Task SyncTemplateIngredients(ManufactureTemplate template, List<FlexiIngredient> erpIngredients)
    {
        // Remove ingredients not in ERP
        var erpCodes = erpIngredients.Select(i => i.Code).ToHashSet();
        var toRemove = template.Ingredients.Where(i => !erpCodes.Contains(i.IngredientCode)).ToList();
        
        foreach (var ingredient in toRemove)
        {
            template.RemoveIngredient(ingredient.IngredientCode);
        }
        
        // Add/update ingredients from ERP
        foreach (var erpIngredient in erpIngredients)
        {
            var existing = template.Ingredients.FirstOrDefault(i => i.IngredientCode == erpIngredient.Code);
            
            if (existing != null)
            {
                template.UpdateIngredient(erpIngredient.Code, erpIngredient.Amount, erpIngredient.CostPerUnit);
            }
            else
            {
                template.AddIngredient(
                    erpIngredient.Code,
                    erpIngredient.Name,
                    erpIngredient.Amount,
                    erpIngredient.Unit,
                    erpIngredient.CostPerUnit);
            }
        }
    }
}
```

### Integration with FlexiBee ERP

#### IFlexiManufactureRepository
```csharp
public interface IFlexiManufactureRepository
{
    Task<List<FlexiManufactureTemplate>> GetManufactureTemplatesAsync(IEnumerable<string> templateIds);
    Task<FlexiManufactureTemplate> GetManufactureTemplateAsync(string templateId);
    Task<List<FlexiIngredient>> GetTemplateIngredientsAsync(string templateId);
    Task SyncTemplateToERPAsync(ManufactureTemplate template);
    Task<bool> ValidateTemplateInERPAsync(string templateId);
}
```

### Caching Strategy

#### Template Caching
- **Individual Templates**: 30 minutes TTL
- **Template Lists**: 15 minutes TTL
- **Cost Analysis**: 1 hour TTL
- **Scaled Templates**: No caching (dynamic calculations)

### Performance Requirements

#### Response Time Targets
- Template retrieval: < 1 second
- Scaling calculations: < 2 seconds
- Template creation: < 3 seconds
- ERP synchronization: < 10 seconds per template

### Happy Day Scenarios

#### Scenario 1: Create Production Recipe
```
1. Production manager creates new manufacturing template
2. Adds base product information and target amount
3. Adds required ingredients with amounts and costs
4. Sets up scaling rules for specific ingredients
5. Validates template for completeness
6. Saves template and makes it active
```

#### Scenario 2: Scale Recipe for Production
```
1. Planner selects manufacturing template
2. Specifies target production amount (e.g., 1000 units)
3. System calculates scaled ingredient amounts
4. Applies scaling rules (minimums, steps, etc.)
5. Displays scaled recipe with costs
6. Planner approves and uses for production order
```

#### Scenario 3: Ingredient Cost Update
```
1. Cost analyst receives new ingredient pricing
2. Updates ingredient cost in template
3. System recalculates total template cost
4. Affected production orders are flagged
5. Updated costs flow to production planning
```

### Error Scenarios

#### Scenario 1: Invalid Scaling Request
```
User: Requests scaling with zero target amount
System: Shows error "Target amount must be greater than zero"
Action: Prompt for valid amount, suggest typical values
```

#### Scenario 2: Missing Ingredient Data
```
User: Tries to create template without ingredients
System: Shows validation error "Template must have at least one ingredient"
Action: Guide user to ingredient addition interface
```

#### Scenario 3: ERP Synchronization Failure
```
User: Initiates ERP sync for template
System: ERP connection fails
Action: Log error, show retry option, use cached data
```