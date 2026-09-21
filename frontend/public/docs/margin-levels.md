# Hladiny marže — co která znamená

Marže se počítá ve čtyřech hladinách. Každá přidává další vrstvu nákladů, takže
číslo klesá odshora dolů. Hladina má vždy dvě čísla:

- **náklad hladiny** — kolik přidává právě tato vrstva,
- **náklad celkem** — součet všech vrstev, ze kterého se počítá procento.

Marže se počítá z **prodejní ceny bez DPH** (`PriceWithoutVat`):

```
marže (Kč) = cena bez DPH − náklad celkem
marže (%)  = marže (Kč) / cena bez DPH × 100
```

Produkt bez prodejní ceny nemá marži vůbec — v přehledu se neobjeví.

---

## Přehled

| Hladina | Název | Co přidává | Náklad celkem |
|---|---|---|---|
| **M0** | Materiálový náklad | materiál / nákupní cena | M0 |
| **M1_A** | Plošný výrobní náklad | rozpočítaná výrobní práce | M0 + M1_A |
| **M1_B** | Přímý výrobní náklad | náklad konkrétní dávky | M0 + M1_B |
| **M2** | Sklad a marketing | skladování + marketing | M0 + M1_A + M1_B + M2 |

**M1_A a M1_B nejsou na sobě postavené** — jsou to dva různé pohledy na tutéž
výrobní práci. Proto M1_B **nestojí** na M1_A. Do konečného součtu v M2 ale
vstupují **obě**.

---

## Společná pravidla

**Okno.** Všechny čtyři hladiny čtou stejně dlouhou historii, danou jedním
nastavením `DataSourceOptions:ManufactureCostHistoryDays` (v produkci **365 dní**).
Okno se zaokrouhluje na celé měsíce: od prvního dne měsíce, do kterého spadá
„dnes minus 365 dní“, do konce aktuálního měsíce.

**Měsíční hodnoty.** Náklad se drží po měsících. Průměr v přehledu je průměr
přes měsíce, které náklad skutečně nesou — měsíc bez dat průměr neředí.

**Zdroj nákladů.** M1_A a M2 čtou účetní deník z FlexiBee (`ucetni-denik`),
vždy podle **střediska** a **předvolby účtu**. M0 čte výrobní a nákupní historii
produktu.

---

## M0 — Materiálový náklad

Základní náklad na to, z čeho je produkt.

**Vyráběné položky** (výrobek, polotovar, dárkový balíček) berou cenu
z **výrobní historie**: za každý měsíc vážený průměr ceny za kus přes všechny
příjemky z výroby v tom měsíci, vážený množstvím.

Měsíc bez výroby nezůstane prázdný:

- existuje-li dřívější výroba, **přenese se poslední známá cena** dopředu,
- nebyla-li výroba ještě žádná, použije se **nejbližší budoucí** cena zpětně,
- není-li ani jedno, měsíc nemá M0.

**Nakupované položky** (zboží, materiál) a vyráběné položky bez jediné výrobní
příjemky používají **nákupní cenu s DPH** (`PurchasePriceWithVat`), stejnou pro
všechny měsíce.

---

## M1_A — Plošný výrobní náklad

Celá výrobní práce firmy rozpočítaná na produkty podle náročnosti.

```
sazba        = náklady střediska VYROBA / Σ (vyrobené množství × náročnost)
M1_A na kus  = (vyrobené množství × náročnost) × sazba / vyrobené množství
```

**Náklady:** středisko **VYROBA**, účty **51x** (služby) a **52x** (osobní
náklady) — tedy mzdy výroby, jejich odvody a provozní služby výroby.

**Náročnost** (`ManufactureDifficultySettings`) je koeficient na produkt, platný
od data. Produkt bez nastavené náročnosti má koeficient **1**.

**Do jmenovatele vstupují jen výrobky a dárkové balíčky.** Zboží a materiál se
nevyrábí, takže si výrobní práci nedělí. **Polotovary jsou vyloučené záměrně:**
jejich příjemky jsou v gramech hmoty a nemají nastavenou náročnost, takže by
každý gram dostal jeden bod, rozředil jmenovatel a výrobní práce by uvízla na
hmotě, která se nikdy neprodá. Polotovar proto má M1_A = 0.

Produkt, který se za posledních 365 dní nevyráběl, má M1_A = 0.

---

## M1_B — Přímý výrobní náklad

Zamýšlený jako skutečný náklad konkrétní výrobní dávky, tedy alternativa
k plošnému rozpočtu M1_A.

> **Zatím není implementovaný.** Vrací pevnou konstantu **15 Kč na kus** pro
> každý produkt a každý měsíc. Není to měřený údaj.
>
> Přesto se **započítává do součtu v M2** — konečná marže je tedy o těchto
> 15 Kč nižší, než by odpovídalo skutečnosti. Vlastní řádek v tabulce nemá.

---

## M2 — Sklad a marketing

Náklady skladu a marketingu rozpočítané rovným dílem na prodaný kus.

```
M2 na kus = náklady (SKLAD + MARKETING) / celkový počet prodaných kusů
```

**Náklady:** střediska **SKLAD** a **MARKETING**, účty **50x** (spotřeba —
expediční obaly, potisknuté pásky, marketingový tisk), **51x** (služby — reklama,
nájem skladu, copywriting) a **52x** (osobní náklady skladu a marketingu).

Kurzové ztráty (56x) a daně a poplatky (53x) do M2 nepatří.

**Jmenovatel:** všechny prodané kusy všech položek katalogu za stejné okno.
Nezapočítávají se položky rozpadlé z dárkových balíčků (ty by se počítaly
dvakrát — balíček je už započítaný sám za sebe) a položky, které nejsou
v katalogu (například příspěvek Znesnáze).

**Rozpočítává se na kusy, ne na obrat.** Každý prodaný kus nese stejnou
částku — deodorant 4,9 ml i krém 180 ml. U levných malých produktů to marži
ukrojí nepoměrně víc.

---

## Co v žádné hladině není

Hladiny marže **nepokrývají celou režii firmy**. Mimo ně zůstávají:

- **centrála** — administrativa, IT, vedení, odpisy,
- **prodejna**,
- účty **53x–57x** napříč firmou,
- finanční náklady a daň z příjmů (58x, 59x),
- **BUVOL** — samostatná činnost, která není režií Anely.

Stav k **21. 9. 2026** za posledních 13 měsíců: M2 rozpočítává **11,9 mil. Kč**,
zatímco celá nemateriálová režie po odečtu výrobních mezd je **19,4 mil. Kč**.
Hladiny marže tedy pokrývají zhruba **tři pětiny** režie.

**Prakticky to znamená:** marže na hladině M2 **není cenová podlaha**. Je to
příspěvek na úhradu zbytku režie a zisku. Cenu nelze stavět tak, aby M2 vyšla
těsně nad nulu — zbylých osm milionů by nemělo z čeho být zaplaceno.

---

## Na co si dát pozor

- **Konečná marže je podhodnocená** o 15 Kč na kus z nedodělaného M1_B.
- **M1_A = 0** u produktu znamená „za posledních 365 dní se nevyráběl“, ne
  „nestojí žádnou práci“.
- **Polotovary** mají M1_A = 0 záměrně; jejich práce je v ceně hotového výrobku.
- **Změna okna** (`ManufactureCostHistoryDays`) přepočítá **všechny** hladiny
  najednou, ne jen výrobní.
