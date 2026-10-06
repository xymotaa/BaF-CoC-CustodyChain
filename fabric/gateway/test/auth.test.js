'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');

process.env.GATEWAY_SERVICE_TOKEN = 'token-de-teste-comprido';
const crypto = require('node:crypto');
const {
    autenticarServico, tratarErro, validarComandoDid, verificarProvaDid,
    validarVcPermissao, verificarVcPermissao
} = require('../src/app');

test('middleware aceita bearer token configurado', () => {
    let nextCalled = false;
    const req = { get: () => 'Bearer token-de-teste-comprido' };
    autenticarServico(req, responseFake(), () => { nextCalled = true; });
    assert.equal(nextCalled, true);
});

test('middleware recusa token incorreto sem comparar tamanhos diferentes', () => {
    const response = responseFake();
    const req = { get: () => 'Bearer curto' };
    autenticarServico(req, response, () => assert.fail('next não deveria ser chamado'));
    assert.equal(response.statusCode, 401);
    assert.deepEqual(response.body, { error: 'Credencial de serviço inválida.' });
});

test('valida o contrato mínimo do comando DID antes do Fabric', () => {
    assert.doesNotThrow(() => validarComandoDid({
        type: 'CustodyChainDidRegistration', version: 1,
        commandId: 'urn:uuid:11111111-1111-1111-1111-111111111111',
        audience: 'custodychain-ledger', issuedAt: '2027-01-01T00:00:00.000Z', expiresAt: '2027-01-01T00:05:00.000Z'
    }, 'CustodyChainDidRegistration'));
    assert.throws(() => validarComandoDid({}, 'CustodyChainDidRegistration'), /Contrato/);
});

test('recusa prova DID sem uma chave Multikey válida', () => {
    assert.throws(
        () => verificarProvaDid({ type: 'CustodyChainDidRegistration' }, 'assinatura', 'invalida'),
        /Chave pública/
    );
});

test('classifica migração administrativa repetida como conflito', () => {
    const response = responseFake();
    tratarErro(response, {
        details: [{ message: 'O bootstrap ou a migração do DID administrador já foi concluída.' }]
    });
    assert.equal(response.statusCode, 409);
});

test('valida a assinatura da VC de permissão sobre o envelope sem proof', () => {
    const { privateKey, publicKey } = crypto.generateKeyPairSync('ed25519');
    const rawPublicKey = publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
    const issuer = 'did:legal:admin:teste-vc';
    const credential = {
        '@context': ['https://www.w3.org/2018/credentials/v1'],
        id: 'urn:uuid:11111111-1111-1111-1111-111111111111',
        type: ['VerifiableCredential', 'CustodyChainPermissionCredential'],
        issuer,
        issuanceDate: '2026-10-06T12:00:00.000Z',
        credentialSubject: { id: 'did:legal:expert:teste-vc', perfil: 'PERITO' },
        credentialStatus: { id: 'urn:uuid:11111111-1111-1111-1111-111111111111#status', type: 'CustodyChainLedgerStatusV1' }
    };
    const proofValue = crypto.sign(null, Buffer.from(canonicalizar(credential)), privateKey).toString('base64url');
    const signed = {
        ...credential,
        proof: {
            type: 'CustodyChainEd25519Signature2026', created: credential.issuanceDate,
            proofPurpose: 'assertionMethod', verificationMethod: `${issuer}#auth-1`,
            canonicalization: 'custodychain-json-c14n-v1', proofValue
        }
    };
    const multikey = `z${base58(Buffer.concat([Buffer.from([0xed, 0x01]), rawPublicKey]))}`;

    validarVcPermissao(signed);
    assert.doesNotThrow(() => verificarVcPermissao(signed, multikey));
    assert.throws(() => verificarVcPermissao({
        ...signed, credentialSubject: { ...signed.credentialSubject, perfil: 'ADMIN' }
    }, multikey), /Assinatura/);
});

function canonicalizar(value) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean' || typeof value === 'number') return JSON.stringify(value);
    if (Array.isArray(value)) return `[${value.map(canonicalizar).join(',')}]`;
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalizar(value[key])}`).join(',')}}`;
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

function responseFake() {
    return {
        statusCode: undefined,
        body: undefined,
        status(code) {
            this.statusCode = code;
            return this;
        },
        json(body) {
            this.body = body;
            return this;
        }
    };
}
