(() => {
    function criarErro(message) {
        return new Error(message);
    }

    async function lerResposta(response, mensagemPadrao) {
        const payload = response.status === 204
            ? {}
            : await response.json().catch(() => ({}));

        if (!response.ok) {
            throw criarErro(payload.message || payload.error || mensagemPadrao);
        }

        return payload;
    }

    async function requisicaoAplicacao({ url, token, method = "POST", body, mensagemErro }) {
        const headers = {};
        if (token) {
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) {
            headers["Content-Type"] = "application/json";
        }

        try {
            const response = await fetch(url, {
                method,
                credentials: "same-origin",
                headers,
                body: body === undefined ? undefined : JSON.stringify(body)
            });
            return await lerResposta(response, mensagemErro);
        } catch (error) {
            if (error instanceof TypeError) {
                throw criarErro("Não foi possível comunicar-se com o CustodyChain. Tente novamente.");
            }
            throw error;
        }
    }

    async function requisicaoFormulario({ url, token, body, mensagemErro }) {
        const headers = {};
        if (token) {
            headers.RequestVerificationToken = token;
        }

        try {
            const response = await fetch(url, {
                method: "POST",
                credentials: "same-origin",
                headers,
                body
            });
            return await lerResposta(response, mensagemErro);
        } catch (error) {
            if (error instanceof TypeError) {
                throw criarErro("Não foi possível comunicar-se com o CustodyChain. Tente novamente.");
            }
            throw error;
        }
    }

    async function requisicaoWallet({ endpoint, path, pairingToken, body, mensagemErro }) {
        try {
            const response = await fetch(`${endpoint}${path}`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Wallet-Pairing-Token": pairingToken
                },
                body: JSON.stringify(body)
            });
            return await lerResposta(response, mensagemErro);
        } catch (error) {
            if (error instanceof TypeError) {
                throw criarErro("A wallet local não respondeu. Inicie-a e tente novamente.");
            }
            throw error;
        }
    }

    function obterTokenAntiforgery(container = document) {
        return container.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";
    }

    function definirStatus(elemento, mensagem, estado = "processando") {
        elemento.textContent = mensagem;
        elemento.dataset.estado = estado;
    }

    function exibirErro(elemento, mensagem, destinoTexto = elemento) {
        destinoTexto.textContent = mensagem;
        elemento.hidden = false;
        elemento.focus();
    }

    function limparErro(elemento, destinoTexto = elemento) {
        elemento.hidden = true;
        destinoTexto.textContent = "";
    }

    function limparCamposSensíveis(...campos) {
        campos.filter(Boolean).forEach(campo => {
            campo.value = "";
        });
    }

    function definirProcessamento({ botao, painel, processando }) {
        botao.disabled = processando;
        botao.setAttribute("aria-busy", String(processando));

        if (painel) {
            painel.setAttribute("aria-busy", String(processando));
        }
    }

    function camposPreenchidos(...campos) {
        return campos.every(campo => campo?.value?.trim());
    }

    window.CustodyChainWallet = Object.freeze({
        requisicaoAplicacao,
        requisicaoFormulario,
        requisicaoWallet,
        obterTokenAntiforgery,
        definirStatus,
        exibirErro,
        limparErro,
        limparCamposSensíveis,
        definirProcessamento,
        camposPreenchidos
    });
})();
