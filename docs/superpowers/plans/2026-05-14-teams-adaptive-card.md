# Teams Adaptive Card Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Přepsat Teams notifikaci z legacy `MessageCard` formátu na `Adaptive Card`, aby bylo možné nastavit větší písmo (`size: "medium"`) u textu položek changelogu.

**Architecture:** Dvě úpravy v jediném souboru `.github/workflows/ci-main-branch.yml`. Krok 1 upraví formátování markdown nadpisů (### → **bold**) kvůli omezením Adaptive Card TextBlock. Krok 2 přepíše jq blok generující Teams payload z MessageCard na Adaptive Card strukturu s nastavením velikosti textu.

**Tech Stack:** GitHub Actions YAML, jq, Microsoft Teams Incoming Webhook (Adaptive Card v1.4)

---

### Task 1: Upravit markdown nadpisy v changelog extractu

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:330-338`

Adaptive Card TextBlock podporuje `**bold**` markdown, ale `## Header` renderuje doslova jako text. Proto nahradíme `"## ..."` za `"**...**"`.

- [ ] **Step 1: Upravit jq řetězce nadpisů**

V souboru `.github/workflows/ci-main-branch.yml` najdi blok na řádcích 330–338 (step `📝 Extract Czech changelog for current version`) a nahraď:

```yaml
                    if .[0].type == "feature" then "## ✨ Novinky"
                    elif .[0].type == "fix" then "## 🐛 Opravy"
                    elif .[0].type == "improvement" then "## 📈 Vylepšení"
                    elif .[0].type == "docs" then "## 📚 Dokumentace"
                    elif .[0].type == "perf" then "## ⚡ Zrychlení"
                    elif .[0].type == "refactor" then "## ♻️ Refaktoring"
                    elif .[0].type == "test" then "## 🧪 Interní"
                    elif .[0].type == "ci" then "## 👷 CI/CD"
                    else "## 📝 Ostatní"
```

za:

```yaml
                    if .[0].type == "feature" then "**✨ Novinky**"
                    elif .[0].type == "fix" then "**🐛 Opravy**"
                    elif .[0].type == "improvement" then "**📈 Vylepšení**"
                    elif .[0].type == "docs" then "**📚 Dokumentace**"
                    elif .[0].type == "perf" then "**⚡ Zrychlení**"
                    elif .[0].type == "refactor" then "**♻️ Refaktoring**"
                    elif .[0].type == "test" then "**🧪 Interní**"
                    elif .[0].type == "ci" then "**👷 CI/CD**"
                    else "**📝 Ostatní**"
```

- [ ] **Step 2: Ověřit YAML syntaxi**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci-main-branch.yml'))" && echo "OK"
```

Očekávaný výstup: `OK`

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "feat(ci): use bold text for changelog section headers in Teams card"
```

---

### Task 2: Přepsat Teams payload z MessageCard na Adaptive Card

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:555-595`

Nahradíme celý `TEAMS_MESSAGE=$(jq -n ...)` blok. Nový formát používá `type: message` s přílohami typu `application/vnd.microsoft.card.adaptive`. Adaptive Card v1.4 umožňuje nastavit `"size": "medium"` na TextBlock.

- [ ] **Step 1: Nahradit TEAMS_MESSAGE jq blok**

V souboru `.github/workflows/ci-main-branch.yml` nahraď celý blok od `TEAMS_MESSAGE=$(jq -n \` (řádek 555) po uzavírací `}')` (řádek 595) tímto:

```yaml
          TEAMS_MESSAGE=$(jq -n \
            --arg version "$VERSION" \
            --arg timestamp "$TIMESTAMP" \
            --arg app_url "$APP_URL" \
            --arg run_url "$RUN_URL" \
            --arg changelog "$CHANGELOG_CS" \
            '{
              "type": "message",
              "attachments": [
                {
                  "contentType": "application/vnd.microsoft.card.adaptive",
                  "contentUrl": null,
                  "content": {
                    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                    "type": "AdaptiveCard",
                    "version": "1.4",
                    "body": (
                      [
                        {
                          "type": "TextBlock",
                          "text": "🚀 Heblo \($version) úspěšně nasazeno",
                          "size": "extraLarge",
                          "weight": "bolder",
                          "color": "good",
                          "wrap": true
                        },
                        {
                          "type": "FactSet",
                          "facts": [
                            { "title": "Verze:",     "value": $version },
                            { "title": "Prostředí:", "value": "Production" },
                            { "title": "URL:",       "value": $app_url },
                            { "title": "Čas:",       "value": $timestamp }
                          ]
                        }
                      ]
                      + (
                        if ($changelog | length) > 0
                        then [
                          {
                            "type": "TextBlock",
                            "text": "📋 Změny ve verzi",
                            "size": "large",
                            "weight": "bolder",
                            "separator": true,
                            "spacing": "medium"
                          },
                          {
                            "type": "TextBlock",
                            "text": $changelog,
                            "size": "medium",
                            "wrap": true
                          }
                        ]
                        else []
                        end
                      )
                    ),
                    "actions": [
                      {
                        "type": "Action.OpenUrl",
                        "title": "Zobrazit aplikaci",
                        "url": $app_url
                      },
                      {
                        "type": "Action.OpenUrl",
                        "title": "Zobrazit workflow run",
                        "url": $run_url
                      }
                    ]
                  }
                }
              ]
            }')
```

Zároveň odstraň `--arg workflow "$GITHUB_WORKFLOW" \` z jq argumentů (workflow se už v payloadu nepoužívá).

- [ ] **Step 2: Odebrat nepotřebnou env proměnnou GITHUB_WORKFLOW**

V `env:` bloku stepu (řádky 608–613) odstraň řádek:
```yaml
          GITHUB_WORKFLOW: ${{ github.workflow }}
```

- [ ] **Step 3: Ověřit YAML syntaxi**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci-main-branch.yml'))" && echo "OK"
```

Očekávaný výstup: `OK`

- [ ] **Step 4: Ověřit že jq payload je validní JSON**

```bash
jq -n \
  --arg version "v1.2.3" \
  --arg timestamp "2026-05-14 10:00:00 UTC" \
  --arg app_url "https://heblo.anela.cz" \
  --arg run_url "https://github.com/example/actions/runs/123" \
  --arg changelog "**✨ Novinky**\n- **Výroba:** Nová funkce" \
  '{
    "type": "message",
    "attachments": [
      {
        "contentType": "application/vnd.microsoft.card.adaptive",
        "contentUrl": null,
        "content": {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.4",
          "body": (
            [
              {
                "type": "TextBlock",
                "text": "🚀 Heblo \($version) úspěšně nasazeno",
                "size": "extraLarge",
                "weight": "bolder",
                "color": "good",
                "wrap": true
              },
              {
                "type": "FactSet",
                "facts": [
                  { "title": "Verze:",     "value": $version },
                  { "title": "Prostředí:", "value": "Production" },
                  { "title": "URL:",       "value": $app_url },
                  { "title": "Čas:",       "value": $timestamp }
                ]
              }
            ]
            + (
              if ($changelog | length) > 0
              then [
                {
                  "type": "TextBlock",
                  "text": "📋 Změny ve verzi",
                  "size": "large",
                  "weight": "bolder",
                  "separator": true,
                  "spacing": "medium"
                },
                {
                  "type": "TextBlock",
                  "text": $changelog,
                  "size": "medium",
                  "wrap": true
                }
              ]
              else []
              end
            )
          ),
          "actions": [
            {
              "type": "Action.OpenUrl",
              "title": "Zobrazit aplikaci",
              "url": $app_url
            },
            {
              "type": "Action.OpenUrl",
              "title": "Zobrazit workflow run",
              "url": $run_url
            }
          ]
        }
      }
    ]
  }' > /dev/null && echo "JSON OK"
```

Očekávaný výstup: `JSON OK`

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "feat(ci): switch Teams notification from MessageCard to Adaptive Card"
```
