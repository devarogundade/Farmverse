import { MemoryStore } from '../../game/src/infra/memoryStore';
import { storeConformance } from './storeConformance';

storeConformance('MemoryStore', async () => new MemoryStore());
