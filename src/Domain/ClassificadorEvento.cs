namespace SeiRjMonitor.Domain;

/// <summary>
/// Classifica a descrição de um andamento em um tipo de evento conhecido.
/// </summary>
public static class ClassificadorEvento
{
    public static string Classificar(string descricao)
    {
        var texto = (descricao ?? string.Empty).ToLowerInvariant();

        if (texto.Contains("reabert")) return "Reabertura";
        if (texto.Contains("conclu")) return "Conclusão";
        if (texto.Contains("recebido") || texto.Contains("recebimento")) return "Recebimento";
        if (texto.Contains("remetido") || texto.Contains("remessa") || texto.Contains("enviado")) return "Remessa";
        if (texto.Contains("assinatura") || texto.Contains("assinado")) return "Assinatura";
        if (texto.Contains("gerado") || texto.Contains("geração")) return "Geração";
        if (texto.Contains("anexado") || texto.Contains("inclusão")) return "Inclusão";

        return "Outro";
    }
}
