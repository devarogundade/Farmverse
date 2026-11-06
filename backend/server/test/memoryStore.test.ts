import { MemoryStore } from '../../functions/src/infra/memoryStore';
import { storeConformance } from './storeConformance';

storeConformance('MemoryStore', async () => new MemoryStore());
