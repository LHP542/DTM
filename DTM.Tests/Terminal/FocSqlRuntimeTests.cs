using DTM.Data.Config;
using DTM.Data.Terminal;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Terminal;

public class FocSqlRuntimeTests : IDisposable
{
    private readonly FocSqlConfig _original = FocSqlRuntime.Current;

    public void Dispose() => FocSqlRuntime.Current = _original;

    // --- BuildImportSnippet ---

    [Fact]
    public void BuildImportSnippet_ExplicitModulePath_UsesDirectImport()
    {
        FocSqlRuntime.Current = new FocSqlConfig { ModulePath = @"C:\Modules\FOC-SQL.psd1" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("Import-Module");
        snippet.Should().Contain(@"C:\Modules\FOC-SQL.psd1");
        snippet.Should().NotContain("Copy-Item");
    }

    [Fact]
    public void BuildImportSnippet_ExplicitPath_SingleQuoteEscaped()
    {
        FocSqlRuntime.Current = new FocSqlConfig { ModulePath = @"C:\It's\Module.psd1" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("It''s");
    }

    [Fact]
    public void BuildImportSnippet_NullSambaSource_UsesBuiltInDefault()
    {
        // The ?? DefaultSambaSource path fires only when SambaSource is null.
        FocSqlRuntime.Current = new FocSqlConfig { SambaSource = null!, ModulePath = string.Empty };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("samba01");
        snippet.Should().Contain("Copy-Item");
    }

    [Fact]
    public void BuildImportSnippet_EmptySambaSource_UsesCopyItemPath()
    {
        FocSqlRuntime.Current = new FocSqlConfig();
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("Copy-Item");
        snippet.Should().NotContain("Import-Module '");
    }

    [Fact]
    public void BuildImportSnippet_SambaSource_Set_UsesIt()
    {
        FocSqlRuntime.Current = new FocSqlConfig { SambaSource = @"\\myserver\share\FOC*" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain(@"\\myserver\share\FOC*");
        snippet.Should().NotContain("samba01");
    }

    [Fact]
    public void BuildImportSnippet_SambaSource_SingleQuote_Escaped()
    {
        FocSqlRuntime.Current = new FocSqlConfig { SambaSource = @"\\srv\it's\share" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("it''s");
    }

    [Fact]
    public void BuildImportSnippet_ContainsRemoveModule()
    {
        FocSqlRuntime.Current = new FocSqlConfig { SambaSource = @"\\srv\share" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("Remove-Module");
    }

    [Fact]
    public void BuildImportSnippet_ContainsImportModule()
    {
        FocSqlRuntime.Current = new FocSqlConfig { SambaSource = @"\\srv\share" };
        string snippet = FocSqlRuntime.BuildImportSnippet();
        snippet.Should().Contain("Import-Module");
    }

    // --- BuildCall ---

    [Fact]
    public void BuildCall_Immediate_ContainsImmediateFlag()
    {
        string call = FocSqlRuntime.BuildCall("Backup-Database", "MyDB", null);
        call.Should().Contain("-Immediate");
        call.Should().NotContain("-Time");
        call.Should().NotContain("-Date");
    }

    [Fact]
    public void BuildCall_WithTime_ContainsTimeAndDate()
    {
        var when = new DateTime(2026, 3, 15, 14, 30, 0);
        string call = FocSqlRuntime.BuildCall("Backup-Database", "MyDB", when);
        call.Should().Contain("-Time '14:30'");
        call.Should().Contain("-Date '15.03.2026'");
        call.Should().NotContain("-Immediate");
    }

    [Fact]
    public void BuildCall_DatabaseName_SingleQuoteEscaped()
    {
        string call = FocSqlRuntime.BuildCall("Backup-Database", "DB's-Name", null);
        call.Should().Contain("DB''s-Name");
    }

    [Fact]
    public void BuildCall_FunctionName_InOutput()
    {
        string call = FocSqlRuntime.BuildCall("Sync-Database-ToTest", "MyDB", null);
        call.Should().StartWith("Sync-Database-ToTest");
    }
}
