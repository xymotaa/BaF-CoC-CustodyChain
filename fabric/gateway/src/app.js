'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const grpc = require('@grpc/grpc-js');
const { connect, signers, hash } = require('@hyperledger/fabric-gateway');
const express = require('express');
const cors = require('cors');

const CHANNEL_NAME = process.env.CHANNEL_NAME || 'mychannel';
const CHAINCODE_NAME = process.env.CHAINCODE_NAME || 'custodychain';
const MSP_ID = process.env.MSP_ID || 'Org1MSP';
const PEER_ENDPOINT = process.env.PEER_ENDPOINT || 'localhost:7051';
const PEER_HOST_ALIAS = process.env.PEER_HOST_ALIAS || 'peer0.org1.example.com';
const PORT = process.env.PORT || 3000;
const SERVICE_TOKEN = process.env.GATEWAY_SERVICE_TOKEN || '';
const ALLOWED_ORIGINS = (process.env.ALLOWED_ORIGINS || 'http://localhost:5143')
    .split(',')
    .map((origin) => origin.trim())
    .filter(Boolean);

const CRYPTO_PATH = process.env.CRYPTO_PATH
    || path.resolve(__dirname, '..', '..', 'network', 'organizations', 'peerOrganizations', 'org1.example.com');
const TLS_CERT_PATH = process.env.TLS_CERT_PATH
    || path.resolve(CRYPTO_PATH, 'peers', 'peer0.org1.example.com', 'tls', 'ca.crt');
const MSP_PATH = process.env.MSP_PATH
    || path.resolve(CRYPTO_PATH, 'users', 'User1@org1.example.com', 'msp');

let gateway;
let client;

function lerPrimeiroArquivo(diretorio) {
    const [arquivo] = fs.readdirSync(diretorio);
    return fs.readFileSync(path.resolve(diretorio, arquivo));
}

async function novaConexaoGrpc() {
    const tlsRootCert = fs.readFileSync(TLS_CERT_PATH);
    const credenciaisTls = grpc.credentials.createSsl(tlsRootCert);
    return new grpc.Client(PEER_ENDPOINT, credenciaisTls, {
        'grpc.ssl_target_name_override': PEER_HOST_ALIAS
    });
}

function novaIdentidade() {
    const certPath = path.resolve(MSP_PATH, 'signcerts');
    const credentials = lerPrimeiroArquivo(certPath);
    return { mspId: MSP_ID, credentials };
}

function novoAssinante() {
    const keyPath = path.resolve(MSP_PATH, 'keystore');
    const chavePrivadaPem = lerPrimeiroArquivo(keyPath);
    const chavePrivada = crypto.createPrivateKey(chavePrivadaPem);
    return signers.newPrivateKeySigner(chavePrivada);
}

async function inicializarGateway() {
    client = await novaConexaoGrpc();
    gateway = connect({
        client,
        identity: novaIdentidade(),
        signer: novoAssinante(),
        hash: hash.sha256,
        evaluateOptions: () => ({ deadline: Date.now() + 5000 }),
        endorseOptions: () => ({ deadline: Date.now() + 15000 }),
        submitOptions: () => ({ deadline: Date.now() + 5000 }),
        commitStatusOptions: () => ({ deadline: Date.now() + 60000 })
    });
}

function obterContrato() {
    const network = gateway.getNetwork(CHANNEL_NAME);
    return network.getContract(CHAINCODE_NAME);
}

function tratarErro(res, erro) {
    const mensagem = erro && erro.details ? erro.details : (erro && erro.message) || 'Erro desconhecido no gateway.';
    const status = mensagem.includes('não encontrado') ? 404
        : mensagem.includes('já existe') || mensagem.includes('já foi concluído') ? 409
            : mensagem.includes('inválid') || mensagem.includes('não autorizado') ? 400
                : 500;
    res.status(status).json({ error: mensagem });
}

