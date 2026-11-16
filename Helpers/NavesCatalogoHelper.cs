using AtilsonCargoSpa.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Helpers
{
    // Catálogo de naves (tabla dbo.naves, nov-2026). Las naves se eligen de esta lista al agregar
    // una salida en Itinerarios, para no tener "MSC ANNA" y "MSC ANA" como dos naves distintas.
    // La salida sigue guardando el nombre como texto (ItinerariosNaves.Nave): el zarpe, los roleos
    // y el buscador de Create no cambian. El N° de viaje no va aquí: es distinto en cada salida.
    public static class NavesCatalogoHelper
    {
        // Mayúsculas, sin espacios dobles ni en los extremos (mismo largo máximo que la columna).
        public static string Normalizar(string? nombre)
        {
            string n = Regex.Replace((nombre ?? "").Trim(), @"\s+", " ").ToUpperInvariant();
            return n.Length > 100 ? n[..100] : n;
        }

        // Para comparar nombres parecidos: sin tildes, espacios ni signos.
        private static string Clave(string nombre)
        {
            var sb = new StringBuilder();
            foreach (char c in Normalizar(nombre).Normalize(NormalizationForm.FormD))
                if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString();
        }

        public static Task<List<Nafe>> ActivasAsync(AtilsonContext db) =>
            db.Naves.AsNoTracking().Where(n => !n.IsDeleted).OrderBy(n => n.Nombre).ToListAsync();

        // La misma nave ya registrada (de esta naviera o, si no, de otra: una salida puede venderla
        // una naviera distinta de la que opera el buque).
        public static Nafe? Existente(IEnumerable<Nafe> catalogo, string nombre, int? idNaviera)
        {
            string n = Normalizar(nombre);
            var iguales = catalogo.Where(x => !x.IsDeleted && Normalizar(x.Nombre) == n).ToList();
            return iguales.FirstOrDefault(x => x.IdNaviera == idNaviera) ?? iguales.FirstOrDefault();
        }

        // Nombres muy parecidos (un error de tipeo, una letra de más o de menos) que no son el mismo.
        public static List<Nafe> Parecidas(IEnumerable<Nafe> catalogo, string nombre, int max = 4)
        {
            string clave = Clave(nombre);
            if (clave.Length < 3) return new List<Nafe>();
            int tolerancia = clave.Length <= 6 ? 1 : 2;
            return catalogo
                .Where(x => !x.IsDeleted)
                .Select(x => new { Nave = x, Clave = Clave(x.Nombre) })
                .Where(x => x.Clave != clave
                    && (Distancia(x.Clave, clave) <= tolerancia
                        || (Math.Min(x.Clave.Length, clave.Length) >= 5 && (x.Clave.Contains(clave) || clave.Contains(x.Clave)))))
                .OrderBy(x => Distancia(x.Clave, clave))
                .Select(x => x.Nave)
                .Take(max)
                .ToList();
        }

        // Asegura que la nave esté en el catálogo. Si no está (en ninguna naviera), la agrega al
        // contexto con la naviera indicada, sin guardar: quien llama hace SaveChanges junto con la
        // salida. "catalogo" se actualiza para las filas siguientes de una carga masiva.
        public static (Nafe Nave, bool Creada) Asegurar(AtilsonContext db, List<Nafe> catalogo, string nombre, int idNaviera, string? usuario)
        {
            var existente = Existente(catalogo, nombre, idNaviera);
            if (existente != null) return (existente, false);
            var ahora = DateTime.Now;
            var nueva = new Nafe
            {
                Nombre = Normalizar(nombre),
                IdNaviera = idNaviera,
                IsDeleted = false,
                FechaCreacion = ahora,
                FechaModificacion = ahora,
                UsuarioCreador = Recortar(usuario, 50) ?? "Sistema",
                UsuarioModificador = Recortar(usuario, 50) ?? "Sistema",
            };
            db.Naves.Add(nueva);
            catalogo.Add(nueva);
            return (nueva, true);
        }

        private static string? Recortar(string? s, int largo) =>
            string.IsNullOrWhiteSpace(s) ? null : (s.Length > largo ? s[..largo] : s);

        // Distancia de Levenshtein (nombres cortos: el costo es despreciable).
        private static int Distancia(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
