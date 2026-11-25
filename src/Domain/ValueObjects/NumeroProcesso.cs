namespace SeiRjMonitor.Domain.ValueObjects;

/// <summary>
/// Value object que representa o número de um processo no SEI-RJ.
/// </summary>
public sealed record NumeroProcesso(string Valor)
{
    public static NumeroProcesso? Criar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var normalizado = valor.Trim();
        if (normalizado.Length == 0)
            return null;

        return new NumeroProcesso(normalizado);
    }
}
