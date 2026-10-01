using OpenLimiter.Core.Naming;

namespace OpenLimiter.Core.Tests;

public sealed class OwnedPolicyNameTests
{
    [Fact]
    public void Names_are_stable_and_scoped_to_the_product()
    {
        var ruleId = Guid.Parse("f18fb3b4-ae54-47fd-a04f-87b2eb781714");

        Assert.Equal("OpenLimiter:fw:f18fb3b4ae5447fda04f87b2eb781714:in", OwnedPolicyName.Firewall(ruleId, "in"));
        Assert.Equal(OwnedPolicyName.Qos(ruleId), OwnedPolicyName.Qos(ruleId));
        Assert.StartsWith("OpenLimiter-qos-", OwnedPolicyName.Qos(ruleId));
    }
}

