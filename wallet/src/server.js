'use strict';

const crypto = require('node:crypto');
const http = require('node:http');
const path = require('node:path');
const { WalletStore } = require('./walletStore');

const HOST = '127.0.0.1';
const PORT = Number(process.env.WALLET_PORT || 43123);
const DATABASE_PATH = process.env.WALLET_DATABASE_PATH
    || path.resolve(__dirname, '..', 'data', 'wallet.db');
const ALLOWED_ORIGIN = process.env.WALLET_ALLOWED_ORIGIN || 'http://localhost:5143';
const PAIRING_TOKEN = process.env.WALLET_PAIRING_TOKEN || crypto.randomBytes(18).toString('base64url');
const store = new WalletStore(DATABASE_PATH);

function json(res, status, body, origin) {
    const headers = {
        'Content-Type': 'application/json; charset=utf-8',
        'Cache-Control': 'no-store'
    };
    if (origin === ALLOWED_ORIGIN) {
        headers['Access-Control-Allow-Origin'] = origin;
        headers.Vary = 'Origin';
    }
    res.writeHead(status, headers);
    res.end(JSON.stringify(body));
}

function pairingValido(req) {
    const informado = Buffer.from(req.headers['x-wallet-pairing-token'] || '');
    const esperado = Buffer.from(PAIRING_TOKEN);
    return informado.length === esperado.length && crypto.timingSafeEqual(informado, esperado);
}

async function readJson(req) {
    const chunks = [];
    let tamanho = 0;
    for await (const chunk of req) {
        tamanho += chunk.length;
        if (tamanho > 32 * 1024) {
            throw new Error('Requisição excede o limite permitido.');
        }
        chunks.push(chunk);
    }
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

const server = http.createServer(async (req, res) => {
    const origin = req.headers.origin;
    if (origin && origin !== ALLOWED_ORIGIN) {
        return json(res, 403, { message: 'Origem não autorizada pela wallet.' });
    }

    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': ALLOWED_ORIGIN,
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type, X-Wallet-Pairing-Token',
            'Access-Control-Max-Age': '600',
            Vary: 'Origin'
        });
        return res.end();
    }

    if (req.method === 'GET' && req.url === '/health') {
        return json(res, 200, { status: 'ready' }, origin);
    }

    if (!pairingValido(req)) {
        return json(res, 401, { message: 'Código de pareamento inválido.' }, origin);
    }

    try {
        if (req.method === 'GET' && req.url === '/v1/identities') {
            return json(res, 200, { identities: store.listIdentities() }, origin);
        }

        if (req.method === 'POST' && req.url === '/v1/identities') {
            const body = await readJson(req);
            const identity = store.createIdentity({
                did: body.did,
                password: body.password,
                keyId: `${body.did}#key-1`
            });
            return json(res, 201, identity, origin);
        }

        if (req.method === 'POST' && req.url === '/v1/signatures') {
            const body = await readJson(req);
            if (body.purpose !== 'authentication') {
                return json(res, 400, { message: 'Finalidade de assinatura não permitida.' }, origin);
            }
            const signature = store.sign({
                did: body.did,
                password: body.password,
                signingInput: body.signingInput
            });
            return json(res, 200, signature, origin);
        }

        if (req.method === 'POST' && req.url === '/v1/proofs/did-registration') {
            const body = await readJson(req);
            const proof = store.signDidCommand({
                did: body.did,
                password: body.password,
                command: body.command,
                expectedType: 'CustodyChainDidRegistration',
                actorField: 'did'
            });
            return json(res, 200, proof, origin);
        }

        if (req.method === 'POST' && req.url === '/v1/proofs/did-activation') {
            const body = await readJson(req);
            const proof = store.signDidCommand({
                did: body.did,
                password: body.password,
                command: body.command,
                expectedType: 'CustodyChainDidActivation',
                actorField: 'actorDid'
            });
            return json(res, 200, proof, origin);
        }

        if (req.method === 'POST' && req.url === '/v1/proofs/verifiable-credential') {
            const body = await readJson(req);
            const verifiableCredential = store.signVerifiableCredential({
                did: body.did,
                password: body.password,
                credential: body.credential
            });
            return json(res, 200, { verifiableCredential }, origin);
        }

        return json(res, 404, { message: 'Rota não encontrada.' }, origin);
    } catch (error) {
        return json(res, 400, { message: error.message }, origin);
    }
});

server.listen(PORT, HOST, () => {
    console.log(`Wallet CustodyChain disponível em http://${HOST}:${PORT}`);
    console.log(`Código de pareamento: ${PAIRING_TOKEN}`);
});

function shutdown() {
    server.close(() => {
        store.close();
        process.exit(0);
    });
}

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
