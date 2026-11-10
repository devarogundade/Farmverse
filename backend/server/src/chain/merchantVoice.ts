/**
 * Merchant voices: after the pricing formula (`quote`) sets a merchant's price, NEAR AI Cloud writes a short note
 * in that merchant's own voice explaining the move, from the real numbers only. The model never sets prices or
 * sends transactions: the formula is the guardrail, the note is words on the Market Town page.
 *
 * Inference runs in NEAR AI Cloud's TEEs. Each note's signature is checked here the way NEAR's verification
 * example does it: the request and response hashes match the signed text, the signer recovers to a key in NEAR AI's
 * attestation report for the model (fetched with our nonce), and NVIDIA's attestation service (NRAS) passes the GPU
 * evidence for that key. The Intel TDX quote is not checked here, so the page doesn't claim it.
 */
import { createHash, randomBytes } from 'node:crypto';
import { secp256k1 } from '@noble/curves/secp256k1.js';
import { keccak_256 } from '@noble/hashes/sha3.js';
import { createRemoteJWKSet, decodeJwt, jwtVerify } from 'jose';
import type { Logger } from '../../../game/src/core/logger';

/** Who each merchant is, in a sentence or two (the model speaks as them). */
export const PERSONAS: Readonly<Record<string, string>> = {
  bea: 'You run the bakery. You are warm and chatty, up before dawn, and you think about tomorrow\'s bread and your oven.',
  ruth: 'You keep the ranch. You are blunt and practical, you use few words, and you think about your animals and the weather.',
  tomas: 'You trade a bit of everything. You are a cheerful haggler who sees every price as a deal and likes a quick turnover.',
  ivy: 'You collect golden harvests. You are refined and particular, you prize rare things, and you never overpay for common ones.',
};

/** One good's move this tick: the formula's inputs and outputs, nothing else. */
export interface PriceMove {
  readonly merchant: string;
  readonly display: string;
  readonly role: string;
  readonly item: string;
  readonly stock: number;
  readonly target: number;
  readonly sales24h: number;
  /** yocto-FARM per unit the merchant paid at its last tick (none on its first). */
  readonly oldBuy?: bigint;
  readonly newBuy: bigint;
  readonly sell: bigint;
  /** The formula's fixed reason label. */
  readonly why: string;
  readonly wantsMore: boolean;
}

/** What NEAR AI's signature on a note proves, as checked here. */
export interface VoiceProof {
  readonly chatId: string;
  /** provider_tee: signed inside the model's TEE; gateway: signed inside NEAR AI's gateway TEE. */
  readonly kind: 'provider_tee' | 'gateway';
  readonly signingAddress: string;
  readonly signature: string;
  /** The signed text: `model:requestHash:responseHash` (provider_tee) or `requestHash:responseHash`. */
  readonly text: string;
  readonly checks: {
    /** Our request and response hashes are the ones signed (and the model id, for provider_tee). */
    readonly hashes: boolean;
    /** The signature recovers to the signing address. */
    readonly signer: boolean;
    /** That address is in NEAR AI's attestation report, which echoed our nonce. */
    readonly attested: boolean;
    /** NVIDIA's attestation service passed the GPU evidence for that address with our nonce (provider_tee only). */
    readonly gpu: boolean;
  };
  readonly verified: boolean;
  readonly checkedAt: number;
}

/** A merchant's note for one good, kept until its price or reason changes. */
export interface Voice {
  readonly text: string;
  /** The buy price (yocto-FARM) and reason the note was written for. */
  readonly buy: string;
  readonly why: string;
  readonly model: string;
  readonly at: number;
  readonly proof?: VoiceProof;
}

export interface MerchantVoice {
  /** A note for this move, or null (no key, error, timeout, empty answer): the page then shows the fixed label. */
  note(move: PriceMove): Promise<Voice | null>;
}

export interface ChatMessage {
  readonly role: 'system' | 'user';
  readonly content: string;
}

const E18 = 10n ** 18n;

/** yocto-FARM as a short FARM figure (up to 4 decimals). */
export function farmText(yocto: bigint): string {
  const n = Number((yocto * 10_000n + E18 / 2n) / E18) / 10_000;
  return n.toFixed(4).replace(/\.?0+$/, '');
}

