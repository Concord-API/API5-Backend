using Ratio.Application.Abstractions;
using Ratio.Application.Coverage;

namespace Ratio.Application.Tests.Coverage;

public class DeclaredScopeTests
{
    private static readonly ScopeCourt Tjmg = new("TJMG", "Tribunal de Justiça de Minas Gerais", "MG");
    private static readonly ScopeCourt Tjrj = new("TJRJ", "Tribunal de Justiça do Rio de Janeiro", "RJ");
    private static readonly ScopeCourt Tjsp = new("TJSP", "Tribunal de Justiça de São Paulo", "SP");

    [Fact]
    public void Names_every_court_in_the_statement()
    {
        var scope = DeclaredScope.Describe([Tjmg, Tjrj, Tjsp]);

        Assert.Equal("TJMG, TJRJ e TJSP", scope.Statement);
    }

    [Fact]
    public void Joins_two_courts_with_e()
    {
        var scope = DeclaredScope.Describe([Tjrj, Tjsp]);

        Assert.Equal("TJRJ e TJSP", scope.Statement);
    }

    [Fact]
    public void Names_a_single_court_alone()
    {
        var scope = DeclaredScope.Describe([Tjsp]);

        Assert.Equal("TJSP", scope.Statement);
    }

    [Fact]
    public void Keeps_the_courts_as_read()
    {
        var scope = DeclaredScope.Describe([Tjmg, Tjrj, Tjsp]);

        Assert.Equal([Tjmg, Tjrj, Tjsp], scope.Courts);
    }

    [Fact]
    public void Declares_the_civil_subject()
    {
        var scope = DeclaredScope.Describe([Tjsp]);

        Assert.Equal("cível", scope.Subject);
    }

    [Fact]
    public void Has_no_statement_without_courts()
    {
        var scope = DeclaredScope.Describe([]);

        Assert.Empty(scope.Courts);
        Assert.Equal("cível", scope.Subject);
        Assert.Null(scope.Statement);
    }
}
