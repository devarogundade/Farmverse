/** NEAR account names: name.near, sub.name.testnet, or a 64-character implicit account. */
export const isAccount = (v: string) => /^(([a-z\d]+[-_])*[a-z\d]+\.)+(near|testnet)$/.test(v) || /^[a-f\d]{64}$/.test(v);
