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
  - **Aktivní položka**: `bg-secondary-blue-pale`, `text-primary-blue`, `border-r-2 border-primary-blue`
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

Barevná paleta vychází z designového systému pro kosmetickou ERP platformu s moderním tech-forward přístupem a strategickými modrými akcenty pro logistické workflow.

### 4.1 Základní barvy

| Název              | Hex kód    | CSS Custom Property     | Použití                                    |
| ------------------ | ---------- | ----------------------- | ------------------------------------------ |
| **Primary White**  | `#FFFFFF`  | `--color-primary-white` | Čisté plochy, hlavní pozadí, kontejnery   |
| **Primary Blue**   | `#2563EB`  | `--color-primary-blue`  | Primární brand barva, CTA, navigace       |
| **Neutral Slate**  | `#0F172A`  | `--color-neutral-slate` | Primární text, vysoký kontrast            |

### 4.2 Sekundární barvy

| Název                    | Hex kód    | CSS Custom Property          | Použití                                    |
| ------------------------ | ---------- | ---------------------------- | ------------------------------------------ |
| **Secondary Blue Light** | `#3B82F6`  | `--color-secondary-blue-light` | Hover stavy, sekundární tlačítka           |
| **Secondary Blue Pale**  | `#EFF6FF`  | `--color-secondary-blue-pale`  | Jemná pozadí, vybrané stavy                |
| **Neutral Gray**         | `#64748B`  | `--color-neutral-gray`       | Sekundární text, labely                    |

### 4.3 Akcentní barvy

| Název                   | Hex kód    | CSS Custom Property         | Použití                                   |
| ----------------------- | ---------- | --------------------------- | ----------------------------------------- |
| **Accent Blue Bright**  | `#1D4ED8`  | `--color-accent-blue-bright` | Důležité akce, notifikace                |
| **Success Green**       | `#10B981`  | `--color-success`           | Potvrzené stavy, úspěšné zprávy          |
| **Warning Amber**       | `#F59E0B`  | `--color-warning`           | Výstrahy, čekající akce                  |
| **Error Red**           | `#EF4444`  | `--color-error`             | Chybové stavy, destruktivní akce         |

### 4.4 Funkční barvy

| Název              | Hex kód    | CSS Custom Property      | Použití                                |
| ------------------ | ---------- | ------------------------ | -------------------------------------- |
| **Info Blue**      | `#06B6D4`  | `--color-info`           | Informační zprávy, neutrální notifikace |
| **Border Light**   | `#E2E8F0`  | `--color-border-light`   | Jemné oddělovače, okraje kontejnerů    |
| **Background Gray** | `#F8FAFC`  | `--color-background-gray` | Pozadí stránek, separátory sekcí      |
| **Disabled Gray**  | `#94A3B8`  | `--color-disabled-gray`  | Neaktivní stavy, neaktivní prvky       |

### 4.5 Pozadí

| Název                   | Hex kód    | CSS Custom Property          | Použití                              |
| ----------------------- | ---------- | ---------------------------- | ------------------------------------ |
| **Surface White**       | `#FFFFFF`  | `--color-surface-white`      | Karty, modály, elevated plochy       |
| **Background Neutral**  | `#F1F5F9`  | `--color-background-neutral` | Hlavní pozadí aplikace               |
| **Background Subtle**   | `#F8FAFC`  | `--color-background-subtle`  | Sekundární pozadí, content oblasti   |

### 4.6 Tailwind CSS konfigurace

Pro implementaci design systému rozšiřte `tailwind.config.js`:

```javascript
module.exports = {
  theme: {
    extend: {
      colors: {
        // Primary colors
        'primary': {
          white: '#FFFFFF',
          blue: '#2563EB',
        },
        'neutral': {
          slate: '#0F172A',
          gray: '#64748B',
        },
        // Secondary colors
        'secondary': {
          'blue-light': '#3B82F6',
          'blue-pale': '#EFF6FF',
        },
        // Accent colors
        'accent': {
          'blue-bright': '#1D4ED8',
        },
        // State colors
        'success': '#10B981',
        'warning': '#F59E0B',
        'error': '#EF4444',
        'info': '#06B6D4',
        // Functional colors
        'border-light': '#E2E8F0',
        'background-gray': '#F8FAFC',
        'disabled-gray': '#94A3B8',
        // Background colors
        'surface-white': '#FFFFFF',
        'background-neutral': '#F1F5F9',
        'background-subtle': '#F8FAFC',
      },
      boxShadow: {
        'soft': '0 1px 3px rgba(0, 0, 0, 0.05)',
        'hover': '0 4px 20px rgba(0, 0, 0, 0.08)',
      },
      ringColor: {
        'primary': 'rgba(37, 99, 235, 0.3)',
      },
      fontFamily: {
        'sans': ['Inter', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
        'mono': ['JetBrains Mono', 'SF Mono', 'Consolas', 'monospace'],
      }
    }
  }
}
```

