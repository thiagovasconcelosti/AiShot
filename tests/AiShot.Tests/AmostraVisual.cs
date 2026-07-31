using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AiShot.Tests;

/// <summary>
/// Gravação das amostras visuais geradas pelos testes.
/// </summary>
/// <remarks>
/// As amostras existem para conferência a olho, e sobrescrever sempre o mesmo
/// arquivo falha quando ele está aberto num visualizador — o teste quebrava por
/// causa do bloqueio, não do desenho. Uma amostra que derruba a suíte (e, por
/// tabela, o fluxo de release) atrapalha mais do que ajuda.
/// </remarks>
internal static class AmostraVisual
{
    /// <summary>
    /// Grava a amostra na saída de teste e devolve o caminho. Cai para um nome
    /// alternativo quando o arquivo preferido está bloqueado.
    /// </summary>
    public static string Gravar(Bitmap imagem, string nome)
    {
        var preferido = Path.Combine(AppContext.BaseDirectory, nome);

        try
        {
            imagem.Save(preferido, ImageFormat.Png);
            return preferido;
        }
        catch (Exception ex) when (ex is IOException or ExternalException)
        {
            // Arquivo aberto em outro processo: grava ao lado, com sufixo, em
            // vez de falhar. A amostra continua disponível para conferência.
            var alternativo = Path.Combine(
                AppContext.BaseDirectory,
                Path.GetFileNameWithoutExtension(nome)
                    + "-" + Guid.NewGuid().ToString("N")[..8]
                    + Path.GetExtension(nome));

            imagem.Save(alternativo, ImageFormat.Png);
            return alternativo;
        }
    }
}
