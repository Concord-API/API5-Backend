using Microsoft.AspNetCore.Mvc.Testing;

namespace Ratio.Api.Tests;

public class StartupGuardTests
{
    [Fact]
    public void Refuses_to_start_without_a_connection_string()
    {
        using var factory = new WebApplicationFactory<Program>();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings__Ratio", exception.Message);
    }
}