### 4.7 Použití v komponentách

| Styleguide barva     | Tailwind třída                    | Použití                          |
| -------------------- | --------------------------------- | -------------------------------- |
| Primary White        | `bg-primary-white`                | Karty, modály, čisté plochy     |
| Primary Blue         | `bg-primary-blue text-white`      | Primary buttons, aktivní navigace |
| Secondary Blue Light | `bg-secondary-blue-light`         | Hover stavy, sekundární akce     |
| Secondary Blue Pale  | `bg-secondary-blue-pale`          | Vybrané položky, jemná pozadí    |
| Neutral Slate        | `text-neutral-slate`              | Hlavní text, nadpisy             |
| Neutral Gray         | `text-neutral-gray`               | Sekundární text, labely          |
| Success Green        | `text-success bg-success/10`      | Úspěšné stavy, badges           |
| Warning Amber        | `text-warning bg-warning/10`      | Varování, upozornění            |
| Error Red            | `text-error bg-error/10`          | Chyby, urgentní stavy           |
| Info Blue            | `text-info bg-info/10`            | Informace, neutrální notifikace |

---

## 5. Typografie

### 5.1 Font systém

**Primární**: Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif  
**Monospace**: 'JetBrains Mono', 'Fira Code', Consolas, monospace (pro SKU, tracking čísla)

### 5.2 Font váhy

| Název      | Hodnota | Použití                           |
| ---------- | ------- | --------------------------------- |
| Light      | 300     | Velké nadpisy, elegantní důraz    |
| Regular    | 400     | Základní text, popisy             |
| Medium     | 500     | Podnadpisy, labely                |
| Semibold   | 600     | Sekční tituly, důležitá data      |
| Bold       | 700     | Stránkové nadpisy, kritické info  |

### 5.3 Typografická škála

| Styl       | Velikost      | Font váha | CSS/Tailwind                  | Použití              |
| ---------- | ------------- | --------- | ----------------------------- | -------------------- |
| Display    | 2rem (32px)   | Bold      | `text-2xl font-bold`          | Stránkové tituly     |
| H1         | 1.75rem (28px)| Bold      | `text-xl font-bold`           | Sekční hlavičky      |
| H2         | 1.5rem (24px) | Semibold  | `text-lg font-semibold`       | Podnadpisy sekcí     |
| H3         | 1.25rem (20px)| Semibold  | `text-base font-semibold`     | Tituly karet         |
| H4         | 1.125rem (18px)| Medium   | `text-sm font-medium`         | Hlavičky tabulek     |
| Body Large | 1rem (16px)   | Regular   | `text-base font-normal`       | Primární obsah       |
| Body       | 0.875rem (14px)| Regular  | `text-sm font-normal`         | Sekundární obsah     |
| Body Small | 0.75rem (12px)| Medium    | `text-xs font-medium`         | Titulky, metadata    |

### 5.4 Barva textu

| Úroveň      | Hex kód    | CSS Custom Property | Tailwind ekvivalent    | Použití                |
| ----------- | ---------- | ------------------- | ---------------------- | ---------------------- |
| Primary     | `#0F172A`  | `--text-primary`    | `text-neutral-slate`   | Hlavní text, nadpisy   |
| Secondary   | `#64748B`  | `--text-secondary`  | `text-neutral-gray`    | Podpůrný text, labely  |
| Muted       | `#94A3B8`  | `--text-muted`      | `text-disabled-gray`   | Metadata, popisky      |
| Disabled    | `#CBD5E1`  | `--text-disabled`   | `text-gray-300`        | Neaktivní prvky        |

---

## 6. Komponenty

### 6.1 Buttons

#### Primary Button
```html
<button class="bg-primary-blue hover:bg-accent-blue-bright active:bg-accent-blue-bright/90 text-white px-6 py-3 rounded-lg font-semibold text-sm transition-all duration-150 ease-in-out">
  Primary Action
</button>
```
- **Background**: `#2563EB` primary, `#1D4ED8` hover
- **Padding**: `px-6 py-3` (24px / 12px)
- **Border Radius**: `rounded-lg` (8px)
- **Font Weight**: 600 (semibold)
- **Transition**: `transition-all duration-150`