const itemLabel = (item: string) => {
  const golden = item.startsWith('golden_');
  const base = (golden ? item.slice(7) : item).replace(/_/g, ' ');
  return golden ? `golden ${base}` : base;
};

/** The prompt: the merchant's persona, then the numbers of this move and nothing else. */
export function buildMessages(move: PriceMove): ChatMessage[] {
  const persona = PERSONAS[move.merchant] ?? '';
  const system =
    `You are ${move.display}, the ${move.role.toLowerCase()} in Market Town, a farming game where farmers sell goods for FARM tokens. ${persona} ` +
    'Write one short note to farmers, one or two sentences and under 40 words, explaining your price for one of your goods. ' +
    'Speak in the first person, in your own voice. Use only the facts given: do not invent numbers, events, people or reasons, ' +
    'and do not promise future prices. Plain text only: no emojis, hashtags, lists or quotation marks.';
  const lines = [
    `Goods: ${itemLabel(move.item)}`,
    `In stock: ${move.stock} (I like to hold about ${move.target})`,
    `Sold on the market in the last 24 hours: ${move.sales24h}`,
    move.oldBuy === undefined ? 'This is my first price for it.' : `I paid before: ${farmText(move.oldBuy)} FARM each`,
    move.wantsMore ? `I pay now: ${farmText(move.newBuy)} FARM each` : 'I am not buying more right now: my shelves are full.',
    `I sell at: ${farmText(move.sell)} FARM each`,
    `Reason: ${move.why}`,
  ];
  return [
    { role: 'system', content: system },
    { role: 'user', content: lines.join('\n') },
  ];
}

/** Call the model only when the price or the reason changed since the last note. */
export function needsNote(prev: Voice | undefined, buy: bigint, why: string): boolean {
  return !prev || prev.buy !== buy.toString() || prev.why !== why;
}

