## 1. Úvod

Tento dokument definuje návrhová pravidla a vizuální specifikace pro frontend business aplikaci inspirovanou reálným UI stylem a založenou na utility přístupu Tailwind CSS s **Lucide React** ikonami. Výsledkem není implementace, ale normativní referenční dokument, který slouží jako zdroj pravdy pro návrh, revize a další vývoj UI.

Aplikace je určena pro interní správu dat a obsahuje typické prvky jako jsou tabulky, filtrace, přehledy, formuláře, stavy a interakce.

Cílem je sjednotit vzhled a chování všech komponent a zajistit konzistenci v celé aplikaci.

### Ikony
- **Knihovna**: Lucide React pro moderní, konzistentní a dostupné ikony
- **Standardní velikosti**: `h-4 w-4` (malé), `h-5 w-5` (střední), `h-6 w-6` (velké)

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

- **Pozice:** vlevo, `fixed`, výška `100vh`
- **Šířka:** `w-64` (rozbalený), `w-16` (složený)
- **Pozadí:** `bg-white`, `border-r border-gray-200`, `shadow-sm`
- **Skládání/rozbalování:**
  - Ovládá se pomocí toggle tlačítka na spodku sidebaru (ikony `PanelLeftClose`/`PanelLeftOpen`)
  - **Umístění tlačítka**: Ve spodní části sidebaru vedle uživatelského profilu
  - **Rozbalený stav**: Tlačítko vpravo od avatara (`PanelLeftClose`)
  - **Složený stav**: Tlačítko uprostřed pod avatarem (`PanelLeftOpen`)
  - Plynulé animace pomocí `transition-all duration-300`
  - Ve složeném stavu: pouze ikony, tooltip na hover (`title` atribut)
  - V rozbaleném stavu: ikony + text
- **Navigační prvky:**
  - **Ikony**: Lucide React ikony (velikost `h-5 w-5`) + název sekce
  - **Hierarchická struktura**: Podporuje kategorie/sekce s možností collapsible funkcionalitě
  - **Aktivní položka**: `bg-indigo-50`, `text-indigo-700`, `border-r-2 border-indigo-700`
  - **Hover efekt**: `bg-gray-50`
  - **Accordion styl**: Více sekcí může být otevřeno současně (multi-expand)
  - **Collapsible sekce**: Podpora pro rozbalování/skládání sekcí s sub-položkami
  - **Indikátor rozbalení**: Šipka vpravo (dolů/vpravo) pro collapsible sekce
  - **Sub-položky**: Zarovnané pod hlavní sekcí, bez ikon, pouze text
  - **Komponenty**: Vhodné použití `Accordion`, `Disclosure` nebo `HeadlessUI` komponent
- **Responsivita:**
  - Zobrazit jako `hidden` pod `md:` breakpointem
  - Na mobilech: přepínatelné přes hamburger menu jako `overlay`
  - Skládání funguje pouze na desktop (`md:` a výše)

### 3.3 Topbar

- Obsahuje vyhledávání, **Lucide React ikony** pro akce, uživatelský avatar
- **Layout:** Levá strana (mobile menu), pravá strana (search + akce)
- Pozadí `white`, border bottom `border-b border-gray-200`, `shadow-sm`
- **Ikony a prvky:**
  - **Mobile menu**: `Menu` ikona (`h-6 w-6`), pouze na mobilech
  - **Search**: Input s `Search` ikonou (`h-4 w-4`) a dropdown (`ChevronDown`)
  - **Settings**: `Settings` ikona (`h-5 w-5`)
  - **CTA button**: Rose tlačítko s `Plus` a `ChevronDown` ikonami (`h-4 w-4`)
- **Responsivita:** 
  - Desktop: full search bar a akční tlačítka
  - Mobile: hamburger menu + search ikona

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

### 4.1 Accent Colors

Aplikace podporuje flexibilní accent barvy pro různé sekce:
- **Růžová (Rose)**: Hlavní CTA a důležité akce
- **Indigo**: Sekundární akce a odkazy
- **Emerald**: Pozitivní stavy a úspěchy
- **Orange/Amber**: Varování a upozornění

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

### 10.1 Standardy ikon