#### Secondary Button
```html
<button class="bg-transparent text-primary-blue border border-border-light hover:bg-secondary-blue-pale px-6 py-3 rounded-lg font-semibold text-sm transition-all duration-150">
  Secondary Action
</button>
```
- **Background**: Transparent with border `#E2E8F0`
- **Text Color**: `#2563EB`
- **Hover**: Light blue background `#EFF6FF`

#### Ghost Button
```html
<button class="bg-transparent text-neutral-gray hover:bg-secondary-blue-pale px-4 py-2 rounded-md font-semibold text-sm transition-all duration-150">
  Ghost Action
</button>
```
- **Background**: Transparent
- **Text Color**: `#64748B`
- **Padding**: `px-4 py-2` (16px / 8px)
- **Border Radius**: `rounded-md` (6px)

### 6.2 Formulářové prvky

#### Input Styling
```html
<input class="w-full bg-primary-white border border-border-light rounded-lg px-4 py-3 text-sm text-neutral-slate placeholder-neutral-gray focus:outline-none focus:border-primary-blue focus:ring-2 focus:ring-primary-blue/10 transition-colors duration-150">
```
- **Background**: `#FFFFFF`
- **Border**: `#E2E8F0` default, `#2563EB` focus
- **Padding**: `px-4 py-3` (16px / 12px)
- **Focus Ring**: 3px shadow with `rgba(37, 99, 235, 0.1)`
- **Error state**: `border-error focus:border-error focus:ring-error/10`

### 6.3 Cards

#### Card Styling
```html
<div class="bg-primary-white border border-border-light rounded-xl shadow-soft hover:shadow-hover p-6 transition-shadow duration-200">
  <!-- Card content -->
</div>
```
- **Background**: `#FFFFFF`
- **Border**: `#E2E8F0`
- **Border Radius**: 12px
- **Padding**: 24px
- **Shadow**: `0 1px 3px rgba(0, 0, 0, 0.05)`
- **Hover Shadow**: `0 4px 20px rgba(0, 0, 0, 0.08)`
- **Transition**: `transition-shadow duration-200`

### 6.3 Tabulky

Tabulky jsou určeny pro přehledné zobrazení řádkových dat. Inspirací je layout z dashboardu s oddělenými řádky, akcemi a vizuálně odlišeným stavem záznamu.

#### Vzhled

```html
<table class="min-w-full divide-y divide-gray-200">
  <thead class="bg-gray-50">
    <tr>
      <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
        Název
      </th>
    </tr>
  </thead>
  <tbody class="bg-white divide-y divide-gray-200">
    <tr class="hover:bg-gray-50 transition-colors duration-150">
      <td class="px-6 py-4 whitespace-nowrap text-sm text-charcoal">
        <!-- Content -->
      </td>
    </tr>
  </tbody>
</table>
```

#### Interaktivní buňky

```html
<td class="px-6 py-4 whitespace-nowrap">
  <a href="#" class="text-brand hover:text-brand-dark font-medium text-sm transition-colors duration-150">
    Klikatelný prvek
  </a>
</td>
```

#### Stavové značky (status badges)

```html
<!-- Success -->
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-success/10 text-success">
  Dokončeno
</span>

<!-- Warning -->
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-warning/10 text-warning">
  Čeká na schválení
</span>

<!-- Error -->
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-error/10 text-error">
  Chyba
</span>

<!-- Neutral -->
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
  Koncept
</span>
```

---

## 7. Interakce a stavy

### 7.1 Hover stavy
```css
/* Řádek tabulky */
.hover\:bg-gray-50:hover { background-color: #F8FAFC; }

/* Tlačítko */
.hover\:bg-accent-blue-bright:hover { background-color: #1D4ED8; }

/* Link */
.hover\:text-accent-blue-bright:hover { color: #1D4ED8; }
```

### 7.2 Focus stavy
```css
/* Input focus */
.focus\:border-primary-blue:focus { border-color: #2563EB; }
.focus\:ring-2:focus { box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1); }

/* Button focus */
.focus\:outline-none:focus { outline: none; }
.focus\:ring-2:focus { box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1); }
```

### 7.3 Aktivní stavy

#### Sidebar navigace
```html
<a class="bg-secondary-blue-pale text-primary-blue border-r-2 border-primary-blue px-4 py-2 flex items-center">
  <span>Aktivní položka</span>
</a>
```

