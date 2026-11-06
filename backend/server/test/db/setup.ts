import 'dotenv/config';
import { PrismaPg } from '@prisma/adapter-pg';
import { PrismaClient } from '../../src/generated/prisma/client';

/** A client for the test database (DATABASE_URL; the local container by default). */
export function testPrisma(): PrismaClient {
  const url = process.env['DATABASE_URL'];
  if (!url) throw new Error('DATABASE_URL is not set (copy .env.example to .env)');
  return new PrismaClient({ adapter: new PrismaPg({ connectionString: url }) });
}

export async function resetDatabase(prisma: PrismaClient): Promise<void> {
  await prisma.$executeRawUnsafe('TRUNCATE documents, refresh_tokens, accounts CASCADE');
}
