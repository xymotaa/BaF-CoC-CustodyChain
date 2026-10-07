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
    await registrarPeritoAtivo(ctx);
    const credential = criarVcPermissao(admin.privateKey);

    const credentialId = await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));
    const registrado = JSON.parse(await contract.ObterCredencial(ctx, credentialId));

    assert.equal(credentialId, credential.id);
    assert.equal(registrado.formato, 'VC_V1');
    assert.equal(registrado.did, credential.credentialSubject.id);
    assert.equal(registrado.verifiableCredential.proof.proofValue, credential.proof.proofValue);
    assert.equal(await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential)), credential.id);

    const novaProva = {
        ...credential,
        proof: {
            ...credential.proof,
            created: '2027-01-15T08:01:00.000Z',
            proofValue: assinar(admin.privateKey, (({ proof, ...semProva }) => semProva)(credential))
        }
    };
    assert.equal(await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(novaProva)), credential.id);

    const { proof, ...envelopeDiferente } = {
        ...credential,
        credentialSubject: { ...credential.credentialSubject, perfil: 'ADMIN' }
    };
    const credencialDiferente = {
        ...envelopeDiferente,
        proof: { ...credential.proof, proofValue: assinar(admin.privateKey, envelopeDiferente) }
    };
    await assert.rejects(
        contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credencialDiferente)),
        /Conflito de idempotência/
    );
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
    await registrarPeritoAtivo(ctx);
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

test('revoga VC somente com prova assinada pelo emissor e aceita repetição', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    await registrarPeritoAtivo(ctx);
    const credential = criarVcPermissao(admin.privateKey);
    await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));
    const command = {
        type: 'CustodyChainCredentialRevocation', version: 1,
        commandId: 'urn:uuid:66666666-6666-6666-6666-666666666666',
        credentialId: credential.id, issuerDid: did, audience: 'custodychain-ledger',
        issuedAt: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z'
    };

    const revoked = JSON.parse(await contract.RevogarCredencialV2(ctx, JSON.stringify(command), keyId, assinar(admin.privateKey, command)));
    const repeated = JSON.parse(await contract.RevogarCredencialV2(ctx, JSON.stringify(command), keyId, assinar(admin.privateKey, command)));

    assert.equal(revoked.status, 'REVOGADA');
    assert.equal(repeated.revogada, true);
    assert.equal(JSON.parse(await contract.VerificarCredencial(ctx, credential.id)).valido, false);
});

test('registra VC pericial com escopo de processo, vestígio e operações', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    await registrarPeritoAtivo(ctx);
    const credential = criarVcPermissao(admin.privateKey, {
        processoId: '10',
        assetId: '42',
        operations: ['PERICIA_RECEBER', 'LACRE_ROMPER', 'LAUDO_EMITIR']
    });

    const credentialId = await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));
    const registered = JSON.parse(await contract.ObterCredencial(ctx, credentialId));

    assert.equal(registered.processoId, '10');
    assert.equal(registered.assetId, '42');
    assert.deepEqual(registered.operacoes, ['PERICIA_RECEBER', 'LACRE_ROMPER', 'LAUDO_EMITIR']);
});

test('recusa VC pericial com operação fora da política', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    await registrarPeritoAtivo(ctx);
    const credential = criarVcPermissao(admin.privateKey, {
        processoId: '10', assetId: '42', operations: ['OPERACAO_INEXISTENTE']
    });

    await assert.rejects(
        contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential)),
        /Envelope/
    );
});