### 7.4 Disabled stavy
```html
<button class="bg-gray-100 text-gray-400 cursor-not-allowed opacity-50 px-6 py-3 rounded-lg" disabled>
  Neaktivní tlačítko
</button>
```

### 7.5 Error stavy
```html
<div>
  <input class="border-error focus:border-error focus:ring-error/30 text-error" />
  <p class="mt-1 text-sm text-error">Toto pole je povinné</p>
</div>
```

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

## 11. Spacing systém

### 11.1 Spacing škála

Založeno na 4px gridu pro perfektní zarovnání:

| Název | Hodnota | Tailwind | Použití                      |
| ----- | ------- | -------- | ---------------------------- |
| xs    | 4px     | `1`      | Icon padding, těsné spacing  |
| sm    | 8px     | `2`      | Element spacing uvnitř komponent |
| md    | 16px    | `4`      | Komponenty interní spacing   |
| lg    | 24px    | `6`      | Sekce spacing, card padding  |
| xl    | 32px    | `8`      | Velká sekce separace         |
| 2xl   | 48px    | `12`     | Page-level spacing           |
| 3xl   | 64px    | `16`     | Hero sekce, hlavní přestávky |

## 12. Animace a motion systém

### 12.1 Transition Properties

#### Duration
- **Micro**: 150ms (button hover, input focus)
- **Standard**: 250ms (card hover, modal open)
- **Complex**: 350ms (page transitions, drawer slide)

#### Easing
- **Ease Out**: `cubic-bezier(0.25, 0.46, 0.45, 0.94)` (výchozí)
- **Ease In Out**: `cubic-bezier(0.4, 0, 0.2, 1)` (modal, drawer)
- **Bounce**: `cubic-bezier(0.68, -0.55, 0.265, 1.55)` (success stavy)

### 12.2 Specifické animace

- **Button Press**: Scale 0.98 s 100ms ease-out
- **Card Hover**: Translate Y -2px se zvýšením stínu
- **Loading States**: Pulse opacity 0.5 to 1 over 1s infinite
- **Page Transitions**: Slide in from right translateX(100%) to 0
- **Sidebar collapse/expand**: `transition-all duration-300 ease-in-out`

### 12.3 Implementace s Tailwind

```css
/* Micro transitions */
.btn-micro { transition: all 150ms cubic-bezier(0.25, 0.46, 0.45, 0.94); }

/* Standard transitions */  
.card-hover { transition: all 250ms cubic-bezier(0.25, 0.46, 0.45, 0.94); }

/* Complex transitions */
.page-transition { transition: all 350ms cubic-bezier(0.4, 0, 0.2, 1); }
```

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
- **Avatar**: Circular avatar s iniciálami uživatele, `bg-primary-blue` pozadí
- **Role badges**: Zobrazení uživatelských rolí jako `bg-secondary-blue-pale text-primary-blue` značky
- **Dropdown menu**: Vyskakovací menu s uživatelskými údaji a logout funkcí
- **Responsive**: Přizpůsobuje se kontextu (sidebar collapsed/expanded)

### 12.3 Lokalizace

- **Čeština jako primární**: Všechny UI texty v českém jazyce
- **Loading stavy**: "Přihlašování...", "Kontrola přihlášení..."
- **i18next framework**: Připraveno pro rozšíření o další jazyky
- **Konzistentní terminologie**: Jednotná terminologie napříč aplikací

## 13. Accessibility standardy

### 13.1 Kontrast a čitelnost

- **Kontrastní poměry**: Minimálně 4.5:1 pro normální text, 3:1 pro velký text
- **Focus indikátory**: 3px ring s blue barvě (`rgba(37, 99, 235, 0.1)`)
- **Touch targets**: Minimálně 44px pro mobilní interakce
- **Semantic HTML**: Správné HTML s ARIA labely

### 13.2 Keyboard Navigation

- **Plný tab order**: Všechny interaktivní prvky dostupné přes klávesnici
- **Visible focus states**: Jasné focus indikátory pro navigaci
- **Escape klávesa**: Zavření modálů a dropdownů
- **Enter/Space**: Aktivace tlačítek a interaktivních prvků

### 13.3 Screen Reader Support

- **ARIA labely**: Popisné labely pro složité komponenty
- **Role attributes**: Správné sémantické role
- **Live regions**: Oznámení změn stavu
- **Alt texty**: Popisné texty pro ikony a obrázky

### 13.4 Accessibility patterns

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

