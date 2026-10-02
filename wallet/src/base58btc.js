'use strict';

const ALFABETO = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

function encodeBase58(bytes) {
    if (!Buffer.isBuffer(bytes)) {
        bytes = Buffer.from(bytes);
    }

    const digitos = [0];
    for (const byte of bytes) {
        let transporte = byte;
        for (let indice = 0; indice < digitos.length; indice += 1) {
            const valor = digitos[indice] * 256 + transporte;
            digitos[indice] = valor % 58;
            transporte = Math.floor(valor / 58);
        }
        while (transporte > 0) {
            digitos.push(transporte % 58);
            transporte = Math.floor(transporte / 58);
        }
    }

    let zeros = 0;
    while (zeros < bytes.length && bytes[zeros] === 0) {
        zeros += 1;
    }

    let resultado = '1'.repeat(zeros);
    for (let indice = digitos.length - 1; indice >= 0; indice -= 1) {
        resultado += ALFABETO[digitos[indice]];
    }
    return resultado;
}

function encodeEd25519Multikey(publicKeyRaw) {
    if (publicKeyRaw.length !== 32) {
        throw new Error('Uma chave pública Ed25519 deve ter 32 bytes.');
    }
    return `z${encodeBase58(Buffer.concat([Buffer.from([0xed, 0x01]), publicKeyRaw]))}`;
}

module.exports = { encodeBase58, encodeEd25519Multikey };
