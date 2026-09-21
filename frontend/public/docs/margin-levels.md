# Hladiny marže — co která znamená

Marže se počítá ve čtyřech hladinách. Každá přidává další vrstvu nákladů, takže
číslo klesá odshora dolů. Hladina má vždy dvě čísla:

- **náklad hladiny** — kolik přidává právě tato vrstva,
- **náklad celkem** — součet této a všech předchozích vrstev, ze kterého se
  počítá procento.

Hladiny jsou **kumulativní**: M0 → M1 → M2 → M3. Poslední hladina M3 je pohled
„všechny náklady započteny“ a podle ní se přehled ve výchozím stavu řadí.

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
| **M1** | Výrobní náklad | rozpočítaná výrobní práce (VYROBA) | M0 + M1 |
| **M2** | Sklad a marketing | skladování + marketing | M0 + M1 + M2 |
| **M3** | Režie | zbytek přímých nákladů firmy | M0 + M1 + M2 + M3 |

---

## Společná pravidla

**Okno.** Všechny čtyři hladiny čtou stejně dlouhou historii, danou jedním
nastavením `DataSourceOptions:ManufactureCostHistoryDays` (v produkci **365 dní**).
Okno se zaokrouhluje na celé měsíce: od prvního dne měsíce, do kterého spadá
„dnes minus 365 dní“, do konce aktuálního měsíce.

**Měsíční hodnoty.** Náklad se drží po měsících. Průměr v přehledu je průměr
přes měsíce, které náklad skutečně nesou — měsíc bez dat průměr neředí.

**Zdroj nákladů.** M1, M2 a M3 čtou účetní deník z FlexiBee (`ucetni-denik`),
vždy podle **střediska** a **předvolby účtu**. M0 čte výrobní a nákupní historii
produktu.

**Rozsah účtů se liší podle hladiny.** M1 a M3 počítají účty **51x** a **52x**.
M2 navíc počítá **50x**, protože ve skladu a marketingu je to expediční obalový
materiál a marketingový tisk. Jinde je 50x prodané zboží, které do režie nepatří.

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

## M1 — Výrobní náklad

Celá výrobní práce firmy rozpočítaná na produkty podle náročnosti.

```
sazba      = náklady střediska VYROBA / Σ (vyrobené množství × náročnost)
M1 na kus  = náročnost × sazba
```

**Náklady:** středisko **VYROBA**, účty **51x** (služby) a **52x** (osobní
náklady) — tedy mzdy výroby, jejich odvody a provozní služby výroby.

**Náročnost** (`ManufactureDifficultySettings`) je koeficient na produkt, platný
od data. Produkt bez nastavené náročnosti má koeficient **1**.

**Do jmenovatele vstupují jen výrobky a dárkové balíčky.** Zboží a materiál se
nevyrábí, takže si výrobní práci nedělí. **Polotovary jsou vyloučené záměrně:**
jejich příjemky jsou v gramech hmoty a nemají nastavenou náročnost, takže by
každý gram dostal jeden bod, rozředil jmenovatel a výrobní práce by uvízla na
hmotě, která se nikdy neprodá. Polotovar proto má M1 = 0.

Produkt, který se za posledních 365 dní nevyráběl, má M1 = 0.

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

## M3 — Režie

Zbytek přímých nákladů firmy — všechno na účtech 51x a 52x, co si nevzala
žádná z předchozích hladin. Prakticky jde o **centrálu** (administrativa, IT,
vedení), **prodejnu** a jakékoli nezařazené středisko.

```
M3 na kus = režijní náklady / celkový počet prodaných kusů
```

**Jmenovatel je stejný jako u M2** — tytéž prodané kusy, včetně stejného
vyloučení komponent rozpadlých z dárkových balíčků. Náklad na kus je plošný:
stejný pro každý produkt i každý měsíc okna.

**Středisko přidané ve FlexiBee spadne do M3 automaticky.** M3 je definované
jako doplněk, ne výčtem — nové středisko se tak objeví v režii místo aby
z výpočtu zmizelo.

---

## Co v žádné hladině není

Hladiny marže **nepokrývají celou režii firmy**. Mimo ně zůstávají:

- účty **53x–57x** napříč firmou (daně a poplatky, odpisy, kurzové ztráty),
- finanční náklady a daň z příjmů (58x, 59x),
- účty **50x mimo sklad a marketing** — v centrále je to prodané zboží, řádově
  víc než všechny hladiny dohromady.

**Prakticky to znamená:** ani marže na hladině M3 **není cenová podlaha**. Je to
příspěvek na úhradu zbytku nákladů a zisku.

---

## Na co si dát pozor

- **M1 = 0** u produktu znamená „za posledních 365 dní se nevyráběl“, ne
  „nestojí žádnou práci“.
- **Polotovary** mají M1 = 0 záměrně; jejich práce je v ceně hotového výrobku.
- **M2 a M3 se rozpočítávají na kusy**, takže levné malé produkty nesou stejnou
  částku jako drahé velké.
- **Změna okna** (`ManufactureCostHistoryDays`) přepočítá **všechny** hladiny
  najednou, ne jen výrobní.