/** The model's answer as a note: no reasoning, no wrapping quotes, at most two sentences and 280 characters. */
export function cleanNote(raw: string | null | undefined): string | null {
  if (!raw) return null;
  let s = raw.replace(/<think>[\s\S]*?<\/think>/gi, '').replace(/<\/?think>/gi, '');
  s = s.replace(/\s+/g, ' ').trim().replace(/^["'“”‘’]+|["'“”‘’]+$/g, '').trim();
  if (!s) return null;
  const sentences = s.match(/[^.!?]+[.!?]+(\s|$)/g);
  if (sentences && sentences.length > 2) s = sentences.slice(0, 2).join('').trim();
  if (s.length > 280) s = `${s.slice(0, 277).replace(/\s+\S*$/, '')}…`;
  return s;
}

/** The Ethereum-style (EIP-191 personal_sign) signer of `text`, as NEAR AI's ECDSA signatures use. */
export function recoverSigner(text: string, signatureHex: string): string {
  const sig = Buffer.from(signatureHex.replace(/^0x/, ''), 'hex');
  if (sig.length !== 65) throw new Error('signature must be 65 bytes');
  const v = sig[64]!;
  const recovery = v >= 27 ? v - 27 : v;
  const msg = Buffer.from(text, 'utf8');
  const hash = keccak_256(Buffer.concat([Buffer.from(`\x19Ethereum Signed Message:\n${msg.length}`, 'utf8'), msg]));
  const recovered = new Uint8Array(65);
  recovered[0] = recovery;
  recovered.set(sig.subarray(0, 64), 1);
  const pub = secp256k1.recoverPublicKey(recovered, hash, { prehash: false });
  const uncompressed = secp256k1.Point.fromBytes(pub).toBytes(false);
  return `0x${Buffer.from(keccak_256(uncompressed.subarray(1)).subarray(-20)).toString('hex')}`;
}

const sha256 = (s: string) => createHash('sha256').update(s, 'utf8').digest('hex');

/** Checks a signed text against our hashes; returns the kind it is, or null when it doesn't match. */
export function checkSignedText(text: string, model: string, requestHash: string, responseHash: string): VoiceProof['kind'] | null {
  const parts = text.split(':');
  // the model id itself can't hold ':' here, so 3 parts = provider_tee, 2 = gateway
  if (parts.length === 3) return parts[0] === model && parts[1] === requestHash && parts[2] === responseHash ? 'provider_tee' : null;
  if (parts.length === 2) return parts[0] === requestHash && parts[1] === responseHash ? 'gateway' : null;
  return null;
}

export interface NearAiConfig {
  readonly apiKey: string;
  readonly model: string;
  readonly baseUrl: string;
  /** The chat call's timeout. */
  readonly timeoutMs: number;
}

interface Attestation {
  readonly at: number;
  /** Model TEE addresses (lower case) and whether NRAS passed each one's GPU evidence. */
  readonly modelTee: ReadonlyMap<string, boolean>;
  readonly gateway: ReadonlySet<string>;
}

const NRAS_URL = 'https://nras.attestation.nvidia.com/v3/attest/gpu';
const NRAS_JWKS = createRemoteJWKSet(new URL('https://nras.attestation.nvidia.com/.well-known/jwks.json'), {
  // NVIDIA's key set is large (500 KB); jose's default 5 s is too tight
  timeoutDuration: 30_000,
});
/** NRAS tokens live about an hour; attest again after that. */
const ATTESTATION_TTL_MS = 50 * 60_000;

/** NVIDIA's answer for one GPU node passes: every token signed by NRAS, the overall result true, our nonce in each. */
export async function nrasPasses(payload: unknown, nonce: string, fetchFn: typeof fetch): Promise<boolean> {
  const body = typeof payload === 'string' ? payload : JSON.stringify(payload);
  const parsed = JSON.parse(body) as { nonce?: string };
  if (parsed.nonce?.toLowerCase() !== nonce) return false;
  const res = await fetchFn(NRAS_URL, { method: 'POST', headers: { accept: 'application/json', 'content-type': 'application/json' }, body, signal: AbortSignal.timeout(20_000) });
  if (!res.ok) return false;
  const answer = (await res.json()) as unknown[];
  const tokens: [string, string][] = [];
  for (const part of answer) {
    if (Array.isArray(part) && part.length === 2) tokens.push([String(part[0]), String(part[1])]);
    else if (part && typeof part === 'object') for (const [k, t] of Object.entries(part)) tokens.push([k, String(t)]);
  }
  const overall = tokens.find(([k]) => k === 'JWT');
  if (!overall || tokens.length < 2) return false;
  for (const [, token] of tokens) {
    await jwtVerify(token, NRAS_JWKS, { issuer: 'https://nras.attestation.nvidia.com' });
    const claims = decodeJwt(token);
    if (String(claims['eat_nonce'] ?? '').toLowerCase() !== nonce) return false;
  }
  return decodeJwt(overall[1])['x-nvidia-overall-att-result'] === true;
}

export interface VoiceIo {
  readonly fetch?: typeof fetch;
  readonly now?: () => number;
  /** Checks one GPU node's NVIDIA evidence against our nonce. Default: NVIDIA's attestation service. */
  readonly gpuCheck?: (nvidiaPayload: unknown, nonce: string) => Promise<boolean>;
  /** Wait between signature lookups (the signature lives on the node that answered). */
  readonly retryMs?: number;
}

/** NEAR AI Cloud as the merchants' voice (OpenAI-compatible chat completions, then signature checks). */
export function nearAiVoice(cfg: NearAiConfig, log: Logger, io: VoiceIo = {}): MerchantVoice {
  const fetchFn = io.fetch ?? fetch;
  const now = io.now ?? Date.now;
  const gpuCheck = io.gpuCheck ?? ((payload: unknown, nonce: string) => nrasPasses(payload, nonce, fetchFn));
  const retryMs = io.retryMs ?? 1_000;
  const auth = { authorization: `Bearer ${cfg.apiKey}` };
  let attestation: Attestation | undefined;

  async function attest(): Promise<Attestation> {
    if (attestation && now() - attestation.at < ATTESTATION_TTL_MS) return attestation;
    const nonce = randomBytes(32).toString('hex');
    const params = new URLSearchParams({ model: cfg.model, signing_algo: 'ecdsa', nonce });
    const res = await fetchFn(`${cfg.baseUrl}/v1/attestation/report?${params}`, { headers: { accept: 'application/json', ...auth }, signal: AbortSignal.timeout(20_000) });
    if (!res.ok) throw new Error(`attestation report: HTTP ${res.status}`);
    const report = (await res.json()) as {
      model_attestations?: { signing_address?: string; request_nonce?: string; nvidia_payload?: unknown }[];
      gateway_attestation?: { signing_address?: string; request_nonce?: string };
    };
    const modelTee = new Map<string, boolean>();
    for (const a of report.model_attestations ?? []) {
      if (!a.signing_address || a.request_nonce !== nonce) continue;
      let gpu = false;
      try {
        gpu = a.nvidia_payload !== undefined && (await gpuCheck(a.nvidia_payload, nonce));
      } catch (e) {
        log.warn('NRAS check failed', { address: a.signing_address, error: String(e) });
      }
      modelTee.set(a.signing_address.toLowerCase(), gpu);
    }
    const gateway = new Set<string>();
    const g = report.gateway_attestation;
    if (g?.signing_address && g.request_nonce === nonce) gateway.add(g.signing_address.toLowerCase());
    attestation = { at: now(), modelTee, gateway };
    return attestation;
  }

  async function prove(chatId: string, requestBody: string, responseBody: string): Promise<VoiceProof | undefined> {
    const url = `${cfg.baseUrl}/v1/signature/${encodeURIComponent(chatId)}?model=${encodeURIComponent(cfg.model)}&signing_algo=ecdsa`;
    let sig: { text?: string; signature?: string; signing_address?: string; signature_kind?: string } | undefined;
    // the signature lives on the TEE node that answered: another node may not have it yet
    for (let i = 0; i < 4 && !sig; i++) {
      const res = await fetchFn(url, { headers: { accept: 'application/json', ...auth }, signal: AbortSignal.timeout(10_000) });
      if (res.ok) sig = (await res.json()) as typeof sig;
      else if (res.status !== 404) throw new Error(`signature: HTTP ${res.status}`);
      else await new Promise((r) => setTimeout(r, retryMs));
    }
    if (!sig?.text || !sig.signature || !sig.signing_address) return undefined;
    const kind = checkSignedText(sig.text, cfg.model, sha256(requestBody), sha256(responseBody));
    const claimed = sig.signature_kind === 'gateway' || sig.signature_kind === 'provider_tee' ? sig.signature_kind : undefined;
    const hashes = kind !== null && (claimed === undefined || claimed === kind);
    const address = sig.signing_address.toLowerCase();
    let signer = false;
    try {
      signer = recoverSigner(sig.text, sig.signature).toLowerCase() === address;
    } catch {
      signer = false;
    }
    // each check stands on its own (verified needs them all; when hashes pass, k is the kind they matched)
    const k = kind ?? claimed ?? (sig.text.split(':').length === 2 ? 'gateway' : 'provider_tee');
    const att = await attest();
    const attested = k === 'gateway' ? att.gateway.has(address) : att.modelTee.has(address);
    const gpu = k === 'provider_tee' && att.modelTee.get(address) === true;
    return {
      chatId,
      kind: k,
      signingAddress: sig.signing_address,
      signature: sig.signature,
      text: sig.text,
      checks: { hashes, signer, attested, gpu },
      verified: hashes && signer && attested && (k === 'gateway' || gpu),
      checkedAt: now(),
    };
  }

  return {
    async note(move) {
      const requestBody = JSON.stringify({
        model: cfg.model,
        messages: buildMessages(move),
        max_tokens: 160,
        temperature: 0.8,
        stream: false,
        chat_template_kwargs: { enable_thinking: false },
      });
      try {
        const res = await fetchFn(`${cfg.baseUrl}/v1/chat/completions`, {
          method: 'POST',
          headers: { accept: 'application/json', 'content-type': 'application/json', ...auth },
          body: requestBody,
          signal: AbortSignal.timeout(cfg.timeoutMs),
        });
        if (!res.ok) throw new Error(`chat: HTTP ${res.status}`);
        const responseBody = await res.text();
        const answer = JSON.parse(responseBody) as { id?: string; choices?: { message?: { content?: string | null } }[] };
        const text = cleanNote(answer.choices?.[0]?.message?.content);
        if (!text) return null;
        let proof: VoiceProof | undefined;
        if (answer.id) {
          try {
            proof = await prove(answer.id, requestBody, responseBody);
          } catch (e) {
            log.warn('merchant note not verified', { merchant: move.merchant, item: move.item, error: String(e) });
          }
        }
        return { text, buy: move.newBuy.toString(), why: move.why, model: cfg.model, at: now(), ...(proof ? { proof } : {}) };
      } catch (e) {
        log.warn('merchant note failed', { merchant: move.merchant, item: move.item, error: String(e) });
        return null;
      }
    },
  };
}
