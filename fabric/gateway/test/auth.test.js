'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');

process.env.GATEWAY_SERVICE_TOKEN = 'token-de-teste-comprido';
const { autenticarServico } = require('../src/app');

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
