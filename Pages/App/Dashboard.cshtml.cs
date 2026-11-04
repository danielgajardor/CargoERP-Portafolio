using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AtilsonCargoSpa.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtilsonCargoSpa.Pages.App
{
    public class DashboardModel : PageModel
    {
        private readonly AtilsonContext _context;

        public DashboardModel(AtilsonContext context)
        {
            _context = context;
        }

        // ==========================================
        // PROPIEDADES FUERTEMENTE TIPADAS PARA LA VISTA
        // ==========================================

        // KPIs Generales
        public int TotalBookings { get; set; }
        public int TotalClientes { get; set; }
        public int CantidadCriticos { get; set; }
        public List<Operacione> UltimasOperaciones { get; set; } = new();

        // Naves en curso (oct-2026, minuta Portal Web de Clientes: "todos estos
        // datos quedan vinculados en el panel de control bajo la identificación
        // de la nave"). Operaciones activas agrupadas por naviera + nave, con su
        // booking, cliente y propietario de contrato, más las solicitudes de
        // reserva del portal para esa misma nave que todavía no son operación.
        public class NaveEnCurso
        {
            public string Nave { get; set; } = "";
            public string? Naviera { get; set; }
            public string Color { get; set; } = "#6d2d9e";
            public DateTime? Etd { get; set; }
            public DateTime? Eta { get; set; }
            public string Ruta { get; set; } = "";
            public List<Operacione> Operaciones { get; set; } = new();
            public int SolicitudesPortal { get; set; }
        }
        public List<NaveEnCurso> NavesEnCurso { get; set; } = new();

        // 1. Mix de Servicios
        public int CountIntegral { get; set; }
        public int CountMaritimo { get; set; }
        public int CountTerrestre { get; set; }
        public int CountDocumental { get; set; }

        // 2. Control Marítimo (LAR)
        public int CountMaritimoATiempo { get; set; }
        public int CountMaritimoConLAR { get; set; }

        // 3. Documental (Matrices)
        public int CountMatricesConfirmadas { get; set; }
        public int CountMatricesPendientes { get; set; }
        public int TotalMatrices => CountMatricesConfirmadas + CountMatricesPendientes;
        public double PctConfirmadas => TotalMatrices > 0 ? Math.Round(((double)CountMatricesConfirmadas / TotalMatrices) * 100) : 0;

        // 4. Estado de Bookings (Workflow)
        public int CountPendienteNaviera { get; set; }
        public int CountConfirmadoCliente { get; set; }
        public int CountCanceladas { get; set; }

        // ==========================================
        // Centro de mando (oct-2026): lo que hay que atender y la agenda de la semana
        // ==========================================
        public record Pendiente(string Icono, string Titulo, string Detalle, int Cantidad, string Url, string Tono);
        public List<Pendiente> Pendientes { get; set; } = new();

        public record EventoAgenda(DateTime Fecha, string Tipo, string Icono, Operacione Op);
        public List<EventoAgenda> Agenda { get; set; } = new();

        // ==========================================
        // KPIs del mes (oct-2026): mes en curso contra el mismo tramo del mes anterior
        // (del día 1 al día de hoy), para que la comparación sea justa a mitad de mes.
        // ==========================================
        public record MesActividad(DateTime Mes, int Creadas, int Zarpes);
        public List<MesActividad> Tendencia { get; set; } = new();
        public int OpsMes { get; set; }
        public int OpsMesAnterior { get; set; }
        public int ZarpadosMes { get; set; }
        public int ZarpadosMesAnterior { get; set; }
        public decimal ToneladasMes { get; set; }
        public decimal ToneladasMesAnterior { get; set; }
        public bool HayPesos { get; set; }
        public int PctATiempo => (CountMaritimoATiempo + CountMaritimoConLAR) > 0
            ? (int)Math.Round(CountMaritimoATiempo * 100.0 / (CountMaritimoATiempo + CountMaritimoConLAR)) : 100;
        public int PctBookingsConfirmados => (CountConfirmadoCliente + CountPendienteNaviera) > 0
            ? (int)Math.Round(CountConfirmadoCliente * 100.0 / (CountConfirmadoCliente + CountPendienteNaviera)) : 0;

        public record Ranking(string Nombre, int Cantidad, string Color);
        public List<Ranking> TopClientes { get; set; } = new();
        public List<Ranking> TopNavieras { get; set; } = new();

        public int ZarpesSemana { get; set; }
        public int ClientesConOperaciones { get; set; }
        public int MatricesUrgentes { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                // Query base para ignorar los eliminados
                var queryActivas = _context.Operaciones.Where(o => o.Activo == 1 || o.Activo == null);

                TotalBookings = await queryActivas.CountAsync();
                TotalClientes = await _context.Clientes.CountAsync();

                DateTime limite = DateTime.Now.AddDays(2);
                CantidadCriticos = await queryActivas
                    .CountAsync(o => o.FechaStacking >= DateTime.Now && o.FechaStacking <= limite);

                // ==========================================
                // CÁLCULO DE MÉTRICAS PARA GRÁFICOS
                // ==========================================

                // 1. Tipos de Servicio (Mix Comercial)
                CountIntegral = await queryActivas.CountAsync(o => o.IdTipoServicio == 1 || o.IdTipoServicio == 8);
                CountMaritimo = await queryActivas.CountAsync(o => o.IdTipoServicio == 5 || o.IdTipoServicio == 12);
                CountTerrestre = await queryActivas.CountAsync(o => o.IdTipoServicio == 6 || o.IdTipoServicio == 13);
                CountDocumental = await queryActivas.CountAsync(o => o.IdTipoServicio == 7 || o.IdTipoServicio == 14);

                // 2. Control LAR (Solo operaciones con tramo marítimo)
                var queryMaritimas = queryActivas.Where(o => new[] { 1, 2, 3, 5, 8, 9, 10, 12 }.Contains(o.IdTipoServicio ?? 0));
                int totalMaritimas = await queryMaritimas.CountAsync();
                CountMaritimoConLAR = await queryMaritimas.CountAsync(o => o.EstadoLar != null && o.EstadoLar.Contains("LAR"));
                CountMaritimoATiempo = totalMaritimas - CountMaritimoConLAR;

                // 3. Desempeño Documental (Matrices B/L)
                var queryConMatriz = queryActivas.Where(o => new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 14 }.Contains(o.IdTipoServicio ?? 0));
                CountMatricesConfirmadas = await queryConMatriz.CountAsync(o => o.OperacionesDocumentales.Any(d => d.MatrizPresentada == true));
                CountMatricesPendientes = await queryConMatriz.CountAsync() - CountMatricesConfirmadas;

                // 4. Estatus de Bookings / Workflow
                CountPendienteNaviera = await queryActivas.CountAsync(o => o.EstadoWorkflow != null && o.EstadoWorkflow.Contains("Solicitado"));
                CountConfirmadoCliente = await queryActivas.CountAsync(o => o.EstadoWorkflow != null && o.EstadoWorkflow.Contains("Confirmado"));
                CountCanceladas = await _context.Operaciones.CountAsync(o => o.EstadoWorkflow != null && o.EstadoWorkflow.Contains("Cancelado"));

                // ==========================================
                // OBTENCIÓN DE ÚLTIMAS OPERACIONES (TABLA)
                // ==========================================
                UltimasOperaciones = await queryActivas
                    .Include(o => o.IdClienteNavigation)
                    .Include(o => o.IdPuertoDestinoNavigation)
                    .OrderByDescending(o => o.Id)
                    .Take(8)
                    .ToListAsync();

                await CargarKpisMesAsync(queryActivas);
                await CargarNavesEnCursoAsync(queryActivas);
                await CargarAgendaYPendientesAsync(queryActivas);
            }
            catch (Exception ex)
            {
                UltimasOperaciones = new List<Operacione>();
                Console.WriteLine(ex.Message);
            }
        }

        // Agenda de 7 días (stacking, cut-off de matriz, zarpes y arribos) y lista de
        // pendientes con enlace directo. Cada bloque es independiente: si uno falla
        // (por ejemplo, falta un script SQL), el resto del Dashboard se muestra igual.
        private async Task CargarAgendaYPendientesAsync(IQueryable<Operacione> queryActivas)
        {
            var hoy = DateTime.Today;
            var fin = hoy.AddDays(8);
            try
            {
                var vigentes = queryActivas.AsNoTracking()
                    .Where(o => o.EstadoWorkflow == null || (!o.EstadoWorkflow.ToUpper().Contains("CANCEL") && !o.EstadoWorkflow.ToUpper().Contains("FINALIZ")));
                var ops = await vigentes
                    .Include(o => o.IdClienteNavigation)
                    .Include(o => o.IdPuertoOrigenNavigation)
                    .Include(o => o.IdPuertoDestinoNavigation)
                    .Include(o => o.OperacionesDocumentales)
                    .Where(o => (o.FechaStacking >= hoy && o.FechaStacking < fin) || (o.CutOffMatriz >= hoy && o.CutOffMatriz < fin)
                             || (o.EtdPol >= hoy && o.EtdPol < fin) || (o.EtaPod >= hoy && o.EtaPod < fin))
                    .Take(400)
                    .ToListAsync();
                bool EnRango(DateTime? f) => f.HasValue && f.Value >= hoy && f.Value < fin;
                foreach (var o in ops)
                {
                    if (EnRango(o.FechaStacking)) Agenda.Add(new EventoAgenda(o.FechaStacking!.Value, "Stacking", "bi-box-arrow-in-down", o));
                    if (EnRango(o.CutOffMatriz) && !(o.OperacionesDocumentales?.Any(d => d.MatrizPresentada == true) ?? false))
                        Agenda.Add(new EventoAgenda(o.CutOffMatriz!.Value, "Cut-off matriz", "bi-file-earmark-text", o));
                    if (EnRango(o.EtdPol)) Agenda.Add(new EventoAgenda(o.EtdPol!.Value, "Zarpe", "bi-water", o));
                    if (EnRango(o.EtaPod)) Agenda.Add(new EventoAgenda(o.EtaPod!.Value, "Arribo", "bi-geo-alt", o));
                }
                Agenda = Agenda.OrderBy(e => e.Fecha).ThenBy(e => e.Tipo).ToList();
                ZarpesSemana = Agenda.Count(e => e.Tipo == "Zarpe");
                MatricesUrgentes = Agenda.Count(e => e.Tipo == "Cut-off matriz" && e.Fecha < hoy.AddDays(3));
                ClientesConOperaciones = await vigentes.Select(o => o.IdCliente).Distinct().CountAsync();
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }

            void Agregar(string icono, string titulo, string detalle, int n, string url, string tono)
            {
                if (n > 0) Pendientes.Add(new Pendiente(icono, titulo, detalle, n, url, tono));
            }
            Agregar("bi-alarm", "Stacking en las próximas 48 h", "Contenedores que deben entrar a puerto", CantidadCriticos, "/Operaciones/Index", "urgente");
            Agregar("bi-file-earmark-excl", "Cut-off de matriz en menos de 3 días", "Matrices aún no presentadas", MatricesUrgentes, "/Operaciones/Documental", "urgente");
            Agregar("bi-hourglass-split", "Bookings por confirmar con la naviera", "En estado Solicitado", CountPendienteNaviera, "/Operaciones/Index", "aviso");

            try
            {
                int reservas = await _context.Cotizaciones.AsNoTracking().CountAsync(c => c.Activo == true && c.Estado == "NUEVA SOLICITUD" && c.TipoSolicitud == Cotizacion.TipoReserva);
                int cotizaciones = await _context.Cotizaciones.AsNoTracking().CountAsync(c => c.Activo == true && c.Estado == "NUEVA SOLICITUD" && c.TipoSolicitud != Cotizacion.TipoReserva);
                Agregar("bi-calendar-plus", "Reservas del portal por crear", "Clientes que pidieron espacio en una nave", reservas, "/Operaciones/Cotizaciones?filtro=pendientes", "aviso");
                Agregar("bi-calculator", "Cotizaciones del portal por responder", "Solicitudes nuevas de clientes", cotizaciones, "/Operaciones/Cotizaciones?filtro=pendientes", "aviso");
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }

            var sp = HttpContext.RequestServices;
            if (sp.GetService(typeof(AtilsonCargoSpa.Services.SoporteClientesService)) is AtilsonCargoSpa.Services.SoporteClientesService soporte)
                Agregar("bi-headset", "Consultas de clientes por responder", "Mensajes en Soporte clientes", await soporte.ContarAbiertosAsync(), "/App/Soporte", "aviso");
            if (sp.GetService(typeof(AtilsonCargoSpa.Services.RegistroClientesService)) is AtilsonCargoSpa.Services.RegistroClientesService registro)
            {
                Agregar("bi-building-add", "Empresas esperando aprobación", "Registros desde el portal", await registro.ContarPendientesAsync(), "/App/Clientes/Solicitudes", "info");
                var (plantas, depositos) = await registro.ContarUbicacionesPorValidarAsync();
                Agregar("bi-geo", "Plantas y depósitos por validar", "Registrados por clientes", plantas + depositos, plantas > 0 ? "/App/Plantas/Index" : "/App/Depositos/Index", "info");
            }
            if (AtilsonCargoSpa.Helpers.PermisosEquipo.EsJefatura(User)
                && sp.GetService(typeof(AtilsonCargoSpa.Services.TraspasoFinanzasService)) is AtilsonCargoSpa.Services.TraspasoFinanzasService traspaso)
                Agregar("bi-arrow-left-right", "Bookings nuevos para Finanzas", "Traspasos de Operaciones sin revisar", await traspaso.ContarPendientesAsync(), "/Finanzas/ControlFinanciero", "info");
            if (AtilsonCargoSpa.Helpers.PermisosEquipo.EsAdmin(User)
                && sp.GetService(typeof(AtilsonCargoSpa.Services.RegistroEquipoService)) is AtilsonCargoSpa.Services.RegistroEquipoService equipo)
                Agregar("bi-person-plus", "Cuentas del equipo por validar", "Solicitudes en Equipo Atilson", await equipo.ContarPendientesAsync(), "/App/Equipo/Index", "info");
        }

        // Operaciones creadas, contenedores zarpados y toneladas: mes en curso, mismo tramo
        // del mes anterior y tendencia de 6 meses. Clientes y navieras de los últimos 90 días.
        // Excluye canceladas. Una operación cuenta como zarpada con su ETD real (o la ETD
        // programada si aún no se registra la real) ya cumplida.
        private async Task CargarKpisMesAsync(IQueryable<Operacione> queryActivas)
        {
            try
            {
                var ahora = DateTime.Now;
                var hoy = DateTime.Today;
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
                var inicioAnterior = inicioMes.AddMonths(-1);
                // Mismo tramo del mes anterior: del 1 al mismo día (o al último día si ese mes es más corto).
                var finTramoAnterior = inicioAnterior.AddDays(Math.Min(hoy.Day, DateTime.DaysInMonth(inicioAnterior.Year, inicioAnterior.Month)) - 1)
                                       .Add(ahora.TimeOfDay);
                var desde6 = inicioMes.AddMonths(-5);
                var desde90 = hoy.AddDays(-90);

                var datos = await queryActivas.AsNoTracking()
                    .Where(o => o.EstadoWorkflow == null || !o.EstadoWorkflow.ToUpper().Contains("CANCEL"))
                    .Where(o => o.FechaCreacion >= desde6 || o.EtdReal >= desde6 || o.EtdPol >= desde6)
                    .Select(o => new
                    {
                        o.FechaCreacion,
                        Zarpe = o.EtdReal ?? o.EtdPol,
                        o.Kilos,
                        o.IdCliente,
                        Cliente = o.IdClienteNavigation != null ? (o.IdClienteNavigation.NombreCliente ?? o.IdClienteNavigation.RazonSocial) : null,
                        o.IdNaviera,
                        Naviera = o.IdNavieraNavigation
                    })
                    .Take(20000)
                    .ToListAsync();

                bool Entre(DateTime? f, DateTime a, DateTime b) => f.HasValue && f.Value >= a && f.Value <= b;
                var zarpadosMes = datos.Where(d => Entre(d.Zarpe, inicioMes, ahora)).ToList();
                var zarpadosAnt = datos.Where(d => Entre(d.Zarpe, inicioAnterior, finTramoAnterior)).ToList();
                OpsMes = datos.Count(d => Entre(d.FechaCreacion, inicioMes, ahora));
                OpsMesAnterior = datos.Count(d => Entre(d.FechaCreacion, inicioAnterior, finTramoAnterior));
                ZarpadosMes = zarpadosMes.Count;
                ZarpadosMesAnterior = zarpadosAnt.Count;
                HayPesos = datos.Any(d => d.Kilos > 0);
                ToneladasMes = Math.Round(zarpadosMes.Sum(d => d.Kilos ?? 0) / 1000m, 1);
                ToneladasMesAnterior = Math.Round(zarpadosAnt.Sum(d => d.Kilos ?? 0) / 1000m, 1);

                for (int i = 0; i < 6; i++)
                {
                    var m = desde6.AddMonths(i);
                    var fin = i == 5 ? ahora : m.AddMonths(1).AddTicks(-1);
                    Tendencia.Add(new MesActividad(m,
                        datos.Count(d => Entre(d.FechaCreacion, m, fin)),
                        datos.Count(d => Entre(d.Zarpe, m, fin))));
                }

                var recientes = datos.Where(d => d.FechaCreacion >= desde90).ToList();
                TopClientes = recientes
                    .GroupBy(d => d.IdCliente)
                    .Select(g => new Ranking(g.First().Cliente ?? "Sin cliente", g.Count(), "#443094"))
                    .OrderByDescending(r => r.Cantidad).ThenBy(r => r.Nombre).Take(5).ToList();
                TopNavieras = recientes
                    .GroupBy(d => d.IdNaviera)
                    .Select(g => new Ranking(g.First().Naviera?.NombreNaviera ?? "Por confirmar", g.Count(),
                        AtilsonCargoSpa.Helpers.ItinerariosHelper.Color(g.First().Naviera)))
                    .OrderByDescending(r => r.Cantidad).ThenBy(r => r.Nombre).Take(5).ToList();
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        private async Task CargarNavesEnCursoAsync(IQueryable<Operacione> queryActivas)
        {
            try
            {
                await CargarNavesEnCursoInternoAsync(queryActivas);
            }
            catch (Exception ex)
            {
                // El panel de naves es complementario: si falla no debe tumbar el resto del Dashboard.
                NavesEnCurso = new List<NaveEnCurso>();
                Console.WriteLine(ex.Message);
            }
        }

        private async Task CargarNavesEnCursoInternoAsync(IQueryable<Operacione> queryActivas)
        {
            // Naves que zarpan desde hace 10 días hasta 60 días más, o sin ETD
            // todavía cargada (creadas el último mes). Excluye canceladas y cerradas.
            var desde = DateTime.Today.AddDays(-10);
            var hasta = DateTime.Today.AddDays(60);
            var desdeCreacion = DateTime.Today.AddDays(-30);

            var ops = await queryActivas.AsNoTracking()
                .Include(o => o.IdClienteNavigation)
                .Include(o => o.IdNavieraNavigation)
                .Include(o => o.IdPuertoOrigenNavigation)
                .Include(o => o.IdPuertoDestinoNavigation)
                .Where(o => o.Nave != null && o.Nave != "")
                .Where(o => o.EstadoWorkflow == null || (!o.EstadoWorkflow.ToUpper().Contains("CANCEL") && !o.EstadoWorkflow.ToUpper().Contains("FINALIZ")))
                .Where(o => (o.EtdPol >= desde && o.EtdPol <= hasta) || (o.EtdPol == null && o.FechaCreacion >= desdeCreacion))
                .OrderBy(o => o.EtdPol)
                .Take(300)
                .ToListAsync();

            var pendientesPortal = await _context.Cotizaciones.AsNoTracking()
                .Where(c => c.TipoSolicitud == Cotizacion.TipoReserva && c.Estado == "NUEVA SOLICITUD" && c.Activo == true && c.Nave != null)
                .Select(c => c.Nave!)
                .ToListAsync();
            var pendientesPorNave = pendientesPortal
                .GroupBy(n => n.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.Count());

            NavesEnCurso = ops
                .GroupBy(o => new { o.IdNaviera, Nave = o.Nave!.Trim().ToUpper() })
                .Select(g =>
                {
                    var primera = g.First();
                    var rutas = g.Select(o => $"{o.IdPuertoOrigenNavigation?.NombrePuerto ?? "?"} → {o.IdPuertoDestinoNavigation?.NombrePuerto ?? "?"}")
                                 .Where(r => r != "? → ?").Distinct().ToList();
                    return new NaveEnCurso
                    {
                        Nave = g.Key.Nave,
                        Naviera = primera.IdNavieraNavigation?.NombreNaviera,
                        Color = AtilsonCargoSpa.Helpers.ItinerariosHelper.Color(primera.IdNavieraNavigation),
                        Etd = g.Min(o => o.EtdPol),
                        Eta = g.Max(o => o.EtaPod),
                        Ruta = rutas.Count == 0 ? "Ruta por confirmar" : rutas.Count == 1 ? rutas[0] : $"{rutas[0]} y {rutas.Count - 1} más",
                        Operaciones = g.OrderBy(o => o.NumeroBooking).ToList(),
                        SolicitudesPortal = pendientesPorNave.TryGetValue(g.Key.Nave, out var n) ? n : 0
                    };
                })
                .OrderBy(n => n.Etd ?? DateTime.MaxValue)
                .Take(12)
                .ToList();
        }
    }
}