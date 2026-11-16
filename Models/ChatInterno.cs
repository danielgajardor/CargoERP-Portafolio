using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AtilsonCargoSpa.Models
{
    // Chat del equipo (nov-2026). Ver Sql/2026-11_chat_interno.sql y Services/ChatInternoService.cs.

    [Table("ChatConversaciones")]
    public class ChatConversacion
    {
        public const string TipoPrivada = "PRIVADA";
        public const string TipoGeneral = "GENERAL";

        public int Id { get; set; }
        public string Tipo { get; set; } = TipoPrivada;
        public string? Nombre { get; set; }
        /// <summary>"idMenor-idMayor" en las privadas, para no duplicarlas.</summary>
        public string? ClavePrivada { get; set; }
        public int? IdCreador { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaUltimoMensaje { get; set; }
    }

    [Table("ChatParticipantes")]
    public class ChatParticipante
    {
        public int Id { get; set; }
        public int IdConversacion { get; set; }
        public int IdUsuario { get; set; }
        /// <summary>Id del último mensaje que la persona vio en esta conversación.</summary>
        public int UltimoLeidoId { get; set; }
        public DateTime FechaIngreso { get; set; }
    }

    [Table("ChatMensajes")]
    public class ChatMensaje
    {
        public const int LargoMaximo = 2000;

        public int Id { get; set; }
        public int IdConversacion { get; set; }
        public int IdUsuario { get; set; }
        public string Texto { get; set; } = "";
        public DateTime FechaEnvio { get; set; }
    }
}
