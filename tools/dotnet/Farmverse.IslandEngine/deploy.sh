#!/usr/bin/env bash
# Deploys the island engine to Cloud Run (private: only the backend's service account may call it).
#   tools/dotnet/Farmverse.IslandEngine/deploy.sh [project] [region]
set -euo pipefail
PROJECT="${1:-farmverse-8deb6}"
REGION="${2:-europe-west1}"
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
CTX="$(mktemp -d)"
trap 'rm -rf "$CTX"' EXIT

# the build context: only what the engine compiles, in the repo's layout
mkdir -p "$CTX/Assets/Farmverse/Runtime" "$CTX/tools/dotnet"
for d in Core Contracts Domain; do cp -R "$ROOT/Assets/Farmverse/Runtime/$d" "$CTX/Assets/Farmverse/Runtime/"; done
for f in Directory.Build.props NuGet.config; do cp "$ROOT/tools/dotnet/$f" "$CTX/tools/dotnet/"; done
for d in Farmverse.Core Farmverse.Contracts Farmverse.Domain Farmverse.IslandEngine; do cp -R "$ROOT/tools/dotnet/$d" "$CTX/tools/dotnet/"; done
find "$CTX" -name '*.meta' -delete
find "$CTX" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
cp "$ROOT/tools/dotnet/Farmverse.IslandEngine/Dockerfile" "$CTX/Dockerfile"

gcloud run deploy island-engine \
  --project "$PROJECT" --region "$REGION" \
  --source "$CTX" \
  --no-allow-unauthenticated \
  --min-instances 1 --max-instances 20 \
  --cpu 1 --memory 512Mi --concurrency 40 \
  --set-env-vars ASPNETCORE_ENVIRONMENT=Production

echo "Grant the backend's runtime service account roles/run.invoker on island-engine, e.g.:"
echo "  gcloud run services add-iam-policy-binding island-engine --project $PROJECT --region $REGION \\"
echo "    --member serviceAccount:$PROJECT@appspot.gserviceaccount.com --role roles/run.invoker"
