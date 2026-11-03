/* Menú en celular y tablet (nov-2026), ERP y portal de clientes.
 * Bajo 992 px el menú lateral se esconde y se abre con el botón de la barra superior
 * (.barra-movil). Se cierra con el fondo oscuro, la tecla Escape, el botón del menú o
 * al ensanchar la ventana. */
(function () {
    'use strict';
    var body = document.body;
    var abrir = document.getElementById('menuMovilAbrir');
    var fondo = document.getElementById('menuMovilFondo');
    var toggle = document.getElementById('sidebarToggle');
    function esMovil() { return window.innerWidth < 992; }
    function menuMovil(abierto) {
        body.classList.toggle('menu-movil-abierto', abierto);
        if (abrir) abrir.setAttribute('aria-expanded', abierto ? 'true' : 'false');
        if (abierto) { var primero = document.querySelector('.sidebar .nav-link'); if (primero) primero.focus({ preventScroll: true }); }
    }
    window.menuMovil = menuMovil;
    if (abrir) abrir.addEventListener('click', function () { menuMovil(true); });
    if (fondo) fondo.addEventListener('click', function () { menuMovil(false); });
    // En el celular el botón ☰ del menú lo cierra (en escritorio lo contrae, ver el layout).
    if (toggle) toggle.addEventListener('click', function (e) {
        if (!esMovil()) return;
        e.stopImmediatePropagation();
        menuMovil(false);
    }, true);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && body.classList.contains('menu-movil-abierto')) menuMovil(false); });
    window.addEventListener('resize', function () { if (!esMovil()) menuMovil(false); });
    if (esMovil()) body.classList.remove('sidebar-collapsed');
})();
