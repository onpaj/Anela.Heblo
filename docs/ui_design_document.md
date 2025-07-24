## 1. Úvod

Tento dokument definuje návrhová pravidla a vizuální specifikace pro frontend business aplikaci inspirovanou reálným UI stylem (viz přiložený referenční obrázek) a založenou na utility přístupu Tailwind CSS. Výsledkem není implementace, ale normativní referenční dokument, který slouží jako zdroj pravdy pro návrh, revize a další vývoj UI.

Aplikace je určena pro interní správu dat a obsahuje typické prvky jako jsou tabulky, filtrace, přehledy, formuláře, stavy a interakce.

Cílem je sjednotit vzhled a chování všech komponent a zajistit konzistenci v celé aplikaci.

---

## 2. Zásady návrhu UI

- **Konzistence** – opakující se prvky (např. akce, interakce) vypadají a chovají se jednotně.
- **Minimalismus** – maximum informací s minimálním vizuálním šumem.
- **Přístupnost** – barvy a interakce odpovídají WCAG 2.1 AA.
- **Mobilní připravenost** – layout se přizpůsobuje menším zařízením.
- **Oddělení obsahu a navigace** – navigace (sidebar, topbar) je jasně oddělena od hlavního obsahu.

---

## 3. Layout & navigace

### 3.1 Grid systém

- Používá se `max-w-7xl` kontejner se `px-4 sm:px-6 lg:px-8` vnitřním odsazením.
- Základní grid je `grid-cols-1 sm:grid-cols-2 lg:grid-cols-3` podle šířky obrazovky.

### 3.2 Sidebar

- **Pozice:** vlevo, `fixed`, výška `100vh`, šířka `w-64`
- **Pozadí:** `bg-white`, `border-r border-gray-200`
- **Navigační prvky:**
  - Ikona + název sekce (volitelně stavová bublina)
  - Aktivní položka: `bg-gray-100`, `text-indigo-600`, `border-l-4 border-indigo-600`
  - Hover efekt: `bg-gray-50`
- **Responsivita:**
  - Zobrazit jako `hidden` pod `md:` breakpointem
  - Na mobilech: přepínatelné přes hamburger menu jako `overlay`

### 3.3 Topbar

- Obsahuje vyhledávání, ikony akcí, uživatelský avatar
- Zarovnání do pravé části pomocí `flex justify-end`
- Pozadí `white`, border bottom `border-b border-gray-200`

---

## 4. Barevná paleta

Barevná paleta je inspirovaná TailwindUI business dashboardem a využívá jemné neutrální tóny s akcenty pro stavové a akční prvky.

| Název        | Tailwind třída     | Použití                             |
| ------------ | ------------------ | ----------------------------------- |
| `bg-base`    | `bg-gray-50`       | Hlavní pozadí content oblasti       |
| `bg-sidebar` | `bg-white`         | Sidebar a bílá plocha navigace      |
| `text-main`  | `text-gray-900`    | Hlavní texty, nadpisy               |
| `text-sub`   | `text-gray-500`    | Metadata, popisky, pomocný text     |
| `accent`     | `text-indigo-600`  | Odkazy, zvýraznění                  |
| `success`    | `text-emerald-500` | Pozitivní stav, publikováno, úspěch |
| `error`      | `text-rose-500`    | Negativní stav, chyba, varování     |
| `border`     | `border-gray-200`  | Hraniční linie mezi prvky           |
| `cta`        | `bg-rose-400`      | Hlavní akční tlačítko               |
| `hover`      | `bg-gray-100`      | Hover efekty, aktivní řádky         |
| `icon-muted` | `text-gray-400`    | Sekundární ikony                    |

Barvy jsou vybírány ze standardní Tailwind CSS palety (v4) a zajišťují dostatečný kontrast a čitelnost. Jsou připravené pro případné rozšíření o `dark mode` varianty.

---

## 5. Typografie

Používáme výchozí systémové fonty:

```css
font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
```

### Hierarchie textu

