# UI Templates - Seznam a Detail

Tento dokument definuje základní principy pro zobrazení seznamů a detailů v aplikaci Anela Heblo.

## Obecný seznam (List View)

### Struktura layoutu
- **Kompaktní header** - pouze název stránky (text-lg), bez zbytečných popisů
- **Filtry** - vždy nahoře v samostatném panelu s bílým pozadím a stínem
- **Datová tabulka** - hlavní obsah s možností scrollování
- **Stránkování** - fixní na spodku stránky

### Principy filtrování
- Textové filtry s ikonou lupy
- Tlačítka "Filtrovat" a "Vymazat" vpravo
- Aplikace filtrů na Enter nebo kliknutím na tlačítko
- Oddělené vstupní hodnoty od aplikovaných filtrů

### Tabulka dat
- Klikatelné řádky pro otevření detailu (hover efekt)
- Řazení sloupců s vizuální indikací (šipky nahoru/dolů)
- Sticky header při scrollování
- Zaoblené hodnoty pro lepší čitelnost

### Stránkování
- Zobrazení počtu výsledků s informací o filtrech
- Volba počtu položek na stránku
- Navigace s čísly stránek a šipkami

## Detail položky (Detail View)

### Modální okno
- Překrytí celé obrazovky s poloprůhledným pozadím
- Maximální šířka pro čitelnost (max-w-7xl)
- Zavření: tlačítko X, klik na pozadí, klávesa Esc

### Struktura obsahu
- **Header** - ikona, název položky a základní identifikátor
- **Dvousloupcový layout** - levý sloupec informace, pravý vizualizace
- **Sekce s ikonami** - každá sekce má svou ikonu pro lepší orientaci
- **Scrollovatelný obsah** - při překročení výšky obrazovky

### Zobrazení dat
- **Základní informace** - strukturované v šedých boxech
- **Číselné hodnoty** - zvýraznění důležitých hodnot (dostupnost)
- **Tabulky** - kompaktní zobrazení s čitelnými hlavičkami
- **Grafy** - responzivní s legendou a popisky os

### Interakce
- Tlačítko "Zavřít" v patičce
- Plynulé animace při otevírání/zavírání
- Loading stavy s indikátory načítání
- Error stavy s ikonou a popisem chyby

## Barevná paleta

- **Primární akce** - indigo-600 (hover: indigo-700)
- **Sekundární akce** - gray-600 (hover: gray-700)
- **Úspěch/Dostupnost** - green (100/800)
- **Pozadí sekcí** - gray-50
- **Ohraničení** - gray-200/300

## Responzivní chování

- Mobilní verze skrývá méně důležité informace
- Tabulky zůstávají horizontálně scrollovatelné
- Modální okna se přizpůsobují výšce obrazovky
- Filtry se zalomí na menších obrazovkách