# Specification of M1 Calculation – Production Costs

## 1. Purpose of M1

M1 represents **production capacity costs** required to manufacture products, **excluding materials (M0)** and **excluding sales costs (M2)**.

M1 is tracked in two perspectives:
- **M1_A** – long-term economic cost per unit (baseline)
- **M1_B** – actual production cost in a specific month (efficiency KPI)

---

## 2. Scope of Costs Included in M1

### 2.1 Included Costs (INCLUDE)

M1 includes exclusively costs necessary to maintain and utilize production capacity:

- production worker wages (including payroll taxes)
- supervisor / direct production management wages
- energy consumed by production (electricity, gas, water)
- rent / depreciation of production facilities
- depreciation of production machinery
- machinery service and maintenance
- production overhead (PPE, minor production materials)
- production planning costs

### 2.2 Excluded Costs (EXCLUDE)

M1 does not include:

- raw materials and packaging (M0)
- finished goods warehouse
- shipping, logistics
- marketing and sales
- company administration (M3)
- R&D, unless it is a direct part of regular production

---

## 3. Basic Concepts

### 3.1 Complexity Point (CP)

An abstract unit representing production capacity consumption.

- dimensionless
- stable over time
- comparable across products

### 3.2 Product

Each product has defined:

```
CP_per_unit (float > 0)
```

- changes only when technology changes
- changes must be versioned (validity from–to)

### 3.3 Production Batch

Recorded data:

```
product_id
production_date (YYYY-MM)
units_produced
CP_per_unit
```

Derived:

```
produced_CP = units_produced × CP_per_unit
```

---

## 4. M1_B – Actual Production Cost (monthly)

### 4.1 Purpose
- tracking production efficiency
- detecting impact of batches, downtime, investments

### 4.2 Calculation

For month M:

```
M1_B_per_unit_M = M1_costs_M / units_produced_M
```

Alternatively (recommended):

```
M1_B_per_CP_M = M1_costs_M / produced_CP_M
```

### 4.3 Special Cases

- if `units_produced_M = 0` → value = NULL
- M1_B is not calculated in months without production

---

## 5. M1_A – Economic Production Cost (baseline)

### 5.1 Reference Period

- standard: rolling 12 months
- alternatively calendar year

### 5.2 Cost of 1 Complexity Point

```
cost_per_CP =
(∑ M1_costs_period) / (∑ produced_CP_period)
```

### 5.3 Calculation of Product M1_A

```
M1_A_product = CP_per_unit × cost_per_CP
```

Properties:
- constant within the period
- changes only when CP cost is recalculated or product CP changes

---

## 6. Time Allocation

### 6.1 M1_B
- belongs to the month when cost occurred
- used for analytics only

### 6.2 M1_A
- allocated at production
- enters inventory valuation
- released to P&L upon sale

---

## 7. Reporting

### Mandatory Metrics
- M1_B_per_CP (monthly)
- M1_B_per_unit (monthly)
- M1_A per unit (current rate)
- M1_B trend (rolling average)

### Prohibited Interpretations
- do not use M1_B for pricing
- do not interpret M1 in months without production
- do not compare M1_A across months without recalculation

---

## 8. Controls

- ∑ allocated M1_A ≈ ∑ M1 costs for the period
- long-term average M1_B ≈ M1_A
- significant fluctuations without CP changes = suspected error

---

## 9. Summary

| Metric | Purpose | Time |
|------|------|----|
| M1_B | Production efficiency | month |
| M1_A | Economic truth | period |
