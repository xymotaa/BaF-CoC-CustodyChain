'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { WalletStore, canonicalize } = require('../src/walletStore');

test('cria chave cifrada e assina exatamente os bytes do desafio', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:admin:teste-unitario';
    const password = 'senha-local-forte';

    try {
        const identity = store.createAdminIdentity({ did, password });
        assert.deepEqual(Object.keys(identity).sort(), [
            'algorithm', 'did', 'keyId', 'publicKeyMultibase'
        ]);

        const message = Buffer.from('challenge canonico', 'utf8');
        const proof = store.sign({
            did,
            password,
            signingInput: message.toString('base64url')
        });

        const rawPublicKey = decodeMultikey(identity.publicKeyMultibase).subarray(2);
        const spki = Buffer.concat([
            Buffer.from('302a300506032b6570032100', 'hex'),
            rawPublicKey
        ]);
        const publicKey = crypto.createPublicKey({ key: spki, format: 'der', type: 'spki' });
        assert.equal(
            crypto.verify(null, message, publicKey, Buffer.from(proof.signature, 'base64url')),
            true
        );
        assert.throws(
            () => store.sign({ did, password: 'senha-incorreta', signingInput: message.toString('base64url') }),
            /Senha da wallet inválida/
        );
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('cria identidade titular e assina somente comando de registro correspondente', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:expert:teste-unitario';
    const password = 'senha-local-forte';
    const command = {
        version: 1,
        type: 'CustodyChainDidRegistration',
        did,
        enrollmentId: 'urn:uuid:11111111-1111-1111-1111-111111111111',
        audience: 'custodychain-ledger'
    };

    try {
        const identity = store.createIdentity({ did, password });
        assert.equal(identity.keyId, `${did}#key-1`);
        const proof = store.signDidCommand({
            did,
            password,
            command,
            expectedType: 'CustodyChainDidRegistration',
            actorField: 'did'
        });

        const rawPublicKey = decodeMultikey(identity.publicKeyMultibase).subarray(2);
        const publicKey = crypto.createPublicKey({
            key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), rawPublicKey]),
            format: 'der', type: 'spki'
        });
        assert.equal(crypto.verify(null, Buffer.from(canonicalize(command)), publicKey,
            Buffer.from(proof.signature, 'base64url')), true);
        assert.throws(() => store.signDidCommand({
            did,
            password,
            command: { ...command, did: 'did:legal:expert:outro' },
            expectedType: 'CustodyChainDidRegistration',
            actorField: 'did'
        }), /não corresponde/);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('assina VC de permissão somente para o DID emissor', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:admin:teste-vc';
    const password = 'senha-local-forte';
    const credential = {
        '@context': ['https://www.w3.org/2018/credentials/v1'],
        id: 'urn:uuid:11111111-1111-1111-1111-111111111111',
        type: ['VerifiableCredential', 'CustodyChainPermissionCredential'],
        issuer: did,
        issuanceDate: '2026-10-06T12:00:00.000Z',
        credentialSubject: { id: 'did:legal:expert:teste-vc', perfil: 'PERITO' },
        credentialStatus: {
            id: 'urn:uuid:11111111-1111-1111-1111-111111111111#status',
            type: 'CustodyChainLedgerStatusV1'
        }
    };

    try {
        const identity = store.createAdminIdentity({ did, password });
        const signed = store.signVerifiableCredential({ did, password, credential });
        const publicKey = crypto.createPublicKey({
            key: Buffer.concat([
                Buffer.from('302a300506032b6570032100', 'hex'),
                decodeMultikey(identity.publicKeyMultibase).subarray(2)
            ]),
            format: 'der', type: 'spki'
        });
        const { proof, ...unsignedCredential } = signed;

        assert.equal(proof.verificationMethod, `${did}#auth-1`);
        assert.equal(crypto.verify(null, Buffer.from(canonicalize(unsignedCredential)), publicKey,
            Buffer.from(proof.proofValue, 'base64url')), true);
        assert.throws(() => store.signVerifiableCredential({
            did,
            password,
            credential: { ...credential, issuer: 'did:legal:admin:outro' }
        }), /não corresponde/);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('assina vetor canônico de operação sem alterar o envelope', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:expert:teste-001';
    const password = 'senha-local-forte';
    const vectors = JSON.parse(fs.readFileSync(
        path.resolve(__dirname, '..', '..', 'contracts', 'signed-operation-v1-vectors.json')));
    const operation = vectors.vectors[0].unsigned;

    try {
        const identity = store.createIdentity({ did, password });
        const signed = store.signSignedOperation({ did, password, operation });
        const publicKey = crypto.createPublicKey({
            key: Buffer.concat([
                Buffer.from('302a300506032b6570032100', 'hex'),
                decodeMultikey(identity.publicKeyMultibase).subarray(2)
            ]),
            format: 'der', type: 'spki'
        });
        const { signature, ...unsigned } = signed;

        assert.equal(canonicalize(unsigned), vectors.vectors[0].canonical);
        assert.equal(crypto.verify(null, Buffer.from(canonicalize(unsigned)), publicKey,
            Buffer.from(signature, 'base64url')), true);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('rotaciona a chave somente com prova da chave atual e da candidata', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:expert:rotacao-unitaria';
    const password = 'senha-local-forte';

    try {
        const current = store.createIdentity({ did, password });
        const candidate = store.createRotationCandidate({ did, password });
        const command = {
            type: 'CustodyChainDidKeyRotation',
            version: 1,
            commandId: 'urn:uuid:11111111-2222-3333-4444-555555555555',
            subjectDid: did,
            expectedDocumentVersion: 2,
            currentKeyId: current.keyId,
            newVerificationMethod: {
                id: candidate.keyId,
                type: 'Multikey',
                controller: did,
                publicKeyMultibase: candidate.publicKeyMultibase
            },
            algorithm: 'Ed25519',
            audience: 'custodychain-ledger',
            canonicalization: 'custodychain-json-c14n-v1',
            issuedAt: '2027-01-15T08:00:00.000Z',
            expiresAt: '2027-01-15T08:05:00.000Z',
            nonce: '0123456789abcdefghijkl'
        };

        const proofs = store.signKeyRotation({
            did, password, candidateId: candidate.candidateId, command
        });
        assert.equal(proofs.currentKeyProof.keyId, current.keyId);
        assert.equal(proofs.newKeyProof.keyId, candidate.keyId);
        assertProof(command, proofs.currentKeyProof.signature, current.publicKeyMultibase);
        assertProof(command, proofs.newKeyProof.signature, candidate.publicKeyMultibase);

        const activated = store.confirmKeyRotation({
            did, candidateId: candidate.candidateId, keyId: candidate.keyId
        });
        assert.equal(activated.keyId, candidate.keyId);
        assert.equal(store.listIdentities()[0].keyId, candidate.keyId);
        assert.equal(store.sign({
            did, password, signingInput: Buffer.from('novo desafio').toString('base64url')
        }).keyId, candidate.keyId);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('reutiliza candidata pendente e preserva a chave ativa quando a rotação é descartada', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:custodian:rotacao-descartada';
    const password = 'senha-local-forte';

    try {
        const current = store.createIdentity({ did, password });
        const candidate = store.createRotationCandidate({ did, password });
        const repeated = store.createRotationCandidate({ did, password });
        assert.equal(repeated.candidateId, candidate.candidateId);
        assert.throws(() => store.createRotationCandidate({ did, password: 'senha-incorreta' }), /Senha/);

        store.discardRotationCandidate({ did, candidateId: candidate.candidateId });
        assert.equal(store.listIdentities()[0].keyId, current.keyId);
        assert.doesNotThrow(() => store.createRotationCandidate({ did, password }));
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('reconcilia candidata pendente indicada pelo desafio de autenticação', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const store = new WalletStore(path.join(directory, 'wallet.db'));
    const did = 'did:legal:expert:rotacao-reconciliada';
    const password = 'senha-local-forte';

    try {
        store.createIdentity({ did, password });
        const candidate = store.createRotationCandidate({ did, password });
        const signingInput = Buffer.from('desafio da chave indicada pelo ledger').toString('base64url');
        const proof = store.sign({ did, password, signingInput, keyId: candidate.keyId });
        assert.equal(proof.keyId, candidate.keyId);

        const reconciled = store.confirmKeyRotation({ did, keyId: candidate.keyId });
        assert.equal(reconciled.keyId, candidate.keyId);
        assert.equal(store.listIdentities()[0].keyId, candidate.keyId);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

test('migra a chave única legada sem recifrar o material privado', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'custodychain-wallet-'));
    const databasePath = path.join(directory, 'wallet.db');
    const did = 'did:legal:admin:migracao-legada';
    const password = 'senha-local-forte';
    let store = new WalletStore(databasePath);

    try {
        const identity = store.createAdminIdentity({ did, password });
        store.database.exec(`
            INSERT INTO identities (
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p, created_at
            )
            SELECT
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p, created_at
            FROM identity_keys;
            DELETE FROM identity_keys;
        `);
        store.close();

        store = new WalletStore(databasePath);
        assert.equal(store.listIdentities()[0].keyId, identity.keyId);
        assert.equal(store.sign({
            did, password, signingInput: Buffer.from('desafio legado').toString('base64url')
        }).keyId, identity.keyId);
    } finally {
        store.close();
        fs.rmSync(directory, { recursive: true, force: true });
    }
});

function assertProof(command, signature, publicKeyMultibase) {
    const publicKey = crypto.createPublicKey({
        key: Buffer.concat([
            Buffer.from('302a300506032b6570032100', 'hex'),
            decodeMultikey(publicKeyMultibase).subarray(2)
        ]),
        format: 'der', type: 'spki'
    });
    assert.equal(crypto.verify(null, Buffer.from(canonicalize(command)), publicKey,
        Buffer.from(signature, 'base64url')), true);
}

function decodeMultikey(value) {
    const alphabet = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    const bytes = [0];
    for (const character of value.slice(1)) {
        let carry = alphabet.indexOf(character);
        for (let index = 0; index < bytes.length; index += 1) {
            carry += bytes[index] * 58;
            bytes[index] = carry & 0xff;
            carry >>= 8;
        }
        while (carry > 0) {
            bytes.push(carry & 0xff);
            carry >>= 8;
        }
    }
    return Buffer.from(bytes.reverse());
}
