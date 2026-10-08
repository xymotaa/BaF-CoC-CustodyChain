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

            CREATE TABLE IF NOT EXISTS identity_keys (
                did TEXT NOT NULL,
                key_id TEXT NOT NULL,
                public_key_multibase TEXT NOT NULL,
                encrypted_private_key BLOB NOT NULL,
                salt BLOB NOT NULL,
                iv BLOB NOT NULL,
                auth_tag BLOB NOT NULL,
                scrypt_n INTEGER NOT NULL,
                scrypt_r INTEGER NOT NULL,
                scrypt_p INTEGER NOT NULL,
                key_sequence INTEGER NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('ACTIVE', 'PENDING', 'RETIRED', 'COMPROMISED')),
                candidate_id TEXT UNIQUE,
                created_at TEXT NOT NULL,
                activated_at TEXT,
                retired_at TEXT,
                PRIMARY KEY (did, key_id)
            ) STRICT;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_keys_active_did
                ON identity_keys (did) WHERE status = 'ACTIVE';

            INSERT OR IGNORE INTO identity_keys (
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p,
                key_sequence, status, candidate_id, created_at, activated_at, retired_at
            )
            SELECT
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p,
                1, 'ACTIVE', NULL, created_at, created_at, NULL
            FROM identities;
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

        if (this.database.prepare('SELECT 1 FROM identity_keys WHERE did = ?').get(did)) {
            throw new Error('A identidade já existe nesta wallet.');
        }

        const key = createEncryptedKey({ did, keyId, password });
        this.database.prepare(`
            INSERT INTO identity_keys (
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p,
                key_sequence, status, candidate_id, created_at, activated_at, retired_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, 'ACTIVE', NULL, ?, ?, NULL)
        `).run(
            did,
            keyId,
            key.publicKeyMultibase,
            key.encryptedPrivateKey,
            key.salt,
            key.iv,
            key.authTag,
            SCRYPT_N,
            SCRYPT_R,
            SCRYPT_P,
            key.createdAt,
            key.createdAt
        );

        return { did, keyId, algorithm: 'Ed25519', publicKeyMultibase: key.publicKeyMultibase };
    }

    listIdentities() {
        return this.database.prepare(`
            SELECT did, key_id AS keyId, public_key_multibase AS publicKeyMultibase, created_at AS createdAt
            FROM identity_keys WHERE status = 'ACTIVE' ORDER BY created_at
        `).all().map((identity) => ({ ...identity, algorithm: 'Ed25519' }));
    }

    sign({ did, password, signingInput, keyId = null }) {
        if (typeof signingInput !== 'string' || signingInput.length === 0 || signingInput.length > 16384) {
            throw new Error('Conteúdo de assinatura inválido.');
        }
        const identity = keyId
            ? this.database.prepare(`
                SELECT * FROM identity_keys
                WHERE did = ? AND key_id = ? AND status IN ('ACTIVE', 'PENDING')
            `).get(did, keyId)
            : this.database.prepare(
                "SELECT * FROM identity_keys WHERE did = ? AND status = 'ACTIVE'"
            ).get(did);
        if (!identity) {
            throw new Error('Identidade não encontrada nesta wallet.');
        }

        return signWithKey(identity, password, signingInput);
    }

    createRotationCandidate({ did, password }) {
        const activeKey = this.database.prepare(
            "SELECT * FROM identity_keys WHERE did = ? AND status = 'ACTIVE'"
        ).get(did);
        if (!activeKey) {
            throw new Error('Identidade ativa não encontrada nesta wallet.');
        }
        verifyPassword(activeKey, password);

        const pending = this.database.prepare(
            "SELECT * FROM identity_keys WHERE did = ? AND status = 'PENDING'"
        ).get(did);
        if (pending) {
            return {
                candidateId: pending.candidate_id,
                did,
                keyId: pending.key_id,
                algorithm: 'Ed25519',
                publicKeyMultibase: pending.public_key_multibase
            };
        }

        const sequence = Number(this.database.prepare(
            'SELECT COALESCE(MAX(key_sequence), 0) AS value FROM identity_keys WHERE did = ?'
        ).get(did).value) + 1;
        const keyPrefix = activeKey.key_id.startsWith(`${did}#auth-`) ? 'auth' : 'key';
        const keyId = `${did}#${keyPrefix}-${sequence}`;
        const candidateId = `urn:uuid:${crypto.randomUUID()}`;
        const key = createEncryptedKey({ did, keyId, password });

        this.database.prepare(`
            INSERT INTO identity_keys (
                did, key_id, public_key_multibase, encrypted_private_key,
                salt, iv, auth_tag, scrypt_n, scrypt_r, scrypt_p,
                key_sequence, status, candidate_id, created_at, activated_at, retired_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'PENDING', ?, ?, NULL, NULL)
        `).run(
            did, keyId, key.publicKeyMultibase, key.encryptedPrivateKey,
            key.salt, key.iv, key.authTag, SCRYPT_N, SCRYPT_R, SCRYPT_P,
            sequence, candidateId, key.createdAt
        );

        return {
            candidateId,
            did,
            keyId,
            algorithm: 'Ed25519',
            publicKeyMultibase: key.publicKeyMultibase
        };
    }

    signKeyRotation({ did, password, candidateId, command }) {
        if (!command || typeof command !== 'object' || Array.isArray(command)
            || command.type !== 'CustodyChainDidKeyRotation' || command.version !== 1
            || command.subjectDid !== did
            || command.algorithm !== 'Ed25519'
            || command.canonicalization !== 'custodychain-json-c14n-v1') {
            throw new Error('O comando de rotação não corresponde à identidade selecionada.');
        }

        const currentKey = this.database.prepare(
            "SELECT * FROM identity_keys WHERE did = ? AND status = 'ACTIVE'"
        ).get(did);
        const candidateKey = this.database.prepare(
            "SELECT * FROM identity_keys WHERE did = ? AND candidate_id = ? AND status = 'PENDING'"
        ).get(did, candidateId);
        if (!currentKey || !candidateKey
            || command.currentKeyId !== currentKey.key_id
            || command.newVerificationMethod?.id !== candidateKey.key_id
            || command.newVerificationMethod?.controller !== did
            || command.newVerificationMethod?.type !== 'Multikey'
            || command.newVerificationMethod?.publicKeyMultibase !== candidateKey.public_key_multibase) {
            throw new Error('As chaves do comando de rotação não correspondem à wallet.');
        }

        const signingInput = Buffer.from(canonicalize(command), 'utf8').toString('base64url');
        const currentProof = signWithKey(currentKey, password, signingInput);
        const newKeyProof = signWithKey(candidateKey, password, signingInput);
        return {
            currentKeyProof: currentProof,
            newKeyProof
        };
    }

    confirmKeyRotation({ did, candidateId = null, keyId }) {
        const active = this.database.prepare(
            "SELECT * FROM identity_keys WHERE did = ? AND key_id = ? AND status = 'ACTIVE'"
        ).get(did, keyId);
        if (active) {
            return {
                did,
                keyId,
                algorithm: 'Ed25519',
                publicKeyMultibase: active.public_key_multibase
            };
        }

        const candidate = candidateId
            ? this.database.prepare(`
                SELECT * FROM identity_keys
                WHERE did = ? AND candidate_id = ? AND key_id = ? AND status = 'PENDING'
            `).get(did, candidateId, keyId)
            : this.database.prepare(`
                SELECT * FROM identity_keys
                WHERE did = ? AND key_id = ? AND status = 'PENDING'
            `).get(did, keyId);
        if (!candidate) {
            throw new Error('Candidata de rotação não encontrada nesta wallet.');
        }

        const timestamp = new Date().toISOString();
        this.database.exec('BEGIN IMMEDIATE');
        try {
            this.database.prepare(`
                UPDATE identity_keys SET status = 'RETIRED', retired_at = ?
                WHERE did = ? AND status = 'ACTIVE'
            `).run(timestamp, did);
            this.database.prepare(`
                UPDATE identity_keys SET status = 'ACTIVE', activated_at = ?, candidate_id = NULL
                WHERE did = ? AND key_id = ? AND status = 'PENDING'
            `).run(timestamp, did, keyId);
            this.database.exec('COMMIT');
        } catch (error) {
            this.database.exec('ROLLBACK');
            throw error;
        }

        return {
            did,
            keyId,
            algorithm: 'Ed25519',
            publicKeyMultibase: candidate.public_key_multibase
        };
    }

    discardRotationCandidate({ did, candidateId }) {
        const result = this.database.prepare(
            "DELETE FROM identity_keys WHERE did = ? AND candidate_id = ? AND status = 'PENDING'"
        ).run(did, candidateId);
        if (result.changes !== 1) {
            throw new Error('Candidata de rotação não encontrada nesta wallet.');
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

function createEncryptedKey({ did, keyId, password }) {
    const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
    const publicDer = publicKey.export({ format: 'der', type: 'spki' });
    const publicKeyMultibase = encodeEd25519Multikey(publicDer.subarray(SPKI_ED25519_PREFIX_LENGTH));
    const privateKeyDer = privateKey.export({ format: 'der', type: 'pkcs8' });
    const salt = crypto.randomBytes(16);
    const iv = crypto.randomBytes(12);
    const encryptionKey = deriveKey(password, salt, SCRYPT_N, SCRYPT_R, SCRYPT_P);
    try {
        const cipher = crypto.createCipheriv('aes-256-gcm', encryptionKey, iv);
        cipher.setAAD(aad(did, keyId));
        const encryptedPrivateKey = Buffer.concat([cipher.update(privateKeyDer), cipher.final()]);
        return {
            publicKeyMultibase,
            encryptedPrivateKey,
            salt,
            iv,
            authTag: cipher.getAuthTag(),
            createdAt: new Date().toISOString()
        };
    } finally {
        encryptionKey.fill(0);
        privateKeyDer.fill(0);
    }
}

function decryptPrivateKey(identity, password) {
    const encryptionKey = deriveKey(
        password,
        identity.salt,
        identity.scrypt_n,
        identity.scrypt_r,
        identity.scrypt_p
    );
    try {
        const decipher = crypto.createDecipheriv('aes-256-gcm', encryptionKey, identity.iv);
        decipher.setAAD(aad(identity.did, identity.key_id));
        decipher.setAuthTag(identity.auth_tag);
        return Buffer.concat([
            decipher.update(identity.encrypted_private_key),
            decipher.final()
        ]);
    } catch {
        throw new Error('Senha da wallet inválida.');
    } finally {
        encryptionKey.fill(0);
    }
}

function verifyPassword(identity, password) {
    const privateKeyDer = decryptPrivateKey(identity, password);
    privateKeyDer.fill(0);
}

function signWithKey(identity, password, signingInput) {
    const privateKeyDer = decryptPrivateKey(identity, password);
    try {
        const privateKey = crypto.createPrivateKey({ key: privateKeyDer, format: 'der', type: 'pkcs8' });
        const signature = crypto.sign(null, Buffer.from(signingInput, 'base64url'), privateKey);
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
