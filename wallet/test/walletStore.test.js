'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { WalletStore } = require('../src/walletStore');

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
