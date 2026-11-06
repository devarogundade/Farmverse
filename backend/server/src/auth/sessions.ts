import { createHash, createPrivateKey, createPublicKey, randomBytes, randomUUID, type KeyObject } from 'node:crypto';
import { SignJWT, jwtVerify } from 'jose';
import { AppError } from '../../../game/src/core/errors';
import type { PrismaClient } from '../generated/prisma/client';
import type { NearRpc } from './nearRpc';
import { ACCOUNT_ID, verifyNep413 } from './nep413';

export const SIGN_IN_MESSAGE = 'Sign in to Farmverse';
export const ACCESS_TTL_SEC = 15 * 60;
export const REFRESH_TTL_MS = 30 * 24 * 60 * 60 * 1000;
export const NONCE_TTL_SEC = 300;
const ISSUER = 'farmverse';
const AUDIENCE = 'farmverse-game';

/** One-time sign-in nonces (Redis in production). */
export interface NonceStore {
  put(nonce: string, ttlSec: number): Promise<void>;
  /** Removes the nonce; true only the first time, and only before it expires. */
  take(nonce: string): Promise<boolean>;
}

export interface Caller {
  /** The game uid (opaque, alphanumeric): what documents and calls use. */
  readonly uid: string;
  /** The NEAR account behind it: what chain features use. */
  readonly accountId: string;
  readonly isAdmin: boolean;
}

export interface SessionTokens {
  readonly accountId: string;
  readonly uid: string;
  readonly accessToken: string;
  readonly accessExpiresAt: number;
  readonly refreshToken: string;
}

export interface SessionsConfig {
  readonly jwtSeed: Buffer;
  readonly recipient: string;
  readonly network: 'testnet' | 'mainnet';
  readonly adminAccounts: readonly string[];
  /** Local development only: sign in as any account without a wallet (the Unity editor has none). */
  readonly allowDevSignIn?: boolean;
}

const PKCS8_ED25519 = Buffer.from('302e020100300506032b657004220420', 'hex');
const hash = (token: string) => createHash('sha256').update(token).digest('hex');

/**
 * The game uid of a wallet. NEAR account ids contain dots, which the contract's uid format and
 * document paths don't allow, so each account gets a stable opaque id: `u` + 39 hex characters of
 * sha256(account id) (156 bits — collisions are not a practical concern).
 */
export function uidFor(accountId: string): string {
  return `u${createHash('sha256').update(accountId).digest('hex').slice(0, 39)}`;
}

/** Testnet sign-ins use `.testnet` names, mainnet `.near`; implicit (64-hex) accounts work on both. */
export function accountOnNetwork(accountId: string, network: 'testnet' | 'mainnet'): boolean {
  if (!ACCOUNT_ID.test(accountId)) return false;
  if (/^[0-9a-f]{64}$/.test(accountId)) return true;
  return network === 'testnet' ? accountId.endsWith('.testnet') : accountId.endsWith('.near');
}

export class Sessions {
  private readonly privateKey: KeyObject;
  private readonly publicKey: KeyObject;

  constructor(
    private readonly prisma: PrismaClient,
    private readonly nonces: NonceStore,
    private readonly rpc: NearRpc,
    private readonly config: SessionsConfig,
    private readonly now: () => number = Date.now,
  ) {
    if (config.jwtSeed.length !== 32) throw new Error('JWT seed must be 32 bytes');
    this.privateKey = createPrivateKey({ key: Buffer.concat([PKCS8_ED25519, config.jwtSeed]), format: 'der', type: 'pkcs8' });
    this.publicKey = createPublicKey(this.privateKey);
  }

  /** A fresh nonce for the wallet to sign, with the message and recipient it must use. */
  async issueNonce(): Promise<{ nonce: string; message: string; recipient: string; expiresInSec: number }> {
    const nonce = randomBytes(32).toString('base64');
    await this.nonces.put(nonce, NONCE_TTL_SEC);
    return { nonce, message: SIGN_IN_MESSAGE, recipient: this.config.recipient, expiresInSec: NONCE_TTL_SEC };
  }

  /** Signs in with a NEP-413 signature over a nonce from {@link issueNonce}. */
  async signIn(input: { accountId: string; publicKey: string; signature: string; nonce: string; userAgent?: string }): Promise<SessionTokens> {
    const { accountId, publicKey, signature, nonce } = input;
    if (!accountOnNetwork(accountId, this.config.network)) throw new AppError('INVALID_ARGUMENT');
    const nonceBytes = Buffer.from(nonce, 'base64');
    if (nonceBytes.length !== 32) throw new AppError('INVALID_ARGUMENT');
    // Burn the nonce before anything slow, so a signature can never be used twice.
    if (!(await this.nonces.take(nonce))) throw new AppError('UNAUTHENTICATED');
    const valid = verifyNep413(
      { accountId, publicKey, signature },
      { message: SIGN_IN_MESSAGE, nonce: nonceBytes, recipient: this.config.recipient, callbackUrl: null },
    );
    if (!valid) throw new AppError('UNAUTHENTICATED');
    if (!(await this.rpc.isFullAccessKey(accountId, publicKey))) throw new AppError('UNAUTHENTICATED');

    const account = await this.prisma.account.upsert({
      where: { accountId },
      create: { accountId, uid: uidFor(accountId), lastKey: publicKey },
      update: { lastLoginAt: new Date(this.now()), lastKey: publicKey },
    });
    if (account.bannedAt) throw new AppError('PERMISSION_DENIED');
    return this.issue(accountId, randomUUID(), input.userAgent);
  }