- **Primární knihovna**: [**Lucide React**](https://lucide.dev/) pro moderní, konzistentní ikony
- **Fallback**: [**Heroicons**](https://heroicons.com/) (outline styl) jako alternativa
- **Výchozí velikosti**: `h-4 w-4` (malé), `h-5 w-5` (střední), `h-6 w-6` (velké)
- **Barevnost**: `text-gray-400` (muted), `text-gray-600` (active)

### 10.2 Ikony v navigaci

- **Velikost v sidebaru**: `h-5 w-5` pro všechny navigační ikony
- **Sémantické ikony**: Ikony musí odpovídat významu sekce/funkce
- **Tooltips**: Ve složené navigaci vždy použít `title` atribut pro popis
- **Zarovnání**: Ikony uvnitř tlačítek zarovnat pomocí `-ml-1 mr-2` nebo `ml-2 -mr-1`

### 10.3 Indikátory a ovládací prvky

- **Collapse/Expand**: `PanelLeftClose` / `PanelLeftOpen`
- **Dropdown**: `ChevronDown` / `ChevronUp` s `transition-transform` animací
- **Loading states**: Kombinace s `animate-spin` nebo `animate-pulse`
- **User actions**: `LogIn`, `LogOut`, `User` pro autentizaci

### 10.4 Illustrations

- Ilustrace nejsou zatím používány (volitelné)
- Preferovat funkční ikony před dekorativními prvky

---

## 11. Animace a přechody

### 11.1 Základní přechody

- **Tlačítka**: `transition-colors duration-150` nebo `transition-colors duration-200`
- **Tabulky (hover)**: `transition-background`
- **Menu, dropdown**: `transition ease-out duration-100`

### 11.2 Specifické animace

- **Sidebar collapse/expand**: `transition-all duration-300 ease-in-out`
- **Loading states**: `animate-spin` pro spinnery, `animate-pulse` pro placeholder stavy
- **Dropdown šipky**: `transition-transform` s `rotate-180` pro otočení
- **User menu**: Kombinace `bottom-full` pozice s `mb-2` pro vyskakovací menu

### 11.3 Zásady animací

- **Funkční účel**: Animace vždy slouží k lepšímu pochopení změn stavu
- **Konzistentní timing**: Používat standardní duration hodnoty (100ms, 150ms, 200ms, 300ms)
- **Plynulost**: Základní efekt je plynulá změna barvy, pozadí, pozice nebo opacity
- **Performance**: Preferovat `transform` a `opacity` před animacemi layout vlastností

### 11.4 Loading stavy

- **Autentizace**: Spinner s českou lokalizací ("Přihlašování...", "Kontrola přihlášení...")
- **User avatar**: `animate-pulse` na placeholder během načítání
- **Konsistentní indikátory**: Jednotný vzhled loading stavů napříč aplikací

---

## 12. Autentizace a uživatelské rozhraní

### 12.1 AuthGuard pattern

- **Ochrana aplikace**: Celá aplikace je chráněná authentizačním guardem
- **Automatické přesměrování**: Nepřihlášení uživatelé jsou automaticky přesměrováni na Microsoft login
- **Loading stavy**: Zobrazení loading indikátorů během ověřování s českými texty
- **Centralizované**: Jeden vstupní bod pro kontrolu autentizace

### 12.2 User Profile komponenta

- **Dual mode**: Podporuje compact (složený sidebar) a expanded (rozbalený sidebar) režim  
- **Avatar**: Circular avatar s iniciálami uživatele, `bg-indigo-600` pozadí
- **Role badges**: Zobrazení uživatelských rolí jako `bg-indigo-100 text-indigo-800` značky
- **Dropdown menu**: Vyskakovací menu s uživatelskými údaji a logout funkcí
- **Responsive**: Přizpůsobuje se kontextu (sidebar collapsed/expanded)

### 12.3 Lokalizace

- **Čeština jako primární**: Všechny UI texty v českém jazyce
- **Loading stavy**: "Přihlašování...", "Kontrola přihlášení..."
- **i18next framework**: Připraveno pro rozšíření o další jazyky
- **Konzistentní terminologie**: Jednotná terminologie napříč aplikací

### 12.4 Accessibility patterns

- **Focus states**: Všechny interaktivní prvky mají viditelný `:focus` styl
- **Keyboard navigation**: Support pro ovládání klávesnicí
- **Screen reader support**: Správné ARIA labely a strukturování
- **Tooltips**: Informativní tooltips pro collapsed stavy

---

## 13. Appendix – referenční odkazy

- Tailwind CSS: [https://tailwindcss.com/docs](https://tailwindcss.com/docs)
- Heroicons: [https://heroicons.com](https://heroicons.com)
- Lucide Icons: [https://lucide.dev](https://lucide.dev)
- TailwindUI inspiration: [https://tailwindui.com/components/application-ui](https://tailwindui.com/components/application-ui)

---

