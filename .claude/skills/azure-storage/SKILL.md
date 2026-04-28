---
name: azure-storage
description: Manage Azure Blob Storage and Azure Storage Queues for AgentHarness. Use for initial setup, inspecting pipeline artifacts, debugging queue messages, and managing dead-letter queues. Trigger on: "setup azure", "create queues", "list blobs", "check queue", "dead letter", "inspect artifact", "azure storage".
---

You manage Azure Blob Storage and Azure Storage Queues using the `az` CLI.

The connection string is read from the `.env` file in the project root. Before running any `az storage` command, load the `.env` file:

```bash
set -a && source .env && set +a
```

Always pass `--connection-string "$AZURE_STORAGE_CONNECTION_STRING"` to every `az storage` command.

---

## Pipeline config

`.pipeline/config.json` controls the storage account, container name, and pipeline behaviour. Edit it directly or use the snippets below.

### Show current config
```bash
cat .pipeline/config.json
```

### Change the blob container name
```bash
python3 -c "
import json, pathlib
p = pathlib.Path('.pipeline/config.json')
cfg = json.loads(p.read_text())
cfg['storage']['container'] = 'YOUR_CONTAINER_NAME'
p.write_text(json.dumps(cfg, indent=2))
"
```

### Adjust pipeline timeouts / limits
```bash
python3 -c "
import json, pathlib
p = pathlib.Path('.pipeline/config.json')
cfg = json.loads(p.read_text())
cfg['defaults']['dead_letter_threshold'] = 3   # retries before dead-letter
cfg['defaults']['max_revisions'] = 3           # review→dev revision rounds
cfg['defaults']['poll_interval_seconds'] = 1.0 # worker poll cadence
p.write_text(json.dumps(cfg, indent=2))
"
```

> The `connection_string_env` key is the **name** of the env var (`AZURE_STORAGE_CONNECTION_STRING`), not the value — set the actual value in `.env`.

---

## Initial setup

Create the blob container and all pipeline queues:

```bash
# Blob container
az storage container create \
  --name pipeline-artifacts \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"

# Queues
for q in planner-queue architect-queue designer-queue developer-queue review-queue; do
  az storage queue create --name "$q" \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
done
```

---

## Blob Storage

### List all features (top-level artifact folders)
```bash
az storage blob list \
  --container-name pipeline-artifacts \
  --prefix "artifacts/" \
  --delimiter "/" \
  --query "[].name" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  -o tsv
```

### List artifacts for a specific feature
```bash
az storage blob list \
  --container-name pipeline-artifacts \
  --prefix "artifacts/{feature_id}/" \
  --query "[].{name:name, size:properties.contentLength, modified:properties.lastModified}" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  -o table
```

### Download and display an artifact
```bash
az storage blob download \
  --container-name pipeline-artifacts \
  --name "artifacts/{feature_id}/brief.md" \
  --file /dev/stdout \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  2>/dev/null
```

### Download state.json for a feature
```bash
az storage blob download \
  --container-name pipeline-artifacts \
  --name "artifacts/{feature_id}/state.json" \
  --file /dev/stdout \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  2>/dev/null | python3 -m json.tool
```

### Upload a file
```bash
az storage blob upload \
  --container-name pipeline-artifacts \
  --name "artifacts/{feature_id}/brief.md" \
  --file ./brief.md \
  --overwrite \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

### Delete all artifacts for a feature
```bash
az storage blob delete-batch \
  --source pipeline-artifacts \
  --pattern "artifacts/{feature_id}/*" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

---

## Storage Queues

### Check message count on all queues
```bash
for q in planner-queue architect-queue designer-queue developer-queue review-queue; do
  count=$(az storage queue show --name "$q" \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --query "approximateMessageCount" -o tsv 2>/dev/null || echo "?")
  echo "$q: $count"
done
```

### Peek at messages in a queue (non-destructive)
```bash
az storage message peek \
  --queue-name {queue_name} \
  --num-messages 5 \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  -o table
```

### Decode a queue message (messages are base64-encoded JSON)
```bash
az storage message peek \
  --queue-name {queue_name} \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --query "[0].content" -o tsv | base64 -d | python3 -m json.tool
```

### Clear all messages from a queue
```bash
az storage message clear \
  --queue-name {queue_name} \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

### Manually enqueue a message (for testing)
```bash
az storage message put \
  --queue-name planner-queue \
  --content "$(echo '{
    "feature_id": "feat-test",
    "task_id": "feat-test-planning-1",
    "input_artifacts": ["artifacts/feat-test/brief.md"],
    "output_artifact": "artifacts/feat-test/spec.r1.md",
    "agent_role": "planner",
    "context": null,
    "revision": 1,
    "review_feedback": null
  }' | base64)" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

---

## Dead-letter queues

Dead-letter queues are named `{queue-name}-poison`.

### Check dead-letter queues for failed tasks
```bash
for q in planner-queue-poison architect-queue-poison designer-queue-poison developer-queue-poison review-queue-poison; do
  count=$(az storage queue show --name "$q" \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --query "approximateMessageCount" -o tsv 2>/dev/null)
  [ -n "$count" ] && [ "$count" -gt 0 ] && echo "$q: $count failed messages"
done
```

### Inspect a dead-letter message
```bash
az storage message peek \
  --queue-name {queue_name}-poison \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --query "[0].content" -o tsv | base64 -d | python3 -m json.tool
```

### Re-queue a dead-letter message (after fixing the issue)
```bash
# 1. Get the message
MSG=$(az storage message get \
  --queue-name {queue_name}-poison \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --query "[0]" -o json)

CONTENT=$(echo "$MSG" | python3 -c "import sys,json; print(json.load(sys.stdin)['content'])")
ID=$(echo "$MSG" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
RECEIPT=$(echo "$MSG" | python3 -c "import sys,json; print(json.load(sys.stdin)['popReceipt'])")

# 2. Put it back in the original queue
az storage message put \
  --queue-name {queue_name} \
  --content "$CONTENT" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"

# 3. Delete from dead-letter
az storage message delete \
  --queue-name {queue_name}-poison \
  --id "$ID" \
  --pop-receipt "$RECEIPT" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

---

## Common patterns

### Full pipeline health check
```bash
echo "=== Queues ===" && \
for q in planner-queue architect-queue designer-queue developer-queue review-queue; do
  count=$(az storage queue show --name "$q" \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --query "approximateMessageCount" -o tsv 2>/dev/null || echo "MISSING")
  echo "  $q: $count"
done && \
echo "=== Dead-letter ===" && \
for q in planner-queue architect-queue designer-queue developer-queue review-queue; do
  count=$(az storage queue show --name "${q}-poison" \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --query "approximateMessageCount" -o tsv 2>/dev/null || echo "0")
  [ "$count" != "0" ] && echo "  ${q}-poison: $count FAILED MESSAGES" || true
done

echo "=== Active features ===" && \
az storage blob list \
  --container-name pipeline-artifacts \
  --prefix "artifacts/" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
  --query "[?ends_with(name, '/state.json')].name" \
  -o tsv | sed 's|artifacts/||;s|/state.json||'
```
