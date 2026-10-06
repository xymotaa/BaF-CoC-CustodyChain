'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
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

test('migra uma única vez o DID administrativo legado ativo para v2', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const legacyDocument = {
        did,
        metodoDid: 'did:legal:admin',
        ativo: true,
        didEmissor: did,
        criadoEm: '2026-10-01T00:00:00.000Z',
        ativadoEm: '2026-10-01T00:01:00.000Z'
    };
    await ctx.stub.putState(ctx.stub.createCompositeKey('DID', [did]), Buffer.from(JSON.stringify(legacyDocument)));

    const migrated = JSON.parse(await contract.MigrarAdminDidLegadoParaV2(ctx, did, keyId, publicKey));

    assert.equal(migrated.version, 2);
    assert.equal(migrated.migradoDeVersao, 1);
    assert.deepEqual(migrated.capabilityInvocation, [keyId]);
    assert.equal(migrated.criadoEm, legacyDocument.criadoEm);
    await assert.rejects(
        contract.MigrarAdminDidLegadoParaV2(ctx, did, keyId, publicKey),
        /já foi concluída/
    );
});

test('recusa migração de DID legado não administrativo ou inativo', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    await ctx.stub.putState(ctx.stub.createCompositeKey('DID', [did]), Buffer.from(JSON.stringify({
        did,
        metodoDid: 'did:legal:admin',
        ativo: false
    })));

    await assert.rejects(
        contract.MigrarAdminDidLegadoParaV2(ctx, did, keyId, publicKey),
        /legado ativo/
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

test('registra DID titular pendente com prova de posse e o ativa com administrador', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);

    const holderDid = 'did:legal:expert:teste-titular';
    const holder = gerarIdentidade(holderDid, `${holderDid}#key-1`);
    const registration = {
        type: 'CustodyChainDidRegistration', version: 1,
        commandId: 'urn:uuid:11111111-1111-1111-1111-111111111111',
        enrollmentId: 'urn:uuid:22222222-2222-2222-2222-222222222222',
        did: holderDid, metodoDid: 'did:legal:expert',
        verificationMethodId: holder.keyId, publicKeyMultibase: holder.publicKeyMultibase,
        audience: 'custodychain-ledger', issuedAt: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z'
    };
    const pending = JSON.parse(await contract.RegistrarDidV2Pendente(
        ctx, JSON.stringify(registration), assinar(holder.privateKey, registration)));
    assert.equal(pending.status, 'PENDENTE');

    const activation = {
        type: 'CustodyChainDidActivation', version: 1,
        commandId: 'urn:uuid:33333333-3333-3333-3333-333333333333',
        actorDid: did, subjectDid: holderDid, expectedDocumentVersion: 1,
        audience: 'custodychain-ledger', issuedAt: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z'
    };
    const active = JSON.parse(await contract.AtivarDidV2(
        ctx, JSON.stringify(activation), keyId, assinar(admin.privateKey, activation)));
    assert.equal(active.status, 'ATIVO');
    assert.equal(active.didEmissor, did);
});

test('recusa ativação assinada por chave sem capabilityInvocation', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    const stateKey = ctx.stub.createCompositeKey('DID', [did]);
    const document = JSON.parse((await ctx.stub.getState(stateKey)).toString());
    document.capabilityInvocation = [];
    await ctx.stub.putState(stateKey, Buffer.from(JSON.stringify(document)));
    const activation = {
        type: 'CustodyChainDidActivation', version: 1,
        commandId: 'urn:uuid:44444444-4444-4444-4444-444444444444',
        actorDid: did, subjectDid: 'did:legal:expert:inexistente', expectedDocumentVersion: 1,
        audience: 'custodychain-ledger', issuedAt: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z'
    };
    await assert.rejects(
        contract.AtivarDidV2(ctx, JSON.stringify(activation), keyId, assinar(admin.privateKey, activation)),
        /não possui capacidade/
    );
});

test('emite VC de permissão assinada pelo DID emissor e preserva idempotência', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    const credential = criarVcPermissao(admin.privateKey);

    const credentialId = await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));
    const registrado = JSON.parse(await contract.ObterCredencial(ctx, credentialId));

    assert.equal(credentialId, credential.id);
    assert.equal(registrado.formato, 'VC_V1');
    assert.equal(registrado.did, credential.credentialSubject.id);
    assert.equal(registrado.verifiableCredential.proof.proofValue, credential.proof.proofValue);
    assert.equal(await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential)), credential.id);
});

test('recusa emissão de permissão pelo contrato legado sem prova', async () => {
    const contract = new CustodyChainContract();
    await assert.rejects(
        contract.EmitirCredencialPermissao(contexto('Org1MSP'), 'cred-perm-legada', did, did, 'ADMIN'),
        /legada.*desabilitada/
    );
});

test('recusa VC de permissão com prova modificada ou chave sem assertionMethod', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    const credential = criarVcPermissao(admin.privateKey);

    await assert.rejects(
        contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify({
            ...credential,
            credentialSubject: { ...credential.credentialSubject, perfil: 'CUSTODIA' }
        })),
        /assinatura/
    );

    const stateKey = ctx.stub.createCompositeKey('DID', [did]);
    const document = JSON.parse((await ctx.stub.getState(stateKey)).toString());
    document.assertionMethod = [];
    await ctx.stub.putState(stateKey, Buffer.from(JSON.stringify(document)));
    await assert.rejects(
        contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential)),
        /assertionMethod/
    );
});

function criarVcPermissao(privateKey) {
    const credential = {
        '@context': ['https://www.w3.org/2018/credentials/v1'],
        id: 'urn:uuid:55555555-5555-5555-5555-555555555555',
        type: ['VerifiableCredential', 'CustodyChainPermissionCredential'],
        issuer: did,
        issuanceDate: '2027-01-15T08:00:00.000Z',
        expirationDate: '2027-01-16T08:00:00.000Z',
        credentialSubject: { id: 'did:legal:expert:teste-vc', perfil: 'PERITO' },
        credentialStatus: {
            id: 'urn:uuid:55555555-5555-5555-5555-555555555555#status',
            type: 'CustodyChainLedgerStatusV1'
        }
    };
    return {
        ...credential,
        proof: {
            type: 'CustodyChainEd25519Signature2026',
            created: credential.issuanceDate,
            proofPurpose: 'assertionMethod',
            verificationMethod: keyId,
            canonicalization: 'custodychain-json-c14n-v1',
            proofValue: assinar(privateKey, credential)
        }
    };
}

function gerarIdentidade(identityDid, identityKeyId) {
    const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
    const raw = publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
    return {
        keyId: identityKeyId,
        privateKey,
        publicKeyMultibase: `z${base58(Buffer.concat([Buffer.from([0xed, 0x01]), raw]))}`
    };
}

function assinar(privateKey, command) {
    return crypto.sign(null, Buffer.from(canonicalize(command)), privateKey).toString('base64url');
}

function canonicalize(value) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean' || typeof value === 'number') return JSON.stringify(value);
    if (Array.isArray(value)) return `[${value.map(canonicalize).join(',')}]`;
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(',')}}`;
}

function base58(bytes) {
    const alphabet = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    const digits = [0];
    for (const byte of bytes) {
        let carry = byte;
        for (let index = 0; index < digits.length; index += 1) {
            const value = digits[index] * 256 + carry;
            digits[index] = value % 58;
            carry = Math.floor(value / 58);
        }
        while (carry > 0) { digits.push(carry % 58); carry = Math.floor(carry / 58); }
    }
    return digits.reverse().map((digit) => alphabet[digit]).join('');
}

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
