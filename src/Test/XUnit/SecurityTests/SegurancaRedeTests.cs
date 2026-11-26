using System.Net;
using SeiRjMonitor.Infrastructure.Services;
using Xunit;

namespace SeiRjMonitor.Tests.SecurityTests;

public class SegurancaRedeTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void DeveBloquearEnderecosNaoPublicos(string endereco)
    {
        Assert.False(SegurancaRede.EnderecoPublicoPermitido(IPAddress.Parse(endereco)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("200.160.2.3")]
    [InlineData("2804:14c:1::1")]
    [InlineData("2001:4860:4860::8888")]
    public void DevePermitirEnderecosPublicos(string endereco)
    {
        Assert.True(SegurancaRede.EnderecoPublicoPermitido(IPAddress.Parse(endereco)));
    }
}
