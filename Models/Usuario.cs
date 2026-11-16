using System;
using System.Collections.Generic;

namespace AtilsonCargoSpa.Models;

public partial class Usuario
{
    public int Id { get; set; }

    public string NombreCompleto { get; set; } = null!;

    public string Correo { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string Rol { get; set; } = null!;

    // Permiso por persona (nov-2026): administrar y cargar las tarifas de costo de transportistas
    // en Comercial aunque su rol sea Operaciones. Sql/2026-11_ciclo_naves_pagos_permisos.sql.
    public bool PermisoTarifasTransporte { get; set; }



    public byte? Activo { get; set; }

    // NUEVO CAMPO: Conecta al usuario con su empresa (Cliente)
    public int? IdCliente { get; set; }
}