test('registra operação assinada v1, preserva idempotência e recusa conflito', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    const signerDid = 'did:legal:expert:teste-operacao';
    const signer = gerarIdentidade(signerDid, `${signerDid}#key-1`);
    await registrarIdentidadeAtiva(ctx, signerDid, 'did:legal:expert', signer);
    const credential = criarVcPermissao(admin.privateKey, {
        processoId: '10', assetId: '42', operations: ['LAUDO_EMITIR']
    }, signerDid);
    await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));
    const operation = {
        type: 'CustodyChainSignedOperation', version: 1,
        operationId: 'urn:uuid:77777777-7777-7777-7777-777777777777',
        operation: 'LAUDO_EMITIR',
        payload: {
            credentialId: credential.id, assetId: '42', processoId: '10', periciaId: '17',
            numeroLaudo: 'LAUDO-2027-000017', hashLaudo: 'a'.repeat(64), hashVestigio: 'b'.repeat(64)
        },
        signerDid, keyId: signer.keyId, algorithm: 'Ed25519',
        canonicalization: 'custodychain-json-c14n-v1', audience: 'custodychain-ledger',
        timestamp: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z',
        nonce: '0123456789abcdefghijkl'
    };
    const signed = { ...operation, signature: assinar(signer.privateKey, operation) };

    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify(signed)), operation.operationId);
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify(signed)), operation.operationId);

    const changed = {
        ...operation,
        payload: { ...operation.payload, hashLaudo: 'b'.repeat(64) }
    };
    await assert.rejects(
        contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
            ...changed, signature: assinar(signer.privateKey, changed)
        })),
        /Conflito de idempotência/
    );

    ctx.definirHorario('2027-01-15T08:06:00.000Z');
    assert.equal(
        await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify(signed)),
        operation.operationId
    );

    ctx.definirHorario('2027-01-15T08:00:00.000Z');
    const foraDoEscopo = {
        ...operation,
        operationId: 'urn:uuid:88888888-8888-8888-8888-888888888888',
        payload: { ...operation.payload, assetId: '43' }
    };
    await assert.rejects(
        contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
            ...foraDoEscopo, signature: assinar(signer.privateKey, foraDoEscopo)
        })),
        /não autoriza/
    );
});

test('autoriza recebimento e rompimento somente no escopo da VC pericial', async () => {
    const contract = new CustodyChainContract();
    const ctx = contexto('Org1MSP');
    const admin = gerarIdentidade(did, keyId);
    await contract.BootstrapAdminDid(ctx, did, keyId, admin.publicKeyMultibase);
    const signerDid = 'did:legal:expert:teste-fatia4a';
    const signer = gerarIdentidade(signerDid, `${signerDid}#key-1`);
    await registrarIdentidadeAtiva(ctx, signerDid, 'did:legal:expert', signer);
    const credential = criarVcPermissao(admin.privateKey, {
        processoId: '10', assetId: '42', operations: [
            'PERICIA_RECEBER', 'LACRE_ROMPER', 'AMOSTRA_CONSUMIR', 'AMOSTRA_EXAURIR', 'AMOSTRA_FRACIONAR'
        ]
    }, signerDid);
    await contract.EmitirCredencialPermissaoV2(ctx, JSON.stringify(credential));

    const envelope = (operationId, operation, payload) => ({
        type: 'CustodyChainSignedOperation', version: 1, operationId, operation, payload,
        signerDid, keyId: signer.keyId, algorithm: 'Ed25519',
        canonicalization: 'custodychain-json-c14n-v1', audience: 'custodychain-ledger',
        timestamp: '2027-01-15T08:00:00.000Z', expiresAt: '2027-01-15T08:05:00.000Z',
        nonce: '0123456789abcdefghijkl'
    });
    const receber = envelope('urn:uuid:99999999-9999-9999-9999-999999999999', 'PERICIA_RECEBER', {
        credentialId: credential.id, processoId: '10', periciaId: '17', assetId: '42'
    });
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
        ...receber, signature: assinar(signer.privateKey, receber)
    })), receber.operationId);

    const romper = envelope('urn:uuid:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'LACRE_ROMPER', {
        credentialId: credential.id, processoId: '10', periciaId: '17', assetId: '42',
        lacreId: '9', numeroLacre: 'L-001', justificativa: 'Abertura para exame técnico'
    });
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
        ...romper, signature: assinar(signer.privateKey, romper)
    })), romper.operationId);

    const consumir = envelope('urn:uuid:cccccccc-cccc-cccc-cccc-cccccccccccc', 'AMOSTRA_CONSUMIR', {
        credentialId: credential.id, processoId: '10', periciaId: '17', assetId: '42',
        quantidadeDescrita: '10 g', justificativa: 'Análise técnica'
    });
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
        ...consumir, signature: assinar(signer.privateKey, consumir)
    })), consumir.operationId);

    const exaurir = envelope('urn:uuid:dddddddd-dddd-dddd-dddd-dddddddddddd', 'AMOSTRA_EXAURIR', {
        credentialId: credential.id, processoId: '10', periciaId: '17', assetId: '42',
        quantidadeDescrita: null, justificativa: 'Material integralmente utilizado'
    });
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
        ...exaurir, signature: assinar(signer.privateKey, exaurir)
    })), exaurir.operationId);

    const fracionar = envelope('urn:uuid:eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 'AMOSTRA_FRACIONAR', {
        credentialId: credential.id, processoId: '10', periciaId: '17', assetId: '42',
        hashVestigio: 'a'.repeat(64), rotuloEvidenciaResultante: 'RE-002',
        descricaoResultante: 'Fragmento analisado', quantidadeDescrita: '10 g', justificativa: 'Separação técnica'
    });
    assert.equal(await contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
        ...fracionar, signature: assinar(signer.privateKey, fracionar)
    })), fracionar.operationId);

    const lacreInvalido = { ...romper, operationId: 'urn:uuid:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', payload: { ...romper.payload, lacreId: '0' } };
    await assert.rejects(
        contract.RegistrarOperacaoAssinadaV1(ctx, JSON.stringify({
            ...lacreInvalido, signature: assinar(signer.privateKey, lacreInvalido)
        })),
        /LACRE_ROMPER inválido/
    );
});

