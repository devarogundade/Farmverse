//! FARM — the Farmverse token (NEP-141 + NEP-145 + NEP-148).
//!
//! Deliberately plain: the whole supply is minted once, to the treasury, in `new`. There is no
//! mint, no burn-from, no owner and no pause, so nothing about the token can change after deploy
//! and holders don't have to trust anyone with it. Everything else is the audited
//! `near-contract-standards` implementation.
//!
//! Deploy to an account with no access keys left (or keys held only by the team multisig), so the
//! code can't be replaced either.

use near_contract_standards::fungible_token::events::FtMint;
use near_contract_standards::fungible_token::metadata::{
    FT_METADATA_SPEC, FungibleTokenMetadata, FungibleTokenMetadataProvider,
};
use near_contract_standards::fungible_token::{
    FungibleToken, FungibleTokenCore, FungibleTokenResolver,
};
use near_contract_standards::storage_management::{
    StorageBalance, StorageBalanceBounds, StorageManagement,
};
use near_sdk::collections::LazyOption;
use near_sdk::json_types::U128;
use near_sdk::{
    AccountId, BorshStorageKey, NearToken, PanicOnDefault, PromiseOrValue, env, log, near, require,
};

#[derive(PanicOnDefault)]
#[near(contract_state)]
pub struct Contract {
    token: FungibleToken,
    metadata: LazyOption<FungibleTokenMetadata>,
}

#[derive(BorshStorageKey)]
#[near]
enum StorageKey {
    FungibleToken,
    Metadata,
}

#[near]
impl Contract {
    /// Mints `total_supply` (in the smallest unit) to `treasury_id`, once, forever.
    #[init]
    pub fn new(treasury_id: AccountId, total_supply: U128, metadata: FungibleTokenMetadata) -> Self {
        require!(!env::state_exists(), "Already initialized");
        require!(metadata.spec == FT_METADATA_SPEC, "metadata.spec must be ft-1.0.0");
        require!(total_supply.0 > 0, "total_supply must be positive");
        metadata.assert_valid();

        let mut this = Self {
            token: FungibleToken::new(StorageKey::FungibleToken),
            metadata: LazyOption::new(StorageKey::Metadata, Some(&metadata)),
        };
        this.token.internal_register_account(&treasury_id);
        this.token.internal_deposit(&treasury_id, total_supply.0);
        FtMint { owner_id: &treasury_id, amount: total_supply, memo: Some("FARM genesis supply") }.emit();
        this
    }
}

#[near]
impl FungibleTokenCore for Contract {
    #[payable]
    fn ft_transfer(&mut self, receiver_id: AccountId, amount: U128, memo: Option<String>) {
        self.token.ft_transfer(receiver_id, amount, memo)
    }

    #[payable]
    fn ft_transfer_call(
        &mut self,
        receiver_id: AccountId,
        amount: U128,
        memo: Option<String>,
        msg: String,
    ) -> PromiseOrValue<U128> {
        self.token.ft_transfer_call(receiver_id, amount, memo, msg)
    }

    fn ft_total_supply(&self) -> U128 {
        self.token.ft_total_supply()
    }

    fn ft_balance_of(&self, account_id: AccountId) -> U128 {
        self.token.ft_balance_of(account_id)
    }
}

#[near]
impl FungibleTokenResolver for Contract {
    #[private]
    fn ft_resolve_transfer(&mut self, sender_id: AccountId, receiver_id: AccountId, amount: U128) -> U128 {
        let (used, burned) = self.token.internal_ft_resolve_transfer(&sender_id, receiver_id, amount);
        if burned > 0 {
            log!("Account @{} burned {}", sender_id, burned);
        }
        used.into()
    }
}

#[near]
impl StorageManagement for Contract {
    #[payable]
    fn storage_deposit(&mut self, account_id: Option<AccountId>, registration_only: Option<bool>) -> StorageBalance {
        self.token.storage_deposit(account_id, registration_only)
    }

    #[payable]
    fn storage_withdraw(&mut self, amount: Option<NearToken>) -> StorageBalance {
        self.token.storage_withdraw(amount)
    }

    /// Closing an account with a balance needs `force: true`; the balance is then burned.
    #[payable]
    fn storage_unregister(&mut self, force: Option<bool>) -> bool {
        if let Some((account_id, balance)) = self.token.internal_storage_unregister(force) {
            log!("Closed @{} with {}", account_id, balance);
            true
        } else {
            false
        }
    }

    fn storage_balance_bounds(&self) -> StorageBalanceBounds {
        self.token.storage_balance_bounds()
    }

    fn storage_balance_of(&self, account_id: AccountId) -> Option<StorageBalance> {
        self.token.storage_balance_of(account_id)
    }
}

#[near]
impl FungibleTokenMetadataProvider for Contract {
    fn ft_metadata(&self) -> FungibleTokenMetadata {
        self.metadata.get().expect("metadata is set at init")
    }
}

#[cfg(all(test, not(target_arch = "wasm32")))]
mod tests {
    use super::*;
    use near_sdk::test_utils::{VMContextBuilder, accounts};
    use near_sdk::testing_env;

    const SUPPLY: u128 = 1_000_000_000 * 10u128.pow(18);

    fn meta() -> FungibleTokenMetadata {
        FungibleTokenMetadata {
            spec: FT_METADATA_SPEC.to_string(),
            name: "Farmverse".into(),
            symbol: "FARM".into(),
            icon: None,
            reference: None,
            reference_hash: None,
            decimals: 18,
        }
    }

    fn ctx(predecessor: AccountId) -> VMContextBuilder {
        let mut b = VMContextBuilder::new();
        b.current_account_id(accounts(0)).signer_account_id(predecessor.clone()).predecessor_account_id(predecessor);
        b
    }

    #[test]
    fn genesis_supply_goes_to_treasury() {
        testing_env!(ctx(accounts(1)).build());
        let c = Contract::new(accounts(1), U128(SUPPLY), meta());
        assert_eq!(c.ft_total_supply().0, SUPPLY);
        assert_eq!(c.ft_balance_of(accounts(1)).0, SUPPLY);
        assert_eq!(c.ft_metadata().symbol, "FARM");
    }

    #[test]
    #[should_panic(expected = "total_supply must be positive")]
    fn zero_supply_is_refused() {
        testing_env!(ctx(accounts(1)).build());
        Contract::new(accounts(1), U128(0), meta());
    }

    #[test]
    fn transfer_moves_balance() {
        let mut context = ctx(accounts(1));
        testing_env!(context.build());
        let mut c = Contract::new(accounts(1), U128(SUPPLY), meta());

        testing_env!(context
            .attached_deposit(c.storage_balance_bounds().min)
            .predecessor_account_id(accounts(2))
            .build());
        c.storage_deposit(None, None);

        testing_env!(context
            .attached_deposit(NearToken::from_yoctonear(1))
            .predecessor_account_id(accounts(1))
            .build());
        c.ft_transfer(accounts(2), U128(5), None);

        assert_eq!(c.ft_balance_of(accounts(2)).0, 5);
        assert_eq!(c.ft_balance_of(accounts(1)).0, SUPPLY - 5);
        assert_eq!(c.ft_total_supply().0, SUPPLY);
    }
}
