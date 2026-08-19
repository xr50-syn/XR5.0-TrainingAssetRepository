namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Covers the tenant-name to MySQL-identifier mapping in <see cref="XR50TenantDatabase"/>.
///
/// The mapping is lossy on purpose (anything outside [a-zA-Z0-9_] folds to '_'), so several
/// distinct tenant names can land on one database. Provisioning uses
/// CREATE DATABASE IF NOT EXISTS, which means an undetected collision does not fail - it
/// silently attaches the new tenant to the existing tenant's data. The collision key is what
/// the pre-creation guard in TenantsController compares on, so these cases pin down exactly
/// which names must be treated as already taken.
/// </summary>
public class TenantDatabaseNamingTests
{
    [Theory]
    [InlineData("acme", "xr50_tenant_acme")]
    [InlineData("acme_corp", "xr50_tenant_acme_corp")]
    [InlineData("acme-corp", "xr50_tenant_acme_corp")]
    [InlineData("3f2b1a90-7c4d-4e5f-9a6b-8d7c6e5f4a3b", "xr50_tenant_3f2b1a90_7c4d_4e5f_9a6b_8d7c6e5f4a3b")]
    public void SchemaFor_FoldsHyphensButPreservesEverythingElse(string tenantName, string expected)
    {
        XR50TenantDatabase.SchemaFor(tenantName).Should().Be(expected);
    }

    [Fact]
    public void SchemaFor_PreservesCase()
    {
        // The derived name must stay stable for tenants that already exist with mixed-case
        // names; only the collision COMPARISON folds case.
        XR50TenantDatabase.SchemaFor("Acme_Corp").Should().Be("xr50_tenant_Acme_Corp");
    }

    [Theory]
    [InlineData("acme_corp", "acme-corp")]      // '-' and '_' both derive '_'
    [InlineData("acme_corp", "Acme_Corp")]      // case folds under lower_case_table_names=1
    [InlineData("acme_corp", "ACME-CORP")]      // both at once
    [InlineData("acme-corp", "Acme_Corp")]      // transposed separator plus case
    public void CollisionKeyFor_TreatsSeparatorAndCaseVariantsAsTheSameDatabase(string first, string second)
    {
        XR50TenantDatabase.CollisionKeyFor(first)
            .Should().Be(XR50TenantDatabase.CollisionKeyFor(second));
    }

    [Theory]
    [InlineData("acme", "acme2")]
    [InlineData("acme_corp", "acme_corps")]
    [InlineData("acme", "acme_corp")]
    public void CollisionKeyFor_KeepsGenuinelyDifferentNamesApart(string first, string second)
    {
        XR50TenantDatabase.CollisionKeyFor(first)
            .Should().NotBe(XR50TenantDatabase.CollisionKeyFor(second));
    }

    [Fact]
    public void CollisionKeyFor_IsTheLowercasedSchemaName()
    {
        // The guard compares this against LOWER(SCHEMA_NAME) from INFORMATION_SCHEMA, so the
        // key must be fully lowercase or the comparison can never match.
        var key = XR50TenantDatabase.CollisionKeyFor("Acme-Corp");
        key.Should().Be("xr50_tenant_acme_corp");
        key.Should().Be(key.ToLowerInvariant());
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("acme_corp")]
    [InlineData("acme-corp")]
    [InlineData("ACME")]
    [InlineData("3f2b1a90-7c4d-4e5f-9a6b-8d7c6e5f4a3b")]
    public void IsAcceptableTenantName_AcceptsLettersDigitsUnderscoresAndHyphens(string tenantName)
    {
        XR50TenantDatabase.IsAcceptableTenantName(tenantName).Should().BeTrue();
    }

    [Theory]
    [InlineData("acme corp")]
    [InlineData("acme.corp")]
    [InlineData("acme@corp")]
    [InlineData("acme/corp")]
    [InlineData("acme$corp")]
    [InlineData("acme`corp")]
    [InlineData("")]
    public void IsAcceptableTenantName_RejectsAnythingElse(string tenantName)
    {
        XR50TenantDatabase.IsAcceptableTenantName(tenantName).Should().BeFalse();
    }
}
