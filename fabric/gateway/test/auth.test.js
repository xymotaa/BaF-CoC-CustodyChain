'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');

process.env.GATEWAY_SERVICE_TOKEN = 'token-de-teste-comprido';
const { autenticarServico, validarComandoDid, verificarProvaDid } = require('../src/app');

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