| Styl         | Tailwind třída            | Použití                  |
| ------------ | ------------------------- | ------------------------ |
| `Heading XL` | `text-2xl font-bold`      | Stránkové nadpisy        |
| `Heading L`  | `text-xl font-semibold`   | Sekce                    |
| `Body`       | `text-base text-gray-900` | Základní text            |
| `Muted`      | `text-sm text-gray-500`   | Popisky, metadata        |
| `Caption`    | `text-xs text-gray-400`   | Pomocné značky, tooltipy |

---

## 6. Komponenty

### 6.1 Buttons

(Zachováno dle předchozí definice)

### 6.2 Formulářové prvky

(Zachováno dle předchozí definice)

### 6.3 Tabulky

Tabulky jsou určeny pro přehledné zobrazení řádkových dat. Inspirací je layout z dashboardu s oddělenými řádky, akcemi a vizuálně odlišeným stavem záznamu.

#### Vzhled

- **Hlavička:** `uppercase text-xs text-gray-500 tracking-wide bg-white border-b border-gray-200`
- **Řádky:** `hover:bg-gray-50`, výška řádku `h-12`, padding `px-4`
- **Odsazení ikon nebo akcí:** ikony zarovnány pomocí `flex justify-end`

#### Interaktivní buňky

- `cursor-pointer`, hover pozadí
- `font-medium text-indigo-600` pro klikatelné prvky

#### Stavové značky (status badges)

```html
<span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-emerald-100 text-emerald-800">Published</span>
<span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">Draft</span>
```

---

## 7. Interakce a stavy

| Stav              | Styl / Třída                                                                  |
| ----------------- | ----------------------------------------------------------------------------- |
| Hover řádek       | `hover:bg-gray-50`                                                            |
| Aktivní sidebar   | `bg-gray-100 text-indigo-600 border-l-4 border-indigo-600`                    |
| Focus input       | `focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500`                      |
| Disabled tlačítko | `bg-gray-100 text-gray-400 cursor-not-allowed`                                |
| Chyba formuláře   | `border-rose-500 text-rose-600 placeholder-rose-300` + hláška `text-rose-600` |

---

## 8. Responsivita

- Breakpointy Tailwind (`sm`, `md`, `lg`, `xl`, `2xl`) zůstávají výchozí
- Sidebar je viditelný od `md:` výše, pod tím `hidden`
- Grid rozpadá obsah ze 3 sloupců (`lg`) přes 2 (`sm`) až na 1 (`base`)
- Ikony a tlačítka mají `min-w` a `w-full` varianty podle potřeby

---

## 9. Přístupnost (a11y)

- Všechny interaktivní prvky mají `:focus` styl
- Formuláře mají `label` spárovaný s `for`
- Barvy splňují minimálně kontrastní poměr WCAG 2.1 AA (text: 4.5:1)
- Ikony mají `aria-hidden` a/nebo `sr-only` texty

---

## 10. Ikony a ilustrace

- Ikony používáme z knihovny [**Heroicons**](https://heroicons.com/) nebo [**Lucide**](https://lucide.dev/) (outline styl)
- Ikony mají výchozí velikost `w-5 h-5`, barevnost `text-gray-400`
- Ikony uvnitř tlačítek: zarovnat pomocí `-ml-1 mr-2` nebo `ml-2 -mr-1`
- Ilustrace nejsou zatím používány (volitelné)

---

## 11. Animace a přechody

- Přechody se používají na:
  - tlačítka: `transition-colors duration-150`
  - tabulky (hover): `transition-background`
  - menu, dropdown: `transition ease-out duration-100`
- Animace jsou vždy funkční, ne dekorativní
- Základní efekt: plynulá změna barvy, pozadí nebo opacity

---

## 12. Appendix – referenční odkazy

- Tailwind CSS: [https://tailwindcss.com/docs](https://tailwindcss.com/docs)
- Heroicons: [https://heroicons.com](https://heroicons.com)
- Lucide Icons: [https://lucide.dev](https://lucide.dev)
- TailwindUI inspiration: [https://tailwindui.com/components/application-ui](https://tailwindui.com/components/application-ui)

---

