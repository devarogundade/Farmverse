import 'dotenv/config';
import { cloudLogger } from '../../functions/src/core/logger';
import { buildApp } from './app';
import { loadConfig } from './config';
import { LiveHub } from './live';
import { buildServices } from './runtime';

/** The HTTP/WebSocket server process. Jobs run in worker.ts (scale them separately). */
async function main(): Promise<void> {
  const config = loadConfig();
  const log = cloudLogger.with({ service: 'farmverse-server', env: config.FARMVERSE_ENV });
  const services = buildServices(config, log);
  const hub = new LiveHub(services.reader, (t) => services.sessions.verify(t), log);
  await hub.start(services.bus);

  const app = await buildApp({
    rt: services.rt,
    sessions: services.sessions,
    reader: services.reader,
    hub,
    limiter: services.limiter,
    callDeps: {
      clock: services.rt.clock,
      log,
      minClientVersion: () => config.MIN_CLIENT_VERSION,
      exposeIssues: config.FARMVERSE_ENV !== 'prod',
    },
    allowedOrigins: config.ALLOWED_ORIGINS,
    minClientVersion: config.MIN_CLIENT_VERSION,
    environment: config.FARMVERSE_ENV,
  });

  const shutdown = async (signal: string) => {
    log.info('shutting down', { signal });
    await hub.stop();
    await app.close();
    await services.close();
    process.exit(0);
  };
  process.on('SIGTERM', () => void shutdown('SIGTERM'));
  process.on('SIGINT', () => void shutdown('SIGINT'));

  await app.listen({ host: '0.0.0.0', port: config.PORT });
  log.info('listening', { port: config.PORT });
}

main().catch((error: unknown) => {
  cloudLogger.error('server failed to start', { error: String(error), stack: (error as Error).stack });
  process.exit(1);
});
