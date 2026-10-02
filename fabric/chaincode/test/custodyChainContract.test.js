'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const CustodyChainContract = require('../lib/custodyChainContract');

const did = 'did:legal:admin:teste-001';
const keyId = `${did}#auth-1`;
const publicKey = 'z6MktwupdmLXVVqTzCw4i46r4uGyosGXRnR3XjN4Zq7oMMsw';

test('bootstrap cria um único DID administrador v2 ativo', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');

    const document = JSON.parse(await contract.BootstrapAdminDid(ctx, did, keyId, publicKey));

    assert.equal(document.version, 2);
    assert.equal(document.status, 'ATIVO');
    assert.equal(document.verificationMethod[0].publicKeyMultibase, publicKey);
    await assert.rejects(
        contract.BootstrapAdminDid(ctx, 'did:legal:admin:outro', 'did:legal:admin:outro#auth-1', publicKey),
        /já foi concluído/
    );
});

test('bootstrap recusa MSP que não administra identidades', async () => {
    const contract = new CustodyChainContract();
    await assert.rejects(
        contract.BootstrapAdminDid(contexto('Org2MSP'), did, keyId, publicKey),
        /MSP não autorizado/
    );
});

test('revogação v2 torna o DID inativo', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    await contract.BootstrapAdminDid(ctx, did, keyId, publicKey);

    const revoked = JSON.parse(await contract.RevogarDidV2(ctx, did));

    assert.equal(revoked.status, 'REVOGADO');
    assert.equal(revoked.ativo, false);
});

function contexto(mspId) {
    const state = new Map();
    return {
        clientIdentity: { getMSPID: () => mspId },
        stub: {
            createCompositeKey: (prefix, values) => `${prefix}:${values.join(':')}`,
            getState: async (key) => state.get(key) || Buffer.alloc(0),
            putState: async (key, value) => state.set(key, value),
            getTxTimestamp: () => ({ seconds: { low: 1_800_000_000 }, nanos: 0 })
        }
    };
}
