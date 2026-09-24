#!/usr/bin/env python3
"""Read-only report: Flexi ceník nakupCena vs average stock price (prumCena).

Lists every material (warehouse 5) and goods item (warehouse 4) with what the nightly
purchase price sync would do to it: write / unchanged / skip-no-stock-price.
Only GET requests are made.

Approximation: the job classifies items by the catalog type (from stock skupZboz);
this report uses ceník typZasobyK, so a few items may be classified differently.

Usage:
  FLEXI_SERVER=https://petra-tesarikova.flexibee.eu \
  FLEXI_COMPANY=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Company --query value -o tsv) \
  FLEXI_LOGIN=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Login --query value -o tsv) \
  FLEXI_PASSWORD=$(az keyvault secret show --vault-name kv-heblo-prod -n FlexiBeeSettings--Password --query value -o tsv) \
  scripts/flexi-purchase-price-report.py > purchase-price-report.csv
"""
import base64
import csv
import datetime
import json
import math
import os
import sys
import urllib.parse
import urllib.request

TOLERANCE = 0.0001  # same as RecalculatePurchasePriceHandler.PurchasePriceTolerance
WAREHOUSE_BY_TYPE = {"typZasoby.material": 5, "typZasoby.zbozi": 4}

SERVER = os.environ["FLEXI_SERVER"].rstrip("/")
COMPANY = os.environ["FLEXI_COMPANY"]
AUTH = base64.b64encode(f"{os.environ['FLEXI_LOGIN']}:{os.environ['FLEXI_PASSWORD']}".encode()).decode()


def get(resource, params):
    url = f"{SERVER}/c/{COMPANY}/{resource}.json?{urllib.parse.urlencode(params)}"
    request = urllib.request.Request(url, headers={"Authorization": f"Basic {AUTH}"})
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.load(response)["winstrom"][resource]


def stock_prices(warehouse_id, date):
    rows = get("stav-skladu-k-datu", {
        "datum": date,
        "sklad": warehouse_id,
        "detail": "custom:cenik(kod),prumCena,stavMJ,tuz",
        "includes": "/stav-skladu-k-datu/cenik",
        "limit": 0,
    })
    prices = {}
    for row in rows:
        cenik = row.get("cenik") or []
        code = (cenik[0].get("kod") if isinstance(cenik, list) and cenik else "") or ""
        code = code.strip()
        if code and code not in prices:
            qty = float(row.get("stavMJ") or 0)
            value = float(row.get("tuz") or 0)
            # prumCena is rounded to 2 decimals; the job uses tuz / stavMJ (SDK ExactAveragePrice).
            exact = value / qty if qty > 0 and value > 0 else float(row.get("prumCena") or 0)
            prices[code] = (exact, qty)
    return prices


def distance(row):
    if row["nakupCena"] == 0 and row["action"] == "write":
        # Flexi has no purchase price at all for an item with real stock value: the most-wrong
        # row on the sheet, so it must sort first regardless of the (zero) ratio.
        return float("inf")
    ratio = row["nakupCena/prumCena"]
    return abs(math.log(ratio)) if isinstance(ratio, float) and ratio > 0 else -1


def main():
    today = datetime.date.today().isoformat()
    cenik = get("cenik", {"detail": "custom:id,kod,nazev,nakupCena,typZasobyK,mj1", "limit": 0})
    stock = {wh: stock_prices(wh, today) for wh in set(WAREHOUSE_BY_TYPE.values())}

    rows = []
    for item in cenik:
        warehouse = WAREHOUSE_BY_TYPE.get(item.get("typZasobyK"))
        if warehouse is None:
            continue
        code = item["kod"].strip()
        current = float(item.get("nakupCena") or 0)
        prum, qty = stock[warehouse].get(code, (None, None))
        if prum is None or prum <= 0:
            action, ratio = "skip-no-stock-price", ""
        elif abs(prum - current) < TOLERANCE:
            action, ratio = "unchanged", 1.0
        else:
            action = "write"
            ratio = round(current / prum, 3) if prum else ""
        rows.append({
            "kod": code, "nazev": item.get("nazev", ""), "typ": item.get("typZasobyK"),
            "mj": item.get("mj1@showAs", item.get("mj1", "")), "nakupCena": current,
            "prumCena": prum if prum is not None else "", "stavMJ": qty if qty is not None else "",
            "nakupCena/prumCena": ratio, "action": action,
        })

    rows.sort(key=distance, reverse=True)
    writer = csv.DictWriter(sys.stdout, fieldnames=list(rows[0].keys()) if rows else ["kod"])
    writer.writeheader()
    writer.writerows(rows)
    print(f"{len(rows)} items, {sum(r['action'] == 'write' for r in rows)} would be written", file=sys.stderr)


if __name__ == "__main__":
    main()
