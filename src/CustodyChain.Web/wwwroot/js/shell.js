(() => {
    const toggle = document.getElementById("sidebar-toggle");
    const sidebar = document.getElementById("sidebar-principal");
    const scrim = document.querySelector(".sidebar-scrim");

    if (!toggle || !sidebar || !scrim) {
        return;
    }

    const breakpointMobile = window.matchMedia("(max-width: 56.25rem)");
    const seletorFoco = "a[href], button:not([disabled]), [tabindex]:not([tabindex='-1'])";
    let aberto = false;

    function ehMobile() {
        return breakpointMobile.matches;
    }

    function atualizarEstado() {
        const menuAberto = ehMobile() && aberto;
        document.body.classList.toggle("sidebar-aberta", menuAberto);
        toggle.setAttribute("aria-expanded", String(menuAberto));
        toggle.setAttribute("aria-label", menuAberto ? "Fechar menu de navegação" : "Abrir menu de navegação");
        sidebar.setAttribute("aria-hidden", String(ehMobile() && !menuAberto));
        sidebar.inert = ehMobile() && !menuAberto;
    }

    function abrirMenu() {
        if (!ehMobile()) {
            return;
        }

        aberto = true;
        atualizarEstado();
        requestAnimationFrame(() => sidebar.querySelector(seletorFoco)?.focus());
    }

    function fecharMenu(devolverFoco = false) {
        aberto = false;
        atualizarEstado();

        if (devolverFoco) {
            toggle.focus();
        }
    }

    toggle.addEventListener("click", () => {
        if (aberto) {
            fecharMenu();
            return;
        }

        abrirMenu();
    });

    scrim.addEventListener("click", () => fecharMenu(true));

    sidebar.addEventListener("click", event => {
        if (event.target.closest("a")) {
            fecharMenu();
        }
    });

    document.addEventListener("keydown", event => {
        if (!ehMobile() || !aberto) {
            return;
        }

        if (event.key === "Escape") {
            event.preventDefault();
            fecharMenu(true);
            return;
        }

        if (event.key !== "Tab") {
            return;
        }

        const controles = [...sidebar.querySelectorAll(seletorFoco)];
        const primeiro = controles[0];
        const ultimo = controles.at(-1);

        if (!primeiro || !ultimo) {
            return;
        }

        if (event.shiftKey && document.activeElement === primeiro) {
            event.preventDefault();
            ultimo.focus();
        } else if (!event.shiftKey && document.activeElement === ultimo) {
            event.preventDefault();
            primeiro.focus();
        }
    });

    breakpointMobile.addEventListener("change", () => {
        aberto = false;
        atualizarEstado();
    });

    atualizarEstado();
})();
