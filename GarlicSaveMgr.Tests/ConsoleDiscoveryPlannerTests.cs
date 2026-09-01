using System.Net;
using GarlicSaveMgr.Services;
using Xunit;

namespace GarlicSaveMgr.Tests;

public sealed class ConsoleDiscoveryPlannerTests
{
    [Fact]
    public void BuildQuickCandidates_UsesLocal24AndPrioritizesLocalIpAndGateway()
    {
        var network = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("192.168.50.20"),
            IPAddress.Parse("255.255.255.0"),
            [IPAddress.Parse("192.168.50.1")]);

        var candidates = ConsoleDiscoveryPlanner.BuildQuickCandidates([network]);

        Assert.Equal("192.168.50.20", candidates[0]);
        Assert.Equal("192.168.50.1", candidates[1]);
        Assert.Contains("192.168.50.2", candidates);
        Assert.DoesNotContain("192.168.51.2", candidates);
    }

    [Fact]
    public void BuildQuickCandidates_UsesHostOctet_WhenSubnetMaskIs16()
    {
        var network = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("10.23.45.67"),
            IPAddress.Parse("255.255.0.0"),
            []);

        var candidates = ConsoleDiscoveryPlanner.BuildQuickCandidates([network]);

        Assert.Contains("10.23.45.1", candidates);
        Assert.Contains("10.23.45.67", candidates);
        Assert.DoesNotContain("10.23.0.1", candidates);
    }

    [Fact]
    public void BuildQuickCandidates_CoversMultipleInterfacesAndRemovesDuplicates()
    {
        var first = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("192.168.1.20"),
            IPAddress.Parse("255.255.255.0"),
            [IPAddress.Parse("192.168.1.1")]);
        var second = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("192.168.1.30"),
            IPAddress.Parse("255.255.255.0"),
            [IPAddress.Parse("192.168.1.1")]);
        var third = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("192.168.10.40"),
            IPAddress.Parse("255.255.255.0"),
            [IPAddress.Parse("192.168.10.1")]);

        var candidates = ConsoleDiscoveryPlanner.BuildQuickCandidates([first, second, third]);

        Assert.Equal(1, candidates.Count(x => x == "192.168.1.1"));
        Assert.Contains("192.168.1.20", candidates);
        Assert.Contains("192.168.1.30", candidates);
        Assert.Contains("192.168.10.40", candidates);
        Assert.Contains("192.168.10.1", candidates);
    }

    [Fact]
    public void BuildExpandedCandidates_EnumeratesFull16Subnet_AfterQuickPass()
    {
        var network = new ConsoleDiscoveryPlanner.NetworkSnapshot(
            IPAddress.Parse("10.23.45.67"),
            IPAddress.Parse("255.255.0.0"),
            []);

        var candidates = ConsoleDiscoveryPlanner.BuildExpandedCandidates([network], ["10.23.45.67"]);

        Assert.Contains("10.23.0.1", candidates);
        Assert.Contains("10.23.255.254", candidates);
        Assert.DoesNotContain("10.23.45.67", candidates);
    }

    [Fact]
    public void BuildWideCandidates_EnumeratesEntire19216816Range_WithoutSkippingHosts()
    {
        var networks = new[]
        {
            new ConsoleDiscoveryPlanner.NetworkSnapshot(
                IPAddress.Parse("192.168.1.20"),
                IPAddress.Parse("255.255.255.0"),
                [IPAddress.Parse("192.168.1.1")])
        };

        var quick = ConsoleDiscoveryPlanner.BuildQuickCandidates(networks);
        var wide = ConsoleDiscoveryPlanner.BuildWideCandidates(networks, quick);

        Assert.Contains("192.168.0.1", wide);
        Assert.Contains("192.168.127.123", wide);
        Assert.Contains("192.168.50.123", wide);
        Assert.Contains("192.168.255.254", wide);
        Assert.DoesNotContain("192.168.1.20", wide);
        Assert.DoesNotContain("192.168.1.1", wide);
    }
}
