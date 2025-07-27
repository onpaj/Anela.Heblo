# Invoice Import Feature Definition

## Goal
Cilem teto feature je prenest faktury, ktere vznikaji v eshopu (shoptet) do ERP systemu (ABRA Flexi).


## Description
- Feature umoznuje importovat faktury na zaklade z techto podminek:
 - Bud podle konkretniho cisla faktury nebo podle rozsahu datumu (od-do). Jedna z techto podminek musi byt vyplnena.
 - Musi byt definovano, zda chceme fakturu v CZK nebo EUR
- System pomoci playwright simuluje prihlaseni do administrace eshopu a proklika se az k funkci exportu faktur
- Tam zda specifikovane podminky a stahne fakturu ve formatu XML 
- System umozni programove pridavat operace do pipeline, ktere mohou menit nektere hodnoty nactenych dat
- Uploadne faktury do ERP systemu abra flexi pomoci clienta (nuget package Rem.FlexiBeeSdk.Client)
- O kazde importovane fakture vytvori zaznam v databazi, ktery obsahuje:
  - Datum importu
  - Status importu (uspesny/neuspesny)
  - Chybovou hlasku (pokud nastala)
  - ID faktury v ABRA Flexi
  - Kolikaty je to pokus o import (pro pripad, ze se faktura nepodarila importovat)
  - Kdo import vyvolal (automat nebo manualne)