function criarVcPermissao(privateKey, authorization, subjectDid = 'did:legal:expert:teste-vc') {
    const credential = {
        '@context': ['https://www.w3.org/2018/credentials/v1'],
        id: 'urn:uuid:55555555-5555-5555-5555-555555555555',
        type: ['VerifiableCredential', 'CustodyChainPermissionCredential'],
        issuer: did,
        issuanceDate: '2027-01-15T08:00:00.000Z',
        expirationDate: '2027-01-16T08:00:00.000Z',
        credentialSubject: {
            id: subjectDid,
            perfil: 'PERITO',
            ...(authorization ? { authorization } : {})
        },
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

async function registrarPeritoAtivo(ctx) {
    const holderDid = 'did:legal:expert:teste-vc';
    await ctx.stub.putState(ctx.stub.createCompositeKey('DID', [holderDid]), Buffer.from(JSON.stringify({
        id: holderDid,
        did: holderDid,
        metodoDid: 'did:legal:expert',
        version: 2,
        status: 'ATIVO',
        ativo: true
    })));
}

async function registrarIdentidadeAtiva(ctx, identityDid, metodoDid, identity) {
    await ctx.stub.putState(ctx.stub.createCompositeKey('DID', [identityDid]), Buffer.from(JSON.stringify({
        id: identityDid,
        did: identityDid,
        metodoDid,
        version: 2,
        status: 'ATIVO',
        ativo: true,
        verificationMethod: [{
            id: identity.keyId,
            type: 'Multikey',
            controller: identityDid,
            publicKeyMultibase: identity.publicKeyMultibase
        }],
        capabilityInvocation: [identity.keyId]
    })));
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
    let horarioAtual = new Date('2027-01-15T08:00:00.000Z');
    return {
        clientIdentity: { getMSPID: () => mspId },
        definirHorario: (iso) => { horarioAtual = new Date(iso); },
        stub: {
            createCompositeKey: (prefix, values) => `${prefix}:${values.join(':')}`,
            getState: async (key) => state.get(key) || Buffer.alloc(0),
            putState: async (key, value) => state.set(key, value),
            getTxTimestamp: () => ({
                seconds: { low: Math.floor(horarioAtual.getTime() / 1000) },
                nanos: (horarioAtual.getTime() % 1000) * 1_000_000
            })
        }
    };
}