function autenticarServico(req, res, next) {
    const authorization = req.get('authorization') || '';
    const recebido = authorization.startsWith('Bearer ') ? authorization.slice(7) : '';
    const esperado = Buffer.from(SERVICE_TOKEN);
    const informado = Buffer.from(recebido);
    if (!SERVICE_TOKEN || esperado.length !== informado.length
        || !crypto.timingSafeEqual(esperado, informado)) {
        return res.status(401).json({ error: 'Credencial de serviço inválida.' });
    }
    return next();
}

function corpoObrigatorio(req, campos) {
    for (const campo of campos) {
        if (typeof req.body[campo] !== 'string' || !req.body[campo].trim()) {
            throw new Error(`Campo obrigatório inválido: ${campo}`);
        }
    }
}

// evaluateTransaction/submitTransaction retornam Uint8Array, não Buffer —
// .toString() direto produz a lista de códigos de byte, não o texto.
function decodificar(resultadoBruto) {
    return Buffer.from(resultadoBruto).toString('utf8');
}

const app = express();
app.use(cors({
    origin(origin, callback) {
        if (!origin || ALLOWED_ORIGINS.includes(origin)) {
            return callback(null, true);
        }
        return callback(new Error('Origem não autorizada pelo gateway.'));
    }
}));
app.use(express.json());

app.get('/saude', (_req, res) => {
    res.json({ status: gateway ? 'conectado' : 'desconectado' });
});

app.use(autenticarServico);

app.post('/v2/bootstrap/admin', async (req, res) => {
    try {
        corpoObrigatorio(req, ['did', 'verificationMethodId', 'publicKeyMultibase']);
        const { did, verificationMethodId, publicKeyMultibase } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction(
            'BootstrapAdminDid', did, verificationMethodId, publicKeyMultibase
        );
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/v2/dids/:did', async (req, res) => {
    try {
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ResolverDid', req.params.did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/v2/dids/:did/revogar', async (req, res) => {
    try {
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('RevogarDidV2', req.params.did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/dids', async (req, res) => {
    try {
        const { did, metodoDid } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('GerarDid', did, metodoDid);
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/dids/:did/ativar', async (req, res) => {
    try {
        const { did } = req.params;
        const { didEmissor } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('AtivarDid', did, didEmissor);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/dids/:did', async (req, res) => {
    try {
        const { did } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ResolverDid', did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/permissao', async (req, res) => {
    try {
        const { credencialId, did, didEmissor, perfil } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction(
            'EmitirCredencialPermissao', credencialId, did, didEmissor, perfil
        );
        res.status(201).json({ credencialId: decodificar(resultado) });
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/coc', async (req, res) => {
    try {
        const { credencialId, assetId, evento, did, payloadHashSha256 } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction(
            'EmitirCredencialCoC', credencialId, assetId, evento, did, payloadHashSha256
        );
        res.status(201).json({ credencialId: decodificar(resultado) });
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/credenciais/:credencialId/verificar', async (req, res) => {
    try {
        const { credencialId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('VerificarCredencial', credencialId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/credenciais/:credencialId', async (req, res) => {
    try {
        const { credencialId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ObterCredencial', credencialId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/:credencialId/revogar', async (req, res) => {
    try {
        const { credencialId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('RevogarCredencial', credencialId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/ativos/:assetId/historico', async (req, res) => {
    try {
        const { assetId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('HistoricoRegistro', assetId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

async function iniciar() {
    if (!SERVICE_TOKEN) {
        throw new Error('GATEWAY_SERVICE_TOKEN é obrigatório.');
    }
    await inicializarGateway();
    app.listen(PORT, () => {
        console.log(`Gateway CustodyChain ouvindo na porta ${PORT} (canal=${CHANNEL_NAME}, chaincode=${CHAINCODE_NAME})`);
    });
}

if (require.main === module) {
    iniciar().catch((erro) => {
        console.error('Falha ao iniciar o gateway:', erro);
        process.exit(1);
    });
}

process.on('SIGINT', () => {
    if (client) client.close();
    process.exit(0);
});

module.exports = { app, autenticarServico, decodificar };
