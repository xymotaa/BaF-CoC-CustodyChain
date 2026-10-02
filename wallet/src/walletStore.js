'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');
const { encodeEd25519Multikey } = require('./base58btc');

const SCRYPT_N = 32768;
const SCRYPT_R = 8;
const SCRYPT_P = 1;
const SCRYPT_MAXMEM = 64 * 1024 * 1024;
const SPKI_ED25519_PREFIX_LENGTH = 12;

class WalletStore {
    constructor(databasePath) {
        fs.mkdirSync(path.dirname(databasePath), { recursive: true, mode: 0o700 });
        this.database = new DatabaseSync(databasePath);
        fs.chmodSync(databasePath, 0o600);
        this.database.exec('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;');
        this.database.exec(`
            CREATE TABLE IF NOT EXISTS identities (
                did TEXT PRIMARY KEY,
                key_id TEXT NOT NULL UNIQUE,
                public_key_multibase TEXT NOT NULL,
                encrypted_private_key BLOB NOT NULL,
                salt BLOB NOT NULL,
                iv BLOB NOT NULL,
                auth_tag BLOB NOT NULL,
                scrypt_n INTEGER NOT NULL,
                scrypt_r INTEGER NOT NULL,
                scrypt_p INTEGER NOT NULL,
                created_at TEXT NOT NULL
            ) STRICT;
        `);
    }

    createAdminIdentity({ did, password }) {
        if (!/^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/.test(did)) {
            throw new Error('O DID deve seguir o formato did:legal:admin:<identificador>.');
        }
        if (typeof password !== 'string' || password.length < 12) {
            throw new Error('A senha da wallet deve ter pelo menos 12 caracteres.');
        }

        const keyId = `${did}#auth-1`;
        const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
        const publicDer = publicKey.export({ format: 'der', type: 'spki' });
        const publicKeyRaw = publicDer.subarray(SPKI_ED25519_PREFIX_LENGTH);
        const publicKeyMultibase = encodeEd25519Multikey(publicKeyRaw);
        const privateKeyDer = privateKey.export({ format: 'der', type: 'pkcs8' });
        const salt = crypto.randomBytes(16);
        const iv = crypto.randomBytes(12);
        const encryptionKey = deriveKey(password, salt, SCRYPT_N, SCRYPT_R, SCRYPT_P);
        const cipher = crypto.createCipheriv('aes-256-gcm', encryptionKey, iv);
        cipher.setAAD(aad(did, keyId));
        const encryptedPrivateKey = Buffer.concat([cipher.update(privateKeyDer), cipher.final()]);
        const authTag = cipher.getAuthTag();
        encryptionKey.fill(0);
        privateKeyDer.fill(0);

        this.database.prepare(`
            INSERT INTO identities (
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        `).run(
            did,
            keyId,
            publicKeyMultibase,
            encryptedPrivateKey,
            salt,
            iv,
            authTag,
            SCRYPT_N,
            SCRYPT_R,
            SCRYPT_P,
            new Date().toISOString()
        );

        return { did, keyId, algorithm: 'Ed25519', publicKeyMultibase };
    }

    listIdentities() {
        return this.database.prepare(`
            SELECT did, key_id AS keyId, public_key_multibase AS publicKeyMultibase, created_at AS createdAt
            FROM identities ORDER BY created_at
        `).all().map((identity) => ({ ...identity, algorithm: 'Ed25519' }));
    }

    sign({ did, password, signingInput }) {
        if (typeof signingInput !== 'string' || signingInput.length === 0 || signingInput.length > 16384) {
            throw new Error('Conteúdo de assinatura inválido.');
        }
        const identity = this.database.prepare('SELECT * FROM identities WHERE did = ?').get(did);
        if (!identity) {
            throw new Error('Identidade não encontrada nesta wallet.');
        }

        const encryptionKey = deriveKey(
            password,
            identity.salt,
            identity.scrypt_n,
            identity.scrypt_r,
            identity.scrypt_p
        );
        let privateKeyDer;
        try {
            const decipher = crypto.createDecipheriv('aes-256-gcm', encryptionKey, identity.iv);
            decipher.setAAD(aad(identity.did, identity.key_id));
            decipher.setAuthTag(identity.auth_tag);
            privateKeyDer = Buffer.concat([
                decipher.update(identity.encrypted_private_key),
                decipher.final()
            ]);
        } catch {
            throw new Error('Senha da wallet inválida.');
        } finally {
            encryptionKey.fill(0);
        }

        try {
            const privateKey = crypto.createPrivateKey({ key: privateKeyDer, format: 'der', type: 'pkcs8' });
            const message = Buffer.from(signingInput, 'base64url');
            const signature = crypto.sign(null, message, privateKey);
            return {
                did: identity.did,
                keyId: identity.key_id,
                algorithm: 'Ed25519',
                signature: signature.toString('base64url')
            };
        } finally {
            privateKeyDer.fill(0);
        }
    }

    close() {
        this.database.close();
    }
}

function deriveKey(password, salt, n, r, p) {
    if (typeof password !== 'string') {
        throw new Error('Senha da wallet inválida.');
    }
    return crypto.scryptSync(password, salt, 32, { N: n, r, p, maxmem: SCRYPT_MAXMEM });
}

function aad(did, keyId) {
    return Buffer.from(`custodychain-wallet-v1\0${did}\0${keyId}`, 'utf8');
}

module.exports = { WalletStore };
