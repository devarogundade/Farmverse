import { Address, Hex } from '@vechain/sdk-core';
import { ProviderInternalBaseWallet, ThorClient, VeChainProvider } from '@vechain/sdk-network';
import type { Logger } from '../../../game/src/core/logger';
import type { Payer, PayoutRequest, PayoutResult } from './redeem';

/**
 * B3TR payouts through VeBetterDAO's X2EarnRewardsPool: the app's operator key calls
 * `distributeRewardWithProofAndMetadata`, which sends B3TR from the app's allocation to the player.
 * Solidity: vechain-contracts/contracts/interfaces/IX2EarnRewardsPool.sol
 */
export const IX2EarnRewardsPoolAbi = [
  {
    type: 'function',
    name: 'distributeRewardWithProofAndMetadata',
    stateMutability: 'nonpayable',
    inputs: [
      { name: 'appId', type: 'bytes32' },
      { name: 'amount', type: 'uint256' },
      { name: 'receiver', type: 'address' },
      { name: 'proofTypes', type: 'string[]' },
      { name: 'proofValues', type: 'string[]' },
      { name: 'eventCodes', type: 'string[]' },
      { name: 'eventValues', type: 'uint256[]' },
      { name: 'description', type: 'string' },
      { name: 'metadata', type: 'string' },
    ],
    outputs: [],
  },
  {
    type: 'function',
    name: 'availableFunds',
    stateMutability: 'view',
    inputs: [{ name: 'appId', type: 'bytes32' }],
    outputs: [{ type: 'uint256', name: '' }],
  },
  {
    type: 'function',
    name: 'isDistributionPaused',
    stateMutability: 'view',
    inputs: [{ name: 'appId', type: 'bytes32' }],
    outputs: [{ type: 'bool', name: '' }],
  },
] as const;

export interface VeBetterConfig {
  /** A VeChain Thor node (testnet: https://testnet.vechain.org). */
  readonly nodeUrl: string;
  /** The X2EarnRewardsPool contract. */
  readonly pool: string;
  /** The app's id in VeBetterDAO (bytes32). */
  readonly appId: string;
  /** The private key of a wallet the app lists as a reward distributor. */
  readonly signerKey: string;
}

/** Whole tokens (up to 6 decimals) in wei. */
export function toWei(amount: number): bigint {
  const [whole, frac = ''] = amount.toFixed(6).split('.');
  return BigInt(whole!) * 10n ** 18n + BigInt(frac.padEnd(18, '0'));
}

export function veBetterPayer(cfg: VeBetterConfig, log: Logger): Payer {
  const key = Hex.of(cfg.signerKey.startsWith('0x') ? cfg.signerKey : `0x${cfg.signerKey}`).bytes;
  const address = Address.ofPrivateKey(key).toString();
  const thor = ThorClient.at(cfg.nodeUrl);
  const provider = new VeChainProvider(thor, new ProviderInternalBaseWallet([{ privateKey: key, address }]));
  const appId = cfg.appId as `0x${string}`;
  let contract: Promise<ReturnType<typeof thor.contracts.load<typeof IX2EarnRewardsPoolAbi>>> | null = null;
  const load = () =>
    (contract ??= provider.getSigner(address).then((signer) => {
      if (!signer) throw new Error('no signer for the rewards key');
      return thor.contracts.load(cfg.pool, IX2EarnRewardsPoolAbi, signer);
    }));

  return {
    unit: 'B3TR',
    async pay(req: PayoutRequest, onSent: (tx: string) => Promise<void>): Promise<PayoutResult> {
      const amount = toWei(req.amount);
      let pool: Awaited<ReturnType<typeof load>>;
      try {
        pool = await load();
        const [paused] = await pool.read.isDistributionPaused(appId);
        if (paused) return { sent: false, why: 'paused' };
        const [funds] = await pool.read.availableFunds(appId);
        if ((funds as bigint) < amount) return { sent: false, why: 'no_funds' };
      } catch (error) {
        contract = null;
        log.error('b3tr payout: pool check failed', { error: String(error) });
        return { sent: false, why: 'error' };
      }

      let id: string;
      let wait: () => Promise<{ reverted: boolean } | null>;
      try {
        const sent = await pool.transact.distributeRewardWithProofAndMetadata(
          appId,
          amount,
          req.receiver as `0x${string}`,
          ['link'],
          ['https://farmverse.world'],
          [],
          [],
          `Farmverse: ${req.task || 'game reward'}`.slice(0, 120),
          JSON.stringify({ app: 'farmverse', code: req.code, task: req.task }),
        );
        id = sent.id;
        wait = () => sent.wait();
      } catch (error) {
        // refused before it reached the chain (simulation, nonce, node down): nothing was paid
        log.error('b3tr payout: send refused', { code: req.code, error: String(error) });
        return { sent: false, why: 'error' };
      }

      await onSent(id);
      try {
        const receipt = await wait();
        if (!receipt) return { sent: true, tx: id, confirmed: false };
        if (receipt.reverted) {
          log.error('b3tr payout reverted', { code: req.code, tx: id });
          return { sent: false, why: 'reverted', tx: id };
        }
        return { sent: true, tx: id, confirmed: true };
      } catch (error) {
        log.warn('b3tr payout: no receipt yet', { code: req.code, tx: id, error: String(error) });
        return { sent: true, tx: id, confirmed: false };
      }
    },
  };
}
