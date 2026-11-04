import { defineString } from 'firebase-functions/params';
import { httpEngine, type IslandEngine } from '../infra/islandEngine';

/** The Cloud Run island engine's URL (tools/dotnet/Farmverse.IslandEngine/deploy.sh prints it). */
const ISLAND_ENGINE_URL = defineString('ISLAND_ENGINE_URL', { default: 'http://localhost:8080' });

let engine: IslandEngine | undefined;
export const islandEngine = () => (engine ??= httpEngine(ISLAND_ENGINE_URL.value()));