  /**
   * Development sign-in: no signature, any account on the network. Refused unless the server was
   * started with FARMVERSE_ENV=dev (never staging or prod), so the editor can play locally.
   */
  async devSignIn(accountId: string, userAgent?: string): Promise<SessionTokens> {
    if (!this.config.allowDevSignIn) throw new AppError('PERMISSION_DENIED');
    if (!accountOnNetwork(accountId, this.config.network)) throw new AppError('INVALID_ARGUMENT');
    const account = await this.prisma.account.upsert({
      where: { accountId },
      create: { accountId, uid: uidFor(accountId), lastKey: 'dev' },
      update: { lastLoginAt: new Date(this.now()), lastKey: 'dev' },
    });
    if (account.bannedAt) throw new AppError('PERMISSION_DENIED');
    return this.issue(accountId, randomUUID(), userAgent);
  }

  /** Swaps a refresh token for new tokens. Presenting a rotated token revokes the whole sign-in. */
  async refresh(refreshToken: string, userAgent?: string): Promise<SessionTokens> {
    const row = await this.prisma.refreshToken.findUnique({
      where: { tokenHash: hash(refreshToken) },
      include: { account: { select: { bannedAt: true } } },
    });
    if (!row) throw new AppError('UNAUTHENTICATED');
    if (row.account.bannedAt) throw new AppError('PERMISSION_DENIED');
    if (row.revokedAt) {
      await this.revokeFamily(row.family);
      throw new AppError('UNAUTHENTICATED');
    }
    if (row.expiresAt.getTime() <= this.now()) throw new AppError('UNAUTHENTICATED');
    const claimed = await this.prisma.refreshToken.updateMany({
      where: { id: row.id, revokedAt: null },
      data: { revokedAt: new Date(this.now()) },
    });
    if (claimed.count !== 1) {
      await this.revokeFamily(row.family);
      throw new AppError('UNAUTHENTICATED');
    }
    return this.issue(row.accountId, row.family, userAgent);
  }

  async signOut(refreshToken: string): Promise<void> {
    const row = await this.prisma.refreshToken.findUnique({ where: { tokenHash: hash(refreshToken) } });
    if (row) await this.revokeFamily(row.family);
  }

  /** Ends every session of the wallet behind a game uid (account deletion). */
  async signOutEverywhere(uid: string): Promise<void> {
    await this.prisma.refreshToken.updateMany({ where: { account: { uid }, revokedAt: null }, data: { revokedAt: new Date(this.now()) } });
  }

  /** The caller behind an access token, or null when it is missing, forged or expired. */
  async verify(accessToken: string | undefined): Promise<Caller | null> {
    if (!accessToken) return null;
    try {
      const { payload } = await jwtVerify(accessToken, this.publicKey, {
        issuer: ISSUER,
        audience: AUDIENCE,
        algorithms: ['EdDSA'],
        currentDate: new Date(this.now()),
      });
      const accountId = payload['acc'];
      if (typeof payload.sub !== 'string' || typeof accountId !== 'string') return null;
      return { uid: payload.sub, accountId, isAdmin: this.config.adminAccounts.includes(accountId) };
    } catch {
      return null;
    }
  }

  private async issue(accountId: string, family: string, userAgent?: string): Promise<SessionTokens> {
    const nowMs = this.now();
    const accessExpiresAt = nowMs + ACCESS_TTL_SEC * 1000;
    const uid = uidFor(accountId);
    const accessToken = await new SignJWT({ acc: accountId })
      .setProtectedHeader({ alg: 'EdDSA' })
      .setSubject(uid)
      .setIssuer(ISSUER)
      .setAudience(AUDIENCE)
      .setIssuedAt(Math.floor(nowMs / 1000))
      .setExpirationTime(Math.floor(accessExpiresAt / 1000))
      .sign(this.privateKey);
    const refreshToken = randomBytes(32).toString('base64url');
    await this.prisma.refreshToken.create({
      data: {
        accountId,
        tokenHash: hash(refreshToken),
        family,
        expiresAt: new Date(nowMs + REFRESH_TTL_MS),
        userAgent: userAgent?.slice(0, 256) ?? null,
      },
    });
    return { accountId, uid, accessToken, accessExpiresAt, refreshToken };
  }

  private async revokeFamily(family: string): Promise<void> {
    await this.prisma.refreshToken.updateMany({ where: { family, revokedAt: null }, data: { revokedAt: new Date(this.now()) } });
  }
}
