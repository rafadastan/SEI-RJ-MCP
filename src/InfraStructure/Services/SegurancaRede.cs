using System.Net;
using System.Net.Sockets;

namespace SeiRjMonitor.Infrastructure.Services;

/// <summary>
/// Política de destinos de rede permitidos. Bloqueia loopback, redes privadas,
/// link-local, CGNAT, multicast, documentação e metadata de nuvem.
/// </summary>
public static class SegurancaRede
{
    private static readonly (IPAddress Rede, int Prefixo)[] RedesIpv4Bloqueadas =
    {
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4)
    };

    /// <summary>
    /// Retorna true apenas para endereços públicos roteáveis na internet.
    /// </summary>
    public static bool EnderecoPublicoPermitido(IPAddress endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);

        if (endereco.IsIPv4MappedToIPv6)
            endereco = endereco.MapToIPv4();

        if (endereco.AddressFamily == AddressFamily.InterNetworkV6)
            return Ipv6PublicoPermitido(endereco);

        if (endereco.AddressFamily != AddressFamily.InterNetwork)
            return false;

        foreach (var (rede, prefixo) in RedesIpv4Bloqueadas)
        {
            if (EstaNaRede(endereco, rede, prefixo))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolve o host e rejeita o destino se qualquer endereço retornado for
    /// privado/reservado. Resolver todos os endereços evita que um registro DNS
    /// malicioso escolha um alvo interno no momento da conexão.
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> ResolverDestinoSeguroAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new HttpRequestException("Host de destino inválido.");

        IPAddress[] enderecos;
        try
        {
            enderecos = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException ex)
        {
            throw new HttpRequestException("Não foi possível resolver o domínio de destino.", ex);
        }

        if (enderecos.Length == 0)
            throw new HttpRequestException("O domínio de destino não retornou endereços.");

        foreach (var endereco in enderecos)
        {
            if (!EnderecoPublicoPermitido(endereco))
            {
                throw new HttpRequestException(
                    "O domínio de destino resolveu para um endereço de rede não permitido.");
            }
        }

        return enderecos;
    }

    private static bool Ipv6PublicoPermitido(IPAddress endereco)
    {
        if (IPAddress.IPv6Any.Equals(endereco)
            || IPAddress.IPv6Loopback.Equals(endereco)
            || endereco.IsIPv6LinkLocal
            || endereco.IsIPv6SiteLocal
            || endereco.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = endereco.GetAddressBytes();

        // fc00::/7 (unique local)
        if ((bytes[0] & 0xFE) == 0xFC)
            return false;

        // fe80::/10 (link-local)
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            return false;

        // 2001:db8::/32 (documentação)
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            return false;

        // Apenas unicast global 2000::/3 é roteável na internet.
        return (bytes[0] & 0xE0) == 0x20;
    }

    private static bool EstaNaRede(IPAddress endereco, IPAddress rede, int prefixo)
    {
        var a = endereco.GetAddressBytes();
        var b = rede.GetAddressBytes();

        if (a.Length != b.Length)
            return false;

        var bytesCompletos = prefixo / 8;
        var bitsRestantes = prefixo % 8;

        for (var i = 0; i < bytesCompletos; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        if (bitsRestantes == 0)
            return true;

        var mascara = (byte)(0xFF << (8 - bitsRestantes));
        return (a[bytesCompletos] & mascara) == (b[bytesCompletos] & mascara);
    }
}
