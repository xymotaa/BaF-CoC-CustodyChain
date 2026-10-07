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
        return this.createIdentity({ did, password, keyId: `${did}#auth-1`, onlyAdmin: true });
    }

    createIdentity({ did, password, keyId = `${did}#key-1`, onlyAdmin = false }) {
        const padraoDid = onlyAdmin
            ? /^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/
            : /^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/;
        if (!padraoDid.test(did) || keyId !== `${did}#key-1` && keyId !== `${did}#auth-1`) {
            throw new Error('O DID ou o identificador de chave não segue o formato permitido.');
        }
        if (typeof password !== 'string' || password.length < 12) {
            throw new Error('A senha da wallet deve ter pelo menos 12 caracteres.');
        }

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

    signDidCommand({ did, password, command, expectedType, actorField }) {
        if (!command || typeof command !== 'object' || Array.isArray(command)
            || command.type !== expectedType || command.version !== 1
            || command[actorField] !== did) {
            throw new Error('O comando DID não corresponde à identidade selecionada.');
        }
        const signingInput = Buffer.from(canonicalize(command), 'utf8').toString('base64url');
        return this.sign({ did, password, signingInput });
    }

    signSignedOperation({ did, password, operation }) {
        if (!operation || typeof operation !== 'object' || Array.isArray(operation)
            || Object.hasOwn(operation, 'signature')
            || operation.type !== 'CustodyChainSignedOperation'
            || operation.version !== 1
            || operation.signerDid !== did
            || operation.algorithm !== 'Ed25519'
            || operation.canonicalization !== 'custodychain-json-c14n-v1'
            || typeof operation.keyId !== 'string') {
            throw new Error('A operação não corresponde ao contrato de assinatura da wallet.');
        }

        const assinatura = this.sign({
            did,
            password,
            signingInput: Buffer.from(canonicalize(operation), 'utf8').toString('base64url')
        });
        if (operation.keyId !== assinatura.keyId) {
            throw new Error('A operação não corresponde à chave ativa da identidade selecionada.');
        }

        return { ...operation, signature: assinatura.signature };
    }

    signVerifiableCredential({ did, password, credential }) {
        if (!credential || typeof credential !== 'object' || Array.isArray(credential)
            || Object.hasOwn(credential, 'proof') || credential.issuer !== did
            || !Array.isArray(credential.type)
            || !credential.type.includes('VerifiableCredential')
            || !credential.type.includes('CustodyChainPermissionCredential')) {
            throw new Error('A credencial não corresponde à identidade emissora selecionada.');
        }

        const assinatura = this.sign({
            did,
            password,
            signingInput: Buffer.from(canonicalize(credential), 'utf8').toString('base64url')
        });

        return {
            ...credential,
            proof: {
                type: 'CustodyChainEd25519Signature2026',
                created: new Date().toISOString(),
                proofPurpose: 'assertionMethod',
                verificationMethod: assinatura.keyId,
                canonicalization: 'custodychain-json-c14n-v1',
                proofValue: assinatura.signature
            }
        };
    }

    close() {
        this.database.close();
    }
}

function canonicalize(value) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') {
        return JSON.stringify(value);
    }
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) {
            throw new Error('Números não finitos não podem ser assinados.');
        }
        return JSON.stringify(value);
    }
    if (Array.isArray(value)) {
        return `[${value.map(canonicalize).join(',')}]`;
    }
    if (typeof value === 'object') {
        return `{${Object.keys(value).sort().map((key) =>
            `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(',')}}`;
    }
    throw new Error('Tipo inválido no comando DID.');
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

module.exports = { WalletStore, canonicalize };
