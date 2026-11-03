using AtilsonCargoSpa.Models;
using AtilsonCargoSpa.Pages.Shared;
using AtilsonCargoSpa.Services;

namespace AtilsonCargoSpa.Pages.App
{
    // Mi perfil del equipo (oct-2026). Lógica en Pages/Shared/PerfilPageModel.cs.
    public class PerfilModel : PerfilPageModel
    {
        public PerfilModel(AtilsonContext context, PerfilUsuarioService perfiles) : base(context, perfiles) { }
        public override bool EsCliente => false;
    }
